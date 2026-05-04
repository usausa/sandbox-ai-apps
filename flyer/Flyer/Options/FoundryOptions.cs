namespace Flyer.Options;

public sealed class FoundryOptions
{
    public string Endpoint { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    public string ChatDeployment { get; set; } = string.Empty;

    public string EmbeddingDeployment { get; set; } = string.Empty;

    public int EmbeddingDimensions { get; set; } = 1536;
}
