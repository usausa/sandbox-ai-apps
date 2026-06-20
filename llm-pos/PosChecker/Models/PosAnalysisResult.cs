namespace PosChecker.Models;

using System.Text.Json.Serialization;

public sealed record PosAnalysisResult
{
    [JsonPropertyName("overall_score")]
    public int OverallScore { get; init; }

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("recommended_action")]
    public string RecommendedAction { get; init; } = string.Empty;

    [JsonPropertyName("suspicious_patterns")]
    public IReadOnlyList<string> SuspiciousPatterns { get; init; } = [];

    [JsonPropertyName("cashier_results")]
    public IReadOnlyList<CashierRiskResult> CashierResults { get; init; } = [];

    [JsonPropertyName("member_results")]
    public IReadOnlyList<MemberRiskResult> MemberResults { get; init; } = [];
}
