using Library.Contracts;

namespace Library.Data;

public sealed class Book
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Author { get; set; }
    public required string Category { get; set; }
    public int PublishedYear { get; set; }
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }
    public required string CoverKey { get; set; }
    public required string Tag { get; set; }
    public required string Description { get; set; }
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}

public sealed class LibraryUser
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string Phone { get; set; }
    public string Role { get; set; } = RoleNames.Reader;
    public bool IsActive { get; set; } = true;
    public string? PasswordHash { get; set; }
    public string? PasswordResetTokenHash { get; set; }
    public DateTimeOffset? PasswordResetExpiresAt { get; set; }
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}

public sealed class Reservation
{
    public Guid Id { get; set; }
    public Guid BookId { get; set; }
    public Book Book { get; set; } = null!;
    public Guid UserId { get; set; }
    public LibraryUser User { get; set; } = null!;
    public ReservationStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
}

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public required string Type { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }
}
