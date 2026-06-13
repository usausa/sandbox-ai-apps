namespace PosChecker.Models;

using System.Text.Json.Serialization;

public sealed record DailyRiskResult
{
    [JsonPropertyName("date")]
    public DateOnly BusinessDate { get; init; }

    [JsonPropertyName("score")]
    public int Score { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("suspicious_transaction_ids")]
    public IReadOnlyList<string> SuspiciousTransactionIds { get; init; } = [];
}
