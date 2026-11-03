namespace MyBlazorProgressApp.Models;

public class ScoreResponse
{
    public int? PersonID { get; set; }
    public decimal? Score { get; set; }
    public decimal? TotalAksjonTarget { get; set; }
    public decimal? TotalContribution { get; set; }

    // "ajourUntil": "2025-01-30"
    // It's an ISO date with no time; store as DateTime?
    public DateTime? AjourUntil { get; set; }

    public decimal? AjourTarget { get; set; }

    // An array of milestone objects
    public List<Milestone>? Milestones { get; set; }

    // An array of target objects
    public List<TargetItem>? Targets { get; set; }
}

public class Milestone
{
    // The JSON sample shows "date" as a full date/time string
    public DateTime? Date { get; set; }

    // "value": 0, could be int or decimal. 
    // Use decimal? if you expect currency or partial amounts
    public decimal? Value { get; set; }
}

public class TargetItem
{
    public Guid? Uid { get; set; }
    public int? PersonID { get; set; }
    public int? OrgID { get; set; }
    public string? Description { get; set; }

    public decimal? Amount { get; set; }
    public int? ClubSeasonId { get; set; }
    public int? Type { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public int? ArchivedById { get; set; }
    public Season? Season { get; set; }
    public string? Currency { get; set; }
    public DateTime? ArchivedDate { get; set; }
    public int? TargetPriority { get; set; }
    public string? TargetName { get; set; }
}