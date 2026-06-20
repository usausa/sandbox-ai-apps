namespace PosChecker.Models;

// ファイル全体 (複数店舗・複数担当者・複数会員) の機械集計。
public sealed record PosFeatureSummary(
    int TransactionCount,
    int DetailCount,
    int PromotionCount,
    int StoreCount,
    int CashierCount,
    int MemberCount,
    DateOnly StartDate,
    DateOnly EndDate,
    int SalesCount,
    long SalesAmount,
    int ReturnCount,
    long ReturnAmount,
    int RekeyCount,
    IReadOnlyList<TransactionTypeBreakdown> TypeDistribution,
    IReadOnlyList<TenderBreakdown> TenderDistribution,
    IReadOnlyList<CashierFeatureSummary> CashierSummaries,
    IReadOnlyList<MemberFeatureSummary> MemberSummaries,
    IReadOnlyList<FraudSignal> FraudSignals);
