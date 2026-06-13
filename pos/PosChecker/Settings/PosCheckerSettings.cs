namespace PosChecker.Settings;

public sealed class PosCheckerSettings
{
    public string UploadPath { get; set; } = "./upload";

    public int PreviewRowCount { get; set; } = 12;

    // Sale 直後の取消 (SaleThenVoid / PointsRedeemThenVoid) とみなす時間窓。
    public int SaleThenVoidWindowSeconds { get; set; } = 180;

    // 同額返品の反復を異常とみなす最小回数。
    public int RepeatedRefundMinOccurrences { get; set; } = 2;

    // 営業時間帯。これより前/後の取消・返品を afterHours として数える。
    public int BusinessHoursStartHour { get; set; } = 9;

    public int BusinessHoursEndHour { get; set; } = 21;

    // 高額返品の目安 (プロンプトへ供給)。
    public int HighValueReturnAmount { get; set; } = 5000;

    // ポイント利用の会員偏りの目安 (プロンプトへ供給)。
    public double RedeemConcentrationThreshold { get; set; } = 0.5;
}
