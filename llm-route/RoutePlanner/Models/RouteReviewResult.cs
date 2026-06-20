namespace RoutePlanner.Models;

using System.Text.Json.Serialization;

// Foundry(LLM) による足順レビュー結果。実現可能性スコアと違反・改善提案を持つ。
public sealed class RouteReviewResult
{
    [JsonPropertyName("feasibility_score")]
    public int FeasibilityScore { get; init; }

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("recommended_action")]
    public string RecommendedAction { get; init; } = string.Empty;

    [JsonPropertyName("rule_violations")]
    public IReadOnlyList<RuleViolation> RuleViolations { get; init; } = [];

    [JsonPropertyName("improvement_suggestions")]
    public IReadOnlyList<ImprovementSuggestion> ImprovementSuggestions { get; init; } = [];

    [JsonPropertyName("risk_notes")]
    public IReadOnlyList<string> RiskNotes { get; init; } = [];
}
