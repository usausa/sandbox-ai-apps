namespace FlyerChecker.Services;

using FlyerChecker.Models;

using Microsoft.Extensions.Logging;

// チラシ画像の広告価格チェックを一括実行するサービス
public sealed class FlyerCheckerService
{
    private readonly FlyerImageReader flyerImageReader;
    private readonly ProductService productService;
    private readonly PriceDifferenceAnalyzer priceDifferenceAnalyzer;
    private readonly ILogger<FlyerCheckerService> logger;

    public FlyerCheckerService(
        FlyerImageReader flyerImageReader,
        ProductService productService,
        PriceDifferenceAnalyzer priceDifferenceAnalyzer,
        ILogger<FlyerCheckerService> logger)
    {
        ArgumentNullException.ThrowIfNull(flyerImageReader);
        ArgumentNullException.ThrowIfNull(productService);
        ArgumentNullException.ThrowIfNull(priceDifferenceAnalyzer);
        ArgumentNullException.ThrowIfNull(logger);
        this.flyerImageReader = flyerImageReader;
        this.productService = productService;
        this.priceDifferenceAnalyzer = priceDifferenceAnalyzer;
        this.logger = logger;
    }

    // チラシ画像ファイルを解析し、商品毎の価格チェック結果をストリームで返す
    public async IAsyncEnumerable<PriceCheckResult> CheckAsync(
        string filePath,
        int searchTop = 5,
        double minScore = 0.75,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        logger.LogInformation("Flyer check started. filePath=[{FilePath}]", filePath);

        var items = await flyerImageReader.ReadAsync(filePath, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Flyer items extracted. count=[{Count}]", items.Count);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidates = await productService.SearchAsync(item.Name, searchTop, minScore, cancellationToken).ConfigureAwait(false);
            var result = await priceDifferenceAnalyzer.AnalyzeAsync(item, candidates, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Checked item=[{Item}], flyerPrice=[{FlyerPrice}], masterName=[{MasterName}], masterPrice=[{MasterPrice}], diff=[{Diff}]",
                item.Name, item.Price, result.MasterName, result.MasterPrice, result.Difference);

            yield return result;
        }

        logger.LogInformation("Flyer check completed. itemCount=[{Count}]", items.Count);
    }
}
