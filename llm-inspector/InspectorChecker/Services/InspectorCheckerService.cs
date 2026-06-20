namespace InspectorChecker.Services;

using InspectorChecker.Models;
using InspectorChecker.Settings;

using Microsoft.Extensions.Logging;

public sealed class InspectorCheckerService
{
    private readonly ILogger<InspectorCheckerService> log;
    private readonly SurveyCsvLoader csvLoader;
    private readonly InspectionFeatureSummaryBuilder featureSummaryBuilder;
    private readonly InspectionFraudAnalyzer fraudAnalyzer;
    private readonly InspectorCheckerSettings settings;

    public InspectorCheckerService(
        ILogger<InspectorCheckerService> log,
        SurveyCsvLoader csvLoader,
        InspectionFeatureSummaryBuilder featureSummaryBuilder,
        InspectionFraudAnalyzer fraudAnalyzer,
        InspectorCheckerSettings settings)
    {
        this.log = log;
        this.csvLoader = csvLoader;
        this.featureSummaryBuilder = featureSummaryBuilder;
        this.fraudAnalyzer = fraudAnalyzer;
        this.settings = settings;
    }

    public async Task<InspectorCheckResult> AnalyzeAsync(
        string filePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        log.InfoInspectionAnalysisStarted(filePath);
        progress?.Report("CSVを読み込み中...");

        await using var stream = File.OpenRead(filePath);
        var records = await csvLoader.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
        var distinctCustomerCount = records
            .Select(x => x.CustomerId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        log.InfoInspectionRowsLoaded(records.Count, distinctCustomerCount);

        progress?.Report("顧客ごとの基準値を集計中...");
        var featureSummary = featureSummaryBuilder.Build(records);
        log.InfoFeatureSummaryBuilt(featureSummary.DailySummaries.Count, featureSummary.RepeatedDailyTemplates.Count);

        progress?.Report("Foundryで不正パターンを判定中...");
        var (analysis, usage) = await fraudAnalyzer.AnalyzeAsync(featureSummary, records, cancellationToken).ConfigureAwait(false);
        log.InfoInspectionAnalysisCompleted(analysis.OverallScore);

        return new InspectorCheckResult(
            Path.GetFileName(filePath),
            featureSummary,
            analysis,
            usage,
            records.Take(settings.PreviewRowCount).ToArray());
    }
}
