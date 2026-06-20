namespace PosChecker.Models;

// 会員ごとの機械集計。不正一覧の事象1 (会員→担当者偏り) と事象3 (クーポン反復) に対応。
public sealed record MemberFeatureSummary(
    string MemberCode,
    int SalesCount,
    int DistinctCashierCount,
    string? TopCashierCode,
    double CashierConcentration,
    int CouponUseCount,
    int RepeatedCouponCount,
    int MemberLimitedCouponCount,
    int AppCouponCount);
