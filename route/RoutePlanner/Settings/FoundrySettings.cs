namespace RoutePlanner.Settings;

public sealed class FoundrySettings
{
    public string Endpoint { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    public string ChatDeployment { get; set; } = string.Empty;
}
