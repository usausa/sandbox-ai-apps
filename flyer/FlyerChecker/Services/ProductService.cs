namespace FlyerChecker.Services;

using FlyerChecker.Models;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

// Azure AI Search のベクトルコレクションを使って商品の登録・あいまい検索を行うサービス
public sealed class ProductService
{
    private readonly ILogger<ProductService> log;
    private readonly VectorStoreCollection<string, ProductRecord> collection;
    private readonly IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator;

    public ProductService(
        ILogger<ProductService> log,
        VectorStoreCollection<string, ProductRecord> collection,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        this.log = log;
        this.collection = collection;
        this.embeddingGenerator = embeddingGenerator;
    }

    // 既存インデックスを削除して再作成する
    public async Task RecreateIndexAsync(CancellationToken cancellationToken = default)
    {
        await collection.EnsureCollectionDeletedAsync(cancellationToken).ConfigureAwait(false);
        await collection.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
        log.InfoIndexRecreated();
    }

    // 商品を登録(または更新)する
    public async Task RegisterProductAsync(
        string id,
        string name,
        int price,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var result = await embeddingGenerator.GenerateAsync(
            [name],
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var embedding = result[0].Vector;

        await collection.UpsertAsync(
            new ProductRecord
            {
                Id = id,
                Name = name,
                Price = price,
                Category = category,
                NameEmbedding = embedding
            },
            cancellationToken).ConfigureAwait(false);
        log.InfoProductRegistered(id, name);
    }
    public async Task<int> RegisterProductsAsync(
        IAsyncEnumerable<MasterRecord> records,
        CancellationToken cancellationToken = default)
    {
        var count = 0;
        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await RegisterProductAsync(record.Id, record.Name, record.Price, record.Category, cancellationToken).ConfigureAwait(false);
            count++;
        }

        return count;
    }

    // クエリ文字列でベクトル検索を行い、上位 top 件を返す
    public async Task<IReadOnlyList<ProductSearchResult>> SearchAsync(
        string query,
        int top = 5,
        double minScore = 0.75,
        CancellationToken cancellationToken = default)
    {
        var queryResult = await embeddingGenerator.GenerateAsync(
            [query],
            cancellationToken: cancellationToken).ConfigureAwait(false);
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
