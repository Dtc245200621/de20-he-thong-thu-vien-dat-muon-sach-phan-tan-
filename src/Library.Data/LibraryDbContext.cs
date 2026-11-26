using Library.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Library.Data;

public sealed class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<LibraryUser> Users => Set<LibraryUser>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Book>(entity =>
        {
            entity.ToTable("Books");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(240).IsRequired();
            entity.Property(x => x.Author).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(80).IsRequired();
            entity.Property(x => x.CoverKey).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Tag).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1200).IsRequired();
            entity.HasIndex(x => x.Title);
        });

        modelBuilder.Entity<LibraryUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(32).IsRequired();
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.PasswordHash);
            entity.Property(x => x.PasswordResetTokenHash).HasMaxLength(64);
            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasIndex(x => x.Phone).IsUnique();
        });

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.ToTable("Reservations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasIndex(x => new { x.Status, x.DueAt });
            entity.HasOne(x => x.Book).WithMany(x => x.Reservations).HasForeignKey(x => x.BookId);
            entity.HasOne(x => x.User).WithMany(x => x.Reservations).HasForeignKey(x => x.UserId);
            entity.HasIndex(x => new { x.UserId, x.CreatedAt });
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(x => new { x.PublishedAt, x.CreatedAt });
        });
    }
}
