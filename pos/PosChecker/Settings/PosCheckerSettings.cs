namespace PosChecker.Settings;

public sealed class PosCheckerSettings
{
    public string UploadPath { get; set; } = "./upload";

    public int PreviewRowCount { get; set; } = 12;

    // 同一会員の「短時間連続会計」とみなす取引間隔（秒）。事象1のシグナル。
    public int ShortIntervalSeconds { get; set; } = 300;

    // 担当者の売上が特定会員に偏っているとみなす占有率の目安（プロンプトへ供給）。事象1。
    public double MemberConcentrationThreshold { get; set; } = 0.3;

    // 担当者の返品率が高いとみなす目安（プロンプトへ供給）。事象4。
    public double HighReturnRatioThreshold { get; set; } = 0.1;

    // 担当者の打直率が高いとみなす目安（プロンプトへ供給）。事象5。
    public double HighRekeyRatioThreshold { get; set; } = 0.1;

    // 同一会員が同一クーポンを反復利用とみなす最小回数。事象3。偶然の重複と区別するため3。
    public int RepeatedCouponMinOccurrences { get; set; } = 3;

    // 同一JAN/用途を複数購入する会計とみなす最小回数。事象2。
    public int SameItemMinOccurrences { get; set; } = 2;

    // 営業時間帯。これより前/後の返品・打直を afterHours として数える。
    public int BusinessHoursStartHour { get; set; } = 9;

    public int BusinessHoursEndHour { get; set; } = 21;

    // 練習(Practice)・スキャンチェック(ScanCheck)の取引を不正集計から除外するか。
    public bool ExcludeNonNormalRegister { get; set; } = true;
}
