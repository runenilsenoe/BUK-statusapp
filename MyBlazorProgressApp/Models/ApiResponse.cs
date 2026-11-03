using System.Text.Json.Serialization;

namespace MyBlazorProgressApp.Models;

public class ApiResponse<T>
{
    [JsonPropertyName("data")]
    public List<T>? Data { get; set; }
    
    [JsonPropertyName("meta")]
    public Meta? Meta { get; set; }
}

public class Meta
{
    public int? Limit { get; set; }
    public int? Skipped { get; set; }

    [JsonPropertyName("filter_count")]
    public int? FilterCount { get; set; }
}