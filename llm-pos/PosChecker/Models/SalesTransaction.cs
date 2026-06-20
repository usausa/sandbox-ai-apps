namespace PosChecker.Models;

// 売上ヘッダと明細を結合した1取引。集計・分析の基本単位。
public sealed record SalesTransaction(
    SalesHeaderRecord Header,
    IReadOnlyList<SalesDetailRecord> Details);
