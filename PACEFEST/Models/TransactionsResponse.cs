namespace PACEFEST.Models;

public class NewApiResponse<T>
{
    public string? Status { get; set; }
    public int? StatusCode { get; set; }
    public List<T>? Data { get; set; }
    public object? Messages { get; set; }
}

public class Transaction
{
    public int? Id { get; set; }
    public string? Description { get; set; }
    public DateTime? TransactionDate { get; set; }
    public int? UserId { get; set; }
    public string? UserDisplayName { get; set; }
    public decimal? Hours { get; set; }
    public decimal? Amount { get; set; }
    public int? ActivityId { get; set; }
    public int? ClubId { get; set; }
    public int? TeamId { get; set; }
}

public class PersonStatus
{
    public int PersonId { get; set; }
    public int? SpouseId { get; set; }
    public string Name { get; set; }
    public DateTime? DateRange { get; set; }
    public decimal Status { get; set; }
    public decimal TransactionAmount { get; set; }
    public decimal TargetAmount { get; set; }
    public bool IsCountedInAksjon { get; set; }
    public bool IsOnTrack { get; set; }
    public int Age { get; set; }
    public bool PreviousMonthOnTrack { get; set; }
}
