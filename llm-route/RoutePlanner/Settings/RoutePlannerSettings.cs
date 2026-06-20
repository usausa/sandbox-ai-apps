namespace RoutePlanner.Settings;

public sealed class RoutePlannerSettings
{
    public string UploadPath { get; set; } = "./upload";

    public int PreviewRowCount { get; set; } = 12;

    // AIレビューに渡す「各訪問先から足順順で次の何件先までの距離」を前計算する件数。
    // LLM が座標から距離を推測せずに行き来(交差)を判定できるようにするための補助情報。
    public int ReviewLookaheadCount { get; set; } = 3;
}
