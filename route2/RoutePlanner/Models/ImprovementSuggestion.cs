namespace RoutePlanner.Models;

using System.Text.Json.Serialization;

// AI による足順の改善提案（並べ替え・前倒し・事前連絡・分割など）。
public sealed class ImprovementSuggestion
{
    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    [JsonPropertyName("target_stop_ids")]
    public IReadOnlyList<string> TargetStopIds { get; init; } = [];

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("expected_effect")]
    public string ExpectedEffect { get; init; } = string.Empty;
}
