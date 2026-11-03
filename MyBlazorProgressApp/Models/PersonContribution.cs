namespace MyBlazorProgressApp.Models;

public class PersonContribution
{
    public int? Id { get; set; }
    public int? PersonId { get; set; }
    public string? Name { get; set; }
    public decimal? TotalProposedAmount { get; set; }
    public decimal? TotalAcceptedAmount { get; set; }
    public string? Description { get; set; }
    public DateTime? LastChanged { get; set; }
    public int? ClubTargetPriority { get; set; }
    public int? ClubSeasonId { get; set; }
    public int? Status { get; set; }
    public string? AcceptedBy { get; set; }
    public DateTime? AcceptedDate { get; set; }
    public int? ClubId { get; set; }
    public string? LastName { get; set; }
    public string? FirstName { get; set; }
    public DateTime? BirthDate { get; set; }
    public int? Age { get; set; }
}
