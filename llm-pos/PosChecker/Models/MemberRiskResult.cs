namespace PosChecker.Models;

using System.Text.Json.Serialization;

// LLM が返す会員ごとの不正リスク (ポイント不正付与・クーポン反復利用)。
public sealed record MemberRiskResult
{
    [JsonPropertyName("member_code")]
    public string MemberCode { get; init; } = string.Empty;

    [JsonPropertyName("score")]
    public int Score { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("scenarios")]
    public IReadOnlyList<string> Scenarios { get; init; } = [];
}
