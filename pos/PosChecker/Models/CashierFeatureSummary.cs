namespace PosChecker.Models;

// 店舗×担当者ごとの機械集計 (= 判定単位)。不正一覧の事象1・2・4・5 に対応する特徴量。
public sealed record CashierFeatureSummary(
    string StoreCode,
    string CashierCode,
    string CashierName,
    int TransactionCount,
    int SalesCount,
    int ReturnCount,
    double ReturnRatio,
    long ReturnAmount,
    int RekeyCount,
    double RekeyRatio,
    int DistinctMemberCount,
    string? TopMemberCode,
    int TopMemberSalesCount,
    double MemberConcentration,
    int SameItemRepeatCount,
    int ShortIntervalSameMemberCount,
    int AfterHoursReturnRekeyCount);
