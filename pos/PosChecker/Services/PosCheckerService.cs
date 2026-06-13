namespace PosChecker.Services;

using Microsoft.Extensions.Logging;

using PosChecker.Models;
using PosChecker.Settings;

public sealed class PosCheckerService
{
    private readonly TransactionCsvLoader loader;
    private readonly PosFeatureSummaryBuilder summaryBuilder;
    private readonly PosFraudAnalyzer analyzer;
    private readonly PosCheckerSettings settings;
    private readonly ILogger<PosCheckerService> log;

    public PosCheckerService(
        TransactionCsvLoader loader,
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
        string filePath,
        IProgress<string> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(progress);

        log.InfoPosAnalysisStarted(filePath);

        progress.Report("CSVを読み込み中...");
        await using var stream = File.OpenRead(filePath);
        var records = await loader.LoadAsync(stream, cancellationToken).ConfigureAwait(false);

        var cashierCount = records.Select(x => x.CashierId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        log.InfoPosRowsLoaded(records.Count, cashierCount);

        progress.Report("特徴量を集計中...");
        var featureSummary = summaryBuilder.Build(records);
        log.InfoFeatureSummaryBuilt(featureSummary.DailySummaries.Count, featureSummary.SequenceAnomalies.Count);

        progress.Report("AIで分析中...");
        var analysis = await analyzer.AnalyzeAsync(featureSummary, records, cancellationToken).ConfigureAwait(false);
        log.InfoPosAnalysisCompleted(analysis.OverallScore);

        var previewRows = records.Take(settings.PreviewRowCount).ToArray();
        return new PosCheckResult(Path.GetFileName(filePath), featureSummary, analysis, previewRows);
    }
}
