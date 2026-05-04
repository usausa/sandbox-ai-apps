namespace Flyer.Services;

using Flyer.Services.Models;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

/// <summary>
/// Wraps the Azure AI Search vector collection for product master data.
/// </summary>
public sealed class ProductVectorStore
{
    private readonly VectorStoreCollection<string, ProductRecord> collection;
    private readonly IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator;

    public ProductVectorStore(
        VectorStoreCollection<string, ProductRecord> collection,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(embeddingGenerator);
        this.collection = collection;
        this.embeddingGenerator = embeddingGenerator;
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await collection.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> UpsertAsync(IAsyncEnumerable<MasterRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var count = 0;
        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await collection.UpsertAsync(new ProductRecord
            {
                Id = record.Id,
                Name = record.Name,
                Price = record.Price,
                NameEmbedding = record.Name
            }, cancellationToken).ConfigureAwait(false);
            count++;
        }

        return count;
    }

    public async Task<IReadOnlyList<ProductRecord>> SearchAsync(
        string query,
        int top = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var results = new List<ProductRecord>(top);
        await foreach (var hit in collection.SearchAsync(query, top, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            results.Add(hit.Record);
        }

        return results;
    }
}
