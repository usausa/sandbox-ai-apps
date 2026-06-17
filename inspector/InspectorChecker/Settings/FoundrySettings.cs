namespace InspectorChecker.Settings;

public sealed class FoundrySettings
{
    public string Endpoint { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    public string ChatDeployment { get; set; } = string.Empty;

    // 概算費用の算出に使う USD 単価（100万トークンあたり）。Foundry は費用を返さないためここで設定する。
    // モデル固有の値なので、モデルを変えない限り固定でよい。0 のままだと費用は算出しない。
    public decimal InputPricePer1M { get; set; }

    public decimal OutputPricePer1M { get; set; }

    // USD→円 換算レート（ドル円）。為替変動時はこの値だけ更新する。0 以下なら費用を算出しない。
    public decimal UsdJpyRate { get; set; }
}
