namespace PosChecker.Models;

// 決定論的に検出した不正シグナル。LLM へ根拠として渡す。
// Kind 例: CashierMemberConcentration / SameItemMultiBuy / ShortIntervalSameMember /
//          RepeatedCouponByMember / HighReturnCashier / HighRekeyCashier。
public sealed record FraudSignal(
    string Kind,
    string StoreCode,
    string? CashierCode,
    string? MemberCode,
    string Detail,
    int? Count,
    double? Ratio);
