namespace PosChecker.Models;

using System.Text.Json.Serialization;

// LLM が返す店舗×担当者ごとの不正リスク (= 判定単位)。
public sealed record CashierRiskResult
{
    [JsonPropertyName("store_code")]
    public string StoreCode { get; init; } = string.Empty;

    [JsonPropertyName("cashier_code")]
    public string CashierCode { get; init; } = string.Empty;

    [JsonPropertyName("cashier_name")]
    public string CashierName { get; init; } = string.Empty;

    [JsonPropertyName("score")]
    public int Score { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    // 該当する不正一覧の事象 (PointAbuse / CartBypass / CouponAbuse / ReturnFraud / RekeyFraud)。
    [JsonPropertyName("scenarios")]
    public IReadOnlyList<string> Scenarios { get; init; } = [];

    // 根拠となる会員コード・取引キーなど。
    [JsonPropertyName("suspicious_keys")]
    public IReadOnlyList<string> SuspiciousKeys { get; init; } = [];
}
