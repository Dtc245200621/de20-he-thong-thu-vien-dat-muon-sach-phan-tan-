using Library.Data;
using Library.Messaging;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("LibraryDb")
    ?? "Host=localhost;Port=5432;Database=library;Username=library;Password=library_dev";
builder.Services.AddDbContextFactory<LibraryDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqOptions>>().Value);
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddHostedService<ReservationConsumer>();
await builder.Build().RunAsync();
