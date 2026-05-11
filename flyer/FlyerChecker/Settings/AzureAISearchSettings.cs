namespace FlyerChecker.Settings;

public sealed class AzureAISearchSettings
{
    public string Endpoint { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    public string IndexName { get; set; } = "flyer-products";
}
