namespace InspectorChecker.Models;

using System.Text.Json.Serialization;

public sealed class DailyRiskResult
{
    [JsonPropertyName("date")]
    public DateOnly InvestigationDate { get; init; }

    [JsonPropertyName("score")]
    public int Score { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("suspicious_customer_ids")]
    public IReadOnlyList<string> SuspiciousCustomerIds { get; init; } = [];
}
