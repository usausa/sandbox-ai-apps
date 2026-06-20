namespace RoutePlanner.Models;

// Foundry(LLM) 呼び出しのトークン使用量と、USD単価×ドル円レートで算出した概算費用（円）。
// Foundry はトークン数のみ返し費用は返さないため、EstimatedCostJpy はアプリ側で算出する。
// 単価またはドル円レートが未設定の場合は EstimatedCostJpy = null となる。
public sealed record TokenUsageResult(
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    decimal? EstimatedCostJpy);
