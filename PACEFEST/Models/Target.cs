using System.Text.Json;

namespace PACEFEST.Models;

public class Target
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

public class Season
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public static class Parser
{
    public static T[] ParseJsonArray<T>(string json)
    {
        // If you want case-insensitive property matching:
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var response =  JsonSerializer.Deserialize<ApiResponse<T>>(json, options);
        return response?.Data?.ToArray() ?? [];
    }
    public static Target? ParseJson(string json)
    {
        // If you want case-insensitive property matching:
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return JsonSerializer.Deserialize<Target>(json, options);
    }
}