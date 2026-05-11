namespace FlyerChecker.Services;

using FlyerChecker.Models;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

/// <summary>Azure AI Search のベクトルコレクションを使って商品の登録・あいまい検索を行うサービス。</summary>
public sealed class ProductService
{
    private readonly VectorStoreCollection<string, ProductRecord> collection;
    private readonly IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator;

    public ProductService(
        VectorStoreCollection<string, ProductRecord> collection,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(embeddingGenerator);
        this.collection = collection;
        this.embeddingGenerator = embeddingGenerator;
    }

    /// <summary>インデックスが存在しない場合に作成する。</summary>
    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        await collection.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>既存インデックスを削除して再作成する。</summary>
    public async Task RecreateIndexAsync(CancellationToken cancellationToken = default)
    {
        await collection.EnsureCollectionDeletedAsync(cancellationToken).ConfigureAwait(false);
        await collection.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>商品を登録（または更新）する。</summary>
    public async Task RegisterProductAsync(
        string id,
        string name,
        int price,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var result = await embeddingGenerator.GenerateAsync([name], cancellationToken: cancellationToken).ConfigureAwait(false);
        var embedding = result[0].Vector;

        await collection.UpsertAsync(new ProductRecord
        {
            Id = id,
            Name = name,
            Price = price,
            Category = category,
            NameEmbedding = embedding
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>複数商品を一括登録し、登録件数を返す。</summary>
    public async Task<int> RegisterProductsAsync(
        IAsyncEnumerable<MasterRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var count = 0;
        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await RegisterProductAsync(record.Id, record.Name, record.Price, record.Category, cancellationToken).ConfigureAwait(false);
            count++;
        }

        return count;
    }

    /// <summary>クエリ文字列でベクトル検索を行い、上位 <paramref name="top"/> 件を返す。</summary>
    public async Task<IReadOnlyList<ProductSearchResult>> SearchAsync(
        string query,
        int top = 5,
        double minScore = 0.75,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);

        var queryResult = await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken).ConfigureAwait(false);
        var queryEmbedding = queryResult[0].Vector;

        var results = new List<ProductSearchResult>(top);
        await foreach (var hit in collection.SearchAsync(queryEmbedding, top, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            var score = hit.Score ?? 0;
            if (score < minScore)
            {
                continue;
            }

            results.Add(new ProductSearchResult(
                hit.Record.Id,
                hit.Record.Name,
                hit.Record.Price,
                hit.Record.Category,
                score));
        }

        return results;
    }
}
