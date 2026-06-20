namespace FlyerChecker.Services;

using FlyerChecker.Models;

using Microsoft.Extensions.Logging;

// チラシ画像の広告価格チェックを一括実行するサービス
public sealed class FlyerCheckerService
{
    private readonly ILogger<FlyerCheckerService> log;
    private readonly FlyerImageReader flyerImageReader;
    private readonly ProductService productService;
    private readonly PriceDifferenceAnalyzer priceDifferenceAnalyzer;

    public FlyerCheckerService(
        ILogger<FlyerCheckerService> log,
        FlyerImageReader flyerImageReader,
        ProductService productService,
        PriceDifferenceAnalyzer priceDifferenceAnalyzer)
    {
        this.log = log;
        this.flyerImageReader = flyerImageReader;
        this.productService = productService;
        this.priceDifferenceAnalyzer = priceDifferenceAnalyzer;
    }

    // チラシ画像ファイルを解析し、商品毎の価格チェック結果をストリームで返す
    public async IAsyncEnumerable<PriceCheckResult> CheckAsync(
        string filePath,
        int searchTop = 5,
        double minScore = 0.75,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        log.InfoFlyerCheckStarted(filePath);

        var items = await flyerImageReader.ReadAsync(filePath, cancellationToken).ConfigureAwait(false);
        log.InfoFlyerItemsExtracted(items.Count);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidates = await productService.SearchAsync(
                item.Name,
                searchTop,
                minScore,
                cancellationToken).ConfigureAwait(false);
            var result = await priceDifferenceAnalyzer.AnalyzeAsync(item, candidates, cancellationToken).ConfigureAwait(false);

            log.InfoCheckedItem(item.Name, item.Price, result.MasterName, result.MasterPrice, result.Difference);

            yield return result;
        }

        log.InfoFlyerCheckCompleted(items.Count);
    }
}
