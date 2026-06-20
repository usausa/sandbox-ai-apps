namespace PosChecker.Services;

using Microsoft.Extensions.Logging;

using PosChecker.Models;
using PosChecker.Settings;

public sealed class PosCheckerService
{
    private readonly PosDataLoader loader;
    private readonly PosFeatureSummaryBuilder summaryBuilder;
    private readonly PosFraudAnalyzer analyzer;
    private readonly PosCheckerSettings settings;
    private readonly ILogger<PosCheckerService> log;

    public PosCheckerService(
        PosDataLoader loader,
        PosFeatureSummaryBuilder summaryBuilder,
        PosFraudAnalyzer analyzer,
        PosCheckerSettings settings,
        ILogger<PosCheckerService> log)
    {
        this.loader = loader;
        this.summaryBuilder = summaryBuilder;
        this.analyzer = analyzer;
        this.settings = settings;
        this.log = log;
    }

    public async Task<PosCheckResult> AnalyzeAsync(
        string headerPath,
        string detailPath,
        string promotionPath,
        IProgress<string> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(headerPath);
        ArgumentNullException.ThrowIfNull(detailPath);
        ArgumentNullException.ThrowIfNull(promotionPath);
        ArgumentNullException.ThrowIfNull(progress);

        log.InfoPosAnalysisStarted(headerPath);

        progress.Report("CSVを読み込み中...");
        await using var headerStream = File.OpenRead(headerPath);
        await using var detailStream = File.OpenRead(detailPath);
        await using var promotionStream = File.OpenRead(promotionPath);
        var dataset = await loader.LoadAsync(headerStream, detailStream, promotionStream, cancellationToken).ConfigureAwait(false);

        var cashierCount = dataset.Transactions
            .Select(x => (x.Header.StoreCode, x.Header.CashierCode))
            .Distinct()
            .Count();
        log.InfoPosRowsLoaded(dataset.Transactions.Count, cashierCount);

        progress.Report("特徴量を集計中...");
        var featureSummary = summaryBuilder.Build(dataset);
        log.InfoFeatureSummaryBuilt(featureSummary.CashierSummaries.Count, featureSummary.FraudSignals.Count);

        progress.Report("AIで分析中...");
        var (analysis, usage) = await analyzer.AnalyzeAsync(featureSummary, dataset, cancellationToken).ConfigureAwait(false);
        log.InfoPosAnalysisCompleted(analysis.OverallScore);

        var previewTransactions = dataset.Transactions.Take(settings.PreviewRowCount).ToArray();
        var previewPromotions = dataset.Promotions.Take(settings.PreviewRowCount).ToArray();
        return new PosCheckResult(featureSummary, analysis, usage, previewTransactions, previewPromotions);
    }
}
