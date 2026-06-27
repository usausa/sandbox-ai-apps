namespace AutoOrder.Agent;

// Azure AI Foundry への接続設定。エンドポイント・モデル・認証情報は構成（user secrets / 環境変数）で注入する。
public sealed class FoundrySettings
{
    public string Endpoint { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    public string ChatDeployment { get; set; } = string.Empty;
}
