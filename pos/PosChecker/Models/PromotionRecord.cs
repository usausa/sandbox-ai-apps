namespace PosChecker.Models;

// Promotion.csv の1行 = 1クーポンスキャン（クーポン企画情報を結合済み）。
// キー = (StoreCode, SalesDate, PosNo, SlipNo)。
public sealed record PromotionRecord(
    string StoreCode,
    DateOnly SalesDate,
    int PosNo,
    int SlipNo,
    string PlanCode,
    string PlanName,
    string CouponCode,
    string? ScannedMemberCode,
    string CouponJan,
    IssueType IssueType,
    string CouponName,
    DateOnly? StartDate,
    DateOnly? EndDate,
    MemberTargetType MemberTargetType,
    int Sequence)
{
    // 会員限定クーポンか (会員指定 / クレジット会員 / 楽天会員系)。
    public bool IsMemberLimited =>
        MemberTargetType is MemberTargetType.SpecifiedMembers
            or MemberTargetType.CreditMembers
            or MemberTargetType.RakutenMembers
            or MemberTargetType.SdAndRakuten
            or MemberTargetType.SdNotRakuten;

    // アプリクーポン系か (名称ベース判定)。
    public bool IsAppCoupon =>
        CouponName.Contains("アプリ", StringComparison.Ordinal) ||
        PlanName.Contains("アプリ", StringComparison.Ordinal);
}
