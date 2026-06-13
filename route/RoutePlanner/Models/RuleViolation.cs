namespace RoutePlanner.Models;

using System.Text.Json.Serialization;

// AI が検出した、足順上のルール違反・リスク。
public sealed class RuleViolation
{
    [JsonPropertyName("stop_id")]
    public string StopId { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = string.Empty;
}
