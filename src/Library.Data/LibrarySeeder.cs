using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace Library.Data;

public static class LibrarySeeder
{
    public static readonly Guid MinhAnhId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    public static async Task InitializeAsync(LibraryDbContext db, bool enableDemoAccounts = false, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"PasswordHash\" text NULL", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"PasswordResetTokenHash\" character varying(64) NULL", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"PasswordResetExpiresAt\" timestamp with time zone NULL", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"Role\" character varying(32) NOT NULL DEFAULT 'Reader'", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"IsActive\" boolean NOT NULL DEFAULT true", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Reservations\" ADD COLUMN IF NOT EXISTS \"DueAt\" timestamp with time zone NULL", cancellationToken);
        if (await db.Books.AnyAsync(cancellationToken))
        {
            if (enableDemoAccounts)
            {
                var existingUsers = await db.Users.Where(u => u.PasswordHash == null).ToListAsync(cancellationToken);
                var passwordHasher = new PasswordHasher<LibraryUser>();
                foreach (var user in existingUsers) user.PasswordHash = passwordHasher.HashPassword(user, "MocLibrary@2026");
                await EnsureStaffAsync(db, passwordHasher, cancellationToken);
                if (existingUsers.Count > 0) await db.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        db.Users.AddRange(
            new LibraryUser { Id = MinhAnhId, Name = "Minh Anh", Email = "minhanh@example.com", Phone = "0901234567" },
            new LibraryUser { Id = Guid.Parse("10000000-0000-0000-0000-000000000002"), Name = "Bảo Ngọc", Email = "baongoc@example.com", Phone = "0912345678" },
            new LibraryUser { Id = Guid.Parse("10000000-0000-0000-0000-000000000003"), Name = "Quốc Huy", Email = "quochuy@example.com", Phone = "0987654321" });
        if (enableDemoAccounts)
        {
            var hasher = new PasswordHasher<LibraryUser>();
            foreach (var user in db.Users.Local) user.PasswordHash = hasher.HashPassword(user, "MocLibrary@2026");
            var librarian = new LibraryUser { Id = Guid.Parse("10000000-0000-0000-0000-000000000004"), Name = "Thủ thư Mộc", Email = "thuthu@library.local", Phone = "0900000001", Role = Library.Contracts.RoleNames.Librarian };
            var admin = new LibraryUser { Id = Guid.Parse("10000000-0000-0000-0000-000000000005"), Name = "Quản trị viên", Email = "admin@library.local", Phone = "0900000002", Role = Library.Contracts.RoleNames.Admin };
            librarian.PasswordHash = hasher.HashPassword(librarian, "MocLibrary@2026");
            admin.PasswordHash = hasher.HashPassword(admin, "MocLibrary@2026");
            db.Users.AddRange(librarian, admin);
        }

        db.Books.AddRange(
            Book(1, "Nhà giả kim", "Paulo Coelho", "Văn học", 2022, 6, 4, "Được yêu thích", "Một câu chuyện giàu chất thơ về hành trình theo đuổi ước mơ và lắng nghe tiếng gọi từ trái tim."),
            Book(2, "Cây cam ngọt của tôi", "José Mauro de Vasconcelos", "Văn học", 2021, 5, 2, "Bạn đọc chọn", "Qua đôi mắt trong veo của cậu bé Zezé, những vui buồn tuổi thơ hiện lên hồn nhiên và lay động."),
            Book(3, "Đi tìm lẽ sống", "Viktor E. Frankl", "Kỹ năng", 2023, 4, 0, "Đang được mượn", "Từ trải nghiệm sinh tồn, Viktor Frankl suy ngẫm về khả năng tìm thấy ý nghĩa trong hoàn cảnh khắc nghiệt."),
            Book(4, "Tuổi trẻ đáng giá bao nhiêu", "Rosie Nguyễn", "Kỹ năng", 2020, 5, 3, "Gợi ý tuần này", "Một lời nhắn chân thành: hãy học, trải nghiệm và dấn thân để sống có chủ đích."),
            Book(5, "Bước chậm lại giữa thế gian vội vã", "Hae Min", "Văn học", 2022, 7, 5, "Nhẹ nhàng", "Những trang viết ngắn giúp ta nhìn lại nhịp sống, các mối quan hệ và cách đối xử tử tế với chính mình."),
            Book(6, "Không gia đình", "Hector Malot", "Văn học", 2019, 3, 1, "Kinh điển", "Cuộc phiêu lưu của cậu bé Rémi là câu chuyện cảm động về tình người, nghị lực và mái ấm."),
            Book(7, "Đắc nhân tâm", "Dale Carnegie", "Kỹ năng", 2023, 8, 6, "Kinh điển", "Những nguyên tắc thực tế về giao tiếp và xây dựng mối quan hệ, qua các câu chuyện đời thường."),
            Book(8, "Cho tôi xin một vé đi tuổi thơ", "Nguyễn Nhật Ánh", "Văn học", 2021, 4, 2, "Bạn đọc chọn", "Một chuyến tàu trở về tuổi thơ với những trò nghịch ngợm và cách nghĩ ngộ nghĩnh."),
            Book(9, "Dế Mèn phiêu lưu ký", "Tô Hoài", "Thiếu nhi", 2020, 4, 3, "Thiếu nhi", "Hành trình trưởng thành của chú Dế Mèn qua những miền đất mới và những bài học về trách nhiệm."),
            Book(10, "Tuần làm việc 4 giờ", "Timothy Ferriss", "Kinh doanh", 2022, 3, 1, "Kinh doanh", "Một góc nhìn về thiết kế công việc và cuộc sống linh hoạt, sử dụng thời gian có chủ đích."),
            Book(11, "Mắt biếc", "Nguyễn Nhật Ánh", "Văn học", 2018, 2, 0, "Đang được mượn", "Câu chuyện tình đơn phương nhiều day dứt ở làng Đo Đo, với giọng văn trong trẻo và hoài niệm."),
            Book(12, "Totto-chan bên cửa sổ", "Tetsuko Kuroyanagi", "Thiếu nhi", 2021, 3, 2, "Thiếu nhi", "Những kỷ niệm của Totto-chan tại ngôi trường Tomoe, nơi mỗi đứa trẻ được tôn trọng."));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureStaffAsync(LibraryDbContext db, PasswordHasher<LibraryUser> hasher, CancellationToken cancellationToken)
    {
        var staff = new[]
        {
            new LibraryUser { Id = Guid.Parse("10000000-0000-0000-0000-000000000004"), Name = "Thủ thư Mộc", Email = "thuthu@library.local", Phone = "0900000001", Role = Library.Contracts.RoleNames.Librarian },
            new LibraryUser { Id = Guid.Parse("10000000-0000-0000-0000-000000000005"), Name = "Quản trị viên", Email = "admin@library.local", Phone = "0900000002", Role = Library.Contracts.RoleNames.Admin }
        };
        foreach (var item in staff)
        {
            var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == item.Email, cancellationToken);
            if (existing is null)
            {
                item.PasswordHash = hasher.HashPassword(item, "MocLibrary@2026");
                db.Users.Add(item);
            }
            else
            {
                existing.Role = item.Role;
                if (existing.PasswordHash is null) existing.PasswordHash = hasher.HashPassword(existing, "MocLibrary@2026");
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Book Book(int number, string title, string author, string category, int year, int total, int available, string tag, string description) => new()
    {
        Id = Guid.Parse($"20000000-0000-0000-0000-{number:000000000000}"),
        Title = title, Author = author, Category = category, PublishedYear = year,
        TotalCopies = total, AvailableCopies = available, CoverKey = number.ToString(), Tag = tag, Description = description
    };
}
