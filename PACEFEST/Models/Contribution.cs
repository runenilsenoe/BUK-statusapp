namespace PACEFEST.Models;

public class Contribution
{
    public string? Uid { get; set; }
    public int? OrgID { get; set; }
    public string? Description { get; set; }
    public DateTime? ContributionDate { get; set; }
    public int? PersonID { get; set; }
    public decimal? Amount { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public int? Type { get; set; }
    public string? Currency { get; set; }
}
