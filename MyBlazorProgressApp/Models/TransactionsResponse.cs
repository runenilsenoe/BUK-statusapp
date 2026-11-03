namespace MyBlazorProgressApp.Models;

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
    public double? Hours { get; set; }
    public decimal? Amount { get; set; }
    public int? ActivityId { get; set; }
    public int? ClubId { get; set; }
    public int? TeamId { get; set; }
}
