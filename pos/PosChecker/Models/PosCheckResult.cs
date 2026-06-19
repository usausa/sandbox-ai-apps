namespace PosChecker.Models;

public sealed record PosCheckResult(
    PosFeatureSummary FeatureSummary,
    PosAnalysisResult Analysis,
    TokenUsageResult Usage,
    IReadOnlyList<SalesTransaction> PreviewTransactions,
    IReadOnlyList<PromotionRecord> PreviewPromotions);
