namespace Flyer.Services.Models;

using Microsoft.Extensions.VectorData;

public sealed class ProductRecord
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData(IsFullTextIndexed = true)]
    public string Name { get; set; } = string.Empty;

    [VectorStoreData]
    public int Price { get; set; }

    [VectorStoreData]
    public string? Category { get; set; }

    [VectorStoreVector(Dimensions: 1536, DistanceFunction = DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> NameEmbedding { get; set; }
}
