namespace Library.Contracts;

public enum ReservationStatus
{
    Pending,
    Reserved,
    Rejected,
    Borrowed,
    Returned,
    Cancelled
}

public static class RoleNames
{
    public const string Reader = "Reader";
    public const string Librarian = "Librarian";
    public const string Admin = "Admin";
}

public sealed record BookDto(
    Guid Id,
    string Title,
    string Author,
    string Category,
    int PublishedYear,
    int AvailableCopies,
    int TotalCopies,
    string CoverKey,
    string Tag,
    string Description);

public sealed record UserDto(Guid Id, string Name, string Email, string Phone);

public sealed record CreateReservationRequest(Guid UserId, Guid BookId);
public sealed record RegisterUserRequest(string Name, string Email, string Phone, string Password);
public sealed record BookWriteRequest(string Title, string Author, string Category, int PublishedYear, int TotalCopies, string CoverKey, string Tag, string Description);
public sealed record ReaderAdminDto(Guid Id, string Name, string Email, string Phone, string Role, bool IsActive);
public sealed record LibraryStatisticsDto(int BookTitles, int TotalCopies, int AvailableCopies, int Readers, int PendingRequests, int ActiveLoans, int OverdueLoans, IReadOnlyList<PopularBookDto> PopularBooks);
public sealed record PopularBookDto(Guid BookId, string Title, int LoanCount);

public sealed record ReservationDto(
    Guid Id,
    Guid BookId,
    string BookTitle,
    Guid UserId,
    string UserName,
    ReservationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DueAt);

public sealed record ReaderStatusRequest(bool IsActive);

public sealed record ReservationRequestedMessage(Guid ReservationId, Guid BookId);

public static class QueueNames
{
    public const string Reservations = "library.reservations";
    public const string DeadLetters = "library.reservations.dead";
}
