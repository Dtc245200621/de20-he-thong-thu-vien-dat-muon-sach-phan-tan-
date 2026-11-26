using Library.Contracts;
using Library.Data;
using Library.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("LibraryDb")
    ?? "Host=localhost;Port=5432;Database=library;Username=library;Password=library_dev";
builder.Services.AddDbContextFactory<LibraryDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "moc.library.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options => options.AddPolicy("auth", context =>
    RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
    })));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqOptions>>().Value);
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddHostedService<OutboxPublisherService>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

// Retry transient database startup races while Postgres becomes healthy.
for (var attempt = 1; ; attempt++)
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        await LibrarySeeder.InitializeAsync(db, enableDemoAccounts: app.Environment.IsDevelopment());
        break;
    }
    catch when (attempt < 30) { await Task.Delay(TimeSpan.FromSeconds(2)); }
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/auth/register", async (RegisterUserRequest request, HttpContext http, IDbContextFactory<LibraryDbContext> factory) =>
{
    var name = request.Name?.Trim(); var email = request.Email?.Trim().ToLowerInvariant(); var phone = new string((request.Phone ?? "").Where(char.IsDigit).ToArray());
    if (string.IsNullOrWhiteSpace(name) || name.Length > 160 || string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > 254 || phone.Length is < 9 or > 15 || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        return Results.BadRequest(new { error = "Vui lòng nhập họ tên, email hợp lệ, số điện thoại 9–15 số và mật khẩu tối thiểu 8 ký tự." });
    await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
    if (await db.Users.AnyAsync(u => u.Email.ToLower() == email || u.Phone == phone, http.RequestAborted))
        return Results.Conflict(new { error = "Email hoặc số điện thoại đã được đăng ký." });
    var user = new LibraryUser { Id = Guid.NewGuid(), Name = name, Email = email, Phone = phone, Role = RoleNames.Reader, IsActive = true };
    user.PasswordHash = new PasswordHasher<LibraryUser>().HashPassword(user, request.Password);
    db.Users.Add(user);
    try { await db.SaveChangesAsync(http.RequestAborted); }
    catch (DbUpdateException) { return Results.Conflict(new { error = "Email hoặc số điện thoại đã được đăng ký." }); }
    var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Name), new Claim(ClaimTypes.Email, user.Email), new Claim("phone", user.Phone), new Claim(ClaimTypes.Role, user.Role) };
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Created("/auth/me", new { user = new UserDto(user.Id, user.Name, user.Email, user.Phone), role = user.Role });
}).RequireRateLimiting("auth");
app.MapPost("/auth/login", async (LoginRequest request, HttpContext http, IDbContextFactory<LibraryDbContext> factory) =>
{
    if (string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrEmpty(request.Password)) return Results.BadRequest(new { error = "Nhập email hoặc số điện thoại và mật khẩu." });
    await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
    var identifier = request.Identifier.Trim();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == identifier.ToLower(), http.RequestAborted);
    if (user is null && !identifier.Contains('@'))
    {
        var normalizedPhone = new string(identifier.Where(char.IsDigit).ToArray());
        user = await db.Users.FirstOrDefaultAsync(u => u.Phone == normalizedPhone, http.RequestAborted);
    }
    if (user?.Email.EndsWith("@example.com", StringComparison.OrdinalIgnoreCase) == true && !app.Environment.IsDevelopment()) return Results.Unauthorized();
    if (user is null || !user.IsActive || user.PasswordHash is null || new PasswordHasher<LibraryUser>().VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        return Results.Unauthorized();
    var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Name), new Claim(ClaimTypes.Email, user.Email), new Claim("phone", user.Phone), new Claim(ClaimTypes.Role, user.Role) };
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
        new AuthenticationProperties { IsPersistent = request.RememberMe, AllowRefresh = true });
    return Results.Ok(new { user = new UserDto(user.Id, user.Name, user.Email, user.Phone), role = user.Role });
}).RequireRateLimiting("auth");
app.MapGet("/auth/me", (ClaimsPrincipal principal) =>
{
    var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    return id is null ? Results.Unauthorized() : Results.Ok(new { id, name = principal.Identity?.Name, email = principal.FindFirstValue(ClaimTypes.Email), phone = principal.FindFirstValue("phone"), role = principal.FindFirstValue(ClaimTypes.Role) });
});
app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
});
app.MapPost("/auth/forgot-password", async (ForgotPasswordRequest request, HttpContext http, IDbContextFactory<LibraryDbContext> factory, IConfiguration config, ILogger<Program> logger) =>
{
    const string generic = "Nếu email đã đăng ký, hướng dẫn đặt lại mật khẩu sẽ được gửi đến hộp thư của bạn.";
    if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@')) return Results.BadRequest(new { error = "Vui lòng nhập địa chỉ email hợp lệ." });
    await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
    var email = request.Email.Trim().ToLowerInvariant();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email, http.RequestAborted);
    if (user is null) return Results.Ok(new { message = generic });

    var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    user.PasswordResetTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    user.PasswordResetExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
    await db.SaveChangesAsync(http.RequestAborted);
    var configuredBaseUrl = config["App:PublicBaseUrl"];
    var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl) ? $"{http.Request.Scheme}://{http.Request.Host}" : configuredBaseUrl.TrimEnd('/');
    var resetUrl = $"{baseUrl}/login.html?resetToken={Uri.EscapeDataString(token)}";
    var smtpHost = config["Smtp:Host"];
    if (!string.IsNullOrWhiteSpace(smtpHost))
    {
        try
        {
            using var mail = new MailMessage(config["Smtp:From"] ?? config["Smtp:UserName"] ?? "noreply@localhost", user.Email)
            {
                Subject = "Đặt lại mật khẩu Thư viện Mộc",
                Body = $"Bạn vừa yêu cầu đặt lại mật khẩu. Liên kết có hiệu lực trong 30 phút: {resetUrl}",
                IsBodyHtml = false
            };
            using var smtp = new SmtpClient(smtpHost, int.TryParse(config["Smtp:Port"], out var port) ? port : 587)
            {
                EnableSsl = bool.TryParse(config["Smtp:EnableSsl"], out var ssl) ? ssl : true,
                Credentials = string.IsNullOrWhiteSpace(config["Smtp:UserName"]) ? CredentialCache.DefaultNetworkCredentials : new NetworkCredential(config["Smtp:UserName"], config["Smtp:Password"])
            };
            await smtp.SendMailAsync(mail, http.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Failed sending password reset email"); }
    }
    else if (app.Environment.IsDevelopment()) logger.LogWarning("SMTP is not configured. Development password reset link: {ResetUrl}", resetUrl);

    return app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(smtpHost)
        ? Results.Ok(new { message = generic, developmentResetUrl = resetUrl })
        : Results.Ok(new { message = generic });
}).RequireRateLimiting("auth");
app.MapPost("/auth/reset-password", async (ResetPasswordRequest request, IDbContextFactory<LibraryDbContext> factory, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        return Results.BadRequest(new { error = "Mật khẩu mới cần ít nhất 8 ký tự và liên kết phải còn đầy đủ." });
    await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, http.RequestAborted);
    var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));
    if (user?.PasswordResetTokenHash is null || user.PasswordResetExpiresAt <= DateTimeOffset.UtcNow || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(user.PasswordResetTokenHash), Encoding.UTF8.GetBytes(tokenHash)))
        return Results.BadRequest(new { error = "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã hết hạn." });
    user.PasswordHash = new PasswordHasher<LibraryUser>().HashPassword(user, request.NewPassword);
    user.PasswordResetTokenHash = null; user.PasswordResetExpiresAt = null;
    await db.SaveChangesAsync(http.RequestAborted);
    return Results.Ok(new { message = "Mật khẩu đã được cập nhật. Bạn có thể đăng nhập." });
}).RequireRateLimiting("auth");
app.MapGet("/health/ready", async (IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    await using var db = await factory.CreateDbContextAsync(ct);
    return await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503);
});

app.MapGet("/books", async (string? q, string? category, IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    await using var db = await factory.CreateDbContextAsync(ct);
    var query = db.Books.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(q)) query = query.Where(b => EF.Functions.ILike(b.Title, $"%{q}%") || EF.Functions.ILike(b.Author, $"%{q}%"));
    if (!string.IsNullOrWhiteSpace(category) && category != "Tất cả") query = query.Where(b => b.Category == category);
    var books = await query.OrderBy(b => b.Id).ToListAsync(ct);
    return Results.Ok(books.Select(ToBookDto));
});
app.MapGet("/books/{id:guid}", async (Guid id, IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    await using var db = await factory.CreateDbContextAsync(ct);
    var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
    return book is null ? Results.NotFound() : Results.Ok(ToBookDto(book));
});
app.MapPost("/books", async (BookWriteRequest request, IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    var error = ValidateBook(request); if (error is not null) return Results.BadRequest(new { error });
    await using var db = await factory.CreateDbContextAsync(ct);
    var book = new Book { Id = Guid.NewGuid(), Title = request.Title.Trim(), Author = request.Author.Trim(), Category = request.Category.Trim(), PublishedYear = request.PublishedYear,
        TotalCopies = request.TotalCopies, AvailableCopies = request.TotalCopies, CoverKey = request.CoverKey.Trim(), Tag = request.Tag.Trim(), Description = request.Description.Trim() };
    db.Books.Add(book); await db.SaveChangesAsync(ct);
    return Results.Created($"/books/{book.Id}", ToBookDto(book));
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Librarian, RoleNames.Admin));
app.MapPut("/books/{id:guid}", async (Guid id, BookWriteRequest request, IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    var error = ValidateBook(request); if (error is not null) return Results.BadRequest(new { error });
    await using var db = await factory.CreateDbContextAsync(ct);
    var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id, ct);
    if (book is null) return Results.NotFound();
    var copiesInUse = book.TotalCopies - book.AvailableCopies;
    if (request.TotalCopies < copiesInUse) return Results.Conflict(new { error = $"Có {copiesInUse} bản đang được giữ hoặc cho mượn." });
    book.Title = request.Title.Trim(); book.Author = request.Author.Trim(); book.Category = request.Category.Trim(); book.PublishedYear = request.PublishedYear;
    book.TotalCopies = request.TotalCopies; book.AvailableCopies = request.TotalCopies - copiesInUse;
    book.CoverKey = request.CoverKey.Trim(); book.Tag = request.Tag.Trim(); book.Description = request.Description.Trim();
    await db.SaveChangesAsync(ct); return Results.Ok(ToBookDto(book));
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Librarian, RoleNames.Admin));
app.MapDelete("/books/{id:guid}", async (Guid id, IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    await using var db = await factory.CreateDbContextAsync(ct);
    var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id, ct);
    if (book is null) return Results.NotFound();
    if (await db.Reservations.AnyAsync(r => r.BookId == id, ct)) return Results.Conflict(new { error = "Sách đã có lịch sử mượn nên không thể xóa. Hãy chỉnh tổng số bản về 0 để ngừng lưu hành." });
    db.Books.Remove(book); await db.SaveChangesAsync(ct); return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Librarian, RoleNames.Admin));
app.MapGet("/admin/readers", async (IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    await using var db = await factory.CreateDbContextAsync(ct);
    return Results.Ok(await db.Users.AsNoTracking().Where(u => u.Role == RoleNames.Reader).OrderBy(u => u.Name)
        .Select(u => new ReaderAdminDto(u.Id, u.Name, u.Email, u.Phone, u.Role, u.IsActive)).ToListAsync(ct));
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));
app.MapPatch("/admin/readers/{id:guid}/status", async (Guid id, ReaderStatusRequest request, IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    await using var db = await factory.CreateDbContextAsync(ct);
    var reader = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == RoleNames.Reader, ct);
    if (reader is null) return Results.NotFound();
    reader.IsActive = request.IsActive; await db.SaveChangesAsync(ct);
    return Results.Ok(new ReaderAdminDto(reader.Id, reader.Name, reader.Email, reader.Phone, reader.Role, reader.IsActive));
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));
app.MapGet("/admin/statistics", async (IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    await using var db = await factory.CreateDbContextAsync(ct);
    var now = DateTimeOffset.UtcNow;
    var popular = await db.Reservations.AsNoTracking().Where(r => r.Status == ReservationStatus.Borrowed || r.Status == ReservationStatus.Returned)
        .GroupBy(r => new { r.BookId, r.Book.Title }).Select(g => new PopularBookDto(g.Key.BookId, g.Key.Title, g.Count()))
        .OrderByDescending(x => x.LoanCount).Take(5).ToListAsync(ct);
    var result = new LibraryStatisticsDto(
        await db.Books.CountAsync(ct), await db.Books.SumAsync(b => (int?)b.TotalCopies, ct) ?? 0,
        await db.Books.SumAsync(b => (int?)b.AvailableCopies, ct) ?? 0,
        await db.Users.CountAsync(u => u.Role == RoleNames.Reader, ct),
        await db.Reservations.CountAsync(r => r.Status == ReservationStatus.Pending, ct),
        await db.Reservations.CountAsync(r => r.Status == ReservationStatus.Reserved || r.Status == ReservationStatus.Borrowed, ct),
        await db.Reservations.CountAsync(r => r.Status == ReservationStatus.Borrowed && r.DueAt < now, ct), popular);
    return Results.Ok(result);
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));
app.MapGet("/staff/reservations", async (string? status, IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    await using var db = await factory.CreateDbContextAsync(ct);
    var query = db.Reservations.AsNoTracking().Include(r => r.Book).Include(r => r.User).AsQueryable();
    if (!string.IsNullOrWhiteSpace(status))
    {
        if (!Enum.TryParse<ReservationStatus>(status, true, out var parsed)) return Results.BadRequest(new { error = "Trạng thái phiếu không hợp lệ." });
        query = query.Where(r => r.Status == parsed);
    }
    var items = await query.OrderByDescending(r => r.CreatedAt).Take(250).ToListAsync(ct);
    return Results.Ok(items.Select(ToReservationDto));
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Librarian, RoleNames.Admin));
app.MapGet("/users", async (IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    await using var db = await factory.CreateDbContextAsync(ct);
    return Results.Ok(await db.Users.AsNoTracking().OrderBy(u => u.Name).Select(u => new UserDto(u.Id, u.Name, u.Email, u.Phone)).ToListAsync(ct));
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));
app.MapPost("/reservations", async (CreateReservationRequest request, ClaimsPrincipal principal, IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    if (request.UserId == Guid.Empty || request.BookId == Guid.Empty) return Results.BadRequest(new { error = "UserId và BookId là bắt buộc." });
    if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var signedInUserId) || signedInUserId != request.UserId) return Results.Forbid();
    await using var db = await factory.CreateDbContextAsync(ct);
    if (!await db.Users.AnyAsync(u => u.Id == request.UserId, ct)) return Results.BadRequest(new { error = "Không tìm thấy người dùng." });
    var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BookId, ct);
    if (book is null) return Results.NotFound(new { error = "Không tìm thấy sách." });
    var active = await db.Reservations.AnyAsync(r => r.UserId == request.UserId && r.BookId == request.BookId &&
        (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Reserved || r.Status == ReservationStatus.Borrowed), ct);
    if (active) return Results.Conflict(new { error = "Bạn đã có yêu cầu đang xử lý cho cuốn sách này." });
    var now = DateTimeOffset.UtcNow;
    var reservation = new Reservation { Id = Guid.NewGuid(), BookId = request.BookId, UserId = request.UserId, Status = ReservationStatus.Pending, CreatedAt = now, UpdatedAt = now };
    var message = new ReservationRequestedMessage(reservation.Id, reservation.BookId);
    db.Reservations.Add(reservation);
    db.OutboxMessages.Add(new OutboxMessage { Id = Guid.NewGuid(), Type = nameof(ReservationRequestedMessage), Payload = System.Text.Json.JsonSerializer.Serialize(message), CreatedAt = now });
    await db.SaveChangesAsync(ct);
    return Results.Accepted($"/reservations/{reservation.Id}", new { reservation.Id, status = reservation.Status.ToString(), reservation.CreatedAt });
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Reader));
app.MapPost("/reservations/{id:guid}/cancel", async (Guid id, ClaimsPrincipal principal, IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    await using var db = await factory.CreateDbContextAsync(ct);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var item = await db.Reservations.FromSqlInterpolated($"SELECT * FROM \"Reservations\" WHERE \"Id\" = {id} FOR UPDATE").Include(r => r.Book).FirstOrDefaultAsync(ct);
    if (item is null) return Results.NotFound();
    if (principal.FindFirstValue(ClaimTypes.NameIdentifier) != item.UserId.ToString()) return Results.Forbid();
    if (item.Status is not (ReservationStatus.Pending or ReservationStatus.Reserved)) return Results.Conflict(new { error = "Chỉ có thể hủy yêu cầu đang chờ hoặc đã giữ sách." });
    if (item.Status == ReservationStatus.Reserved) item.Book.AvailableCopies++;
    item.Status = ReservationStatus.Cancelled; item.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    return Results.Ok(new { item.Id, status = item.Status.ToString() });
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Reader));
app.MapGet("/reservations/{id:guid}", async (Guid id, ClaimsPrincipal principal, IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    await using var db = await factory.CreateDbContextAsync(ct);
    var item = await db.Reservations.AsNoTracking().Include(r => r.Book).Include(r => r.User).FirstOrDefaultAsync(r => r.Id == id, ct);
    if (item is null) return Results.NotFound();
    if (principal.FindFirstValue(ClaimTypes.NameIdentifier) != item.UserId.ToString()) return Results.Forbid();
    return Results.Ok(ToReservationDto(item));
}).RequireAuthorization();
app.MapGet("/users/{userId:guid}/reservations", async (Guid userId, ClaimsPrincipal principal, IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    if (principal.FindFirstValue(ClaimTypes.NameIdentifier) != userId.ToString()) return Results.Forbid();
    await using var db = await factory.CreateDbContextAsync(ct);
    if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) return Results.NotFound();
    var items = await db.Reservations.AsNoTracking().Where(r => r.UserId == userId).Include(r => r.Book).Include(r => r.User).OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
    return Results.Ok(items.Select(ToReservationDto));
}).RequireAuthorization();
app.MapPost("/reservations/{id:guid}/borrow", async (Guid id, IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    await using var db = await factory.CreateDbContextAsync(ct);
    var item = await db.Reservations.FirstOrDefaultAsync(r => r.Id == id, ct);
    if (item is null) return Results.NotFound();
    if (item.Status != ReservationStatus.Reserved) return Results.Conflict(new { error = "Chỉ yêu cầu đã giữ sách mới được chuyển sang đang mượn." });
    item.Status = ReservationStatus.Borrowed; item.DueAt = DateTimeOffset.UtcNow.AddDays(14); item.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
    return Results.Ok(new { item.Id, status = item.Status.ToString() });
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Librarian, RoleNames.Admin));
app.MapPost("/reservations/{id:guid}/return", async (Guid id, IDbContextFactory<LibraryDbContext> factory, CancellationToken ct) =>
{
    await using var db = await factory.CreateDbContextAsync(ct);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var item = await db.Reservations.FromSqlInterpolated($"SELECT * FROM \"Reservations\" WHERE \"Id\" = {id} FOR UPDATE").Include(r => r.Book).FirstOrDefaultAsync(ct);
    if (item is null) return Results.NotFound();
    if (item.Status != ReservationStatus.Borrowed) return Results.Conflict(new { error = "Chỉ sách đang mượn mới được trả." });
    item.Status = ReservationStatus.Returned; item.UpdatedAt = DateTimeOffset.UtcNow; item.Book.AvailableCopies++;
    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    return Results.Ok(new { item.Id, status = item.Status.ToString() });
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Librarian, RoleNames.Admin));

app.Run();

static BookDto ToBookDto(Book b) => new(b.Id, b.Title, b.Author, b.Category, b.PublishedYear, b.AvailableCopies, b.TotalCopies, b.CoverKey, b.Tag, b.Description);
static ReservationDto ToReservationDto(Reservation r) => new(r.Id, r.BookId, r.Book.Title, r.UserId, r.User.Name, r.Status, r.CreatedAt, r.UpdatedAt, r.DueAt);
static string? ValidateBook(BookWriteRequest r) => string.IsNullOrWhiteSpace(r.Title) || r.Title.Length > 240 ? "Tên sách là bắt buộc (tối đa 240 ký tự)."
    : string.IsNullOrWhiteSpace(r.Author) || r.Author.Length > 160 ? "Tác giả là bắt buộc (tối đa 160 ký tự)."
    : string.IsNullOrWhiteSpace(r.Category) || r.Category.Length > 100 ? "Thể loại là bắt buộc (tối đa 100 ký tự)."
    : r.PublishedYear is < 0 or > 2100 ? "Năm xuất bản không hợp lệ."
    : r.TotalCopies is < 0 or > 100000 ? "Số bản sách không hợp lệ."
    : (r.CoverKey?.Length ?? 0) > 100 || (r.Tag?.Length ?? 0) > 80 || (r.Description?.Length ?? 0) > 2000 ? "Thông tin bìa, nhãn hoặc mô tả quá dài."
    : null;

record LoginRequest(string Identifier, string Password, bool RememberMe = false);
record ForgotPasswordRequest(string Email);
record ResetPasswordRequest(string Email, string Token, string NewPassword);
