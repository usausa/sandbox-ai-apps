namespace PosChecker.Models;

// 3ビューCSVを読み込み結合した分析対象データ一式。
public sealed record PosDataset(
    IReadOnlyList<SalesTransaction> Transactions,
    IReadOnlyList<PromotionRecord> Promotions);
