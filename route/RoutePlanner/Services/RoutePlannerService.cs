namespace RoutePlanner.Services;

using Microsoft.Extensions.Logging;

using RoutePlanner.Models;
using RoutePlanner.Settings;

// 足順決定パイプラインの統括。CSV読込 → 最適化 → AIレビュー の順に実行する。
public sealed class RoutePlannerService
{
    private readonly ILogger<RoutePlannerService> log;
    private readonly RouteOptimizer optimizer;
    private readonly RouteReviewAnalyzer reviewAnalyzer;
    private readonly RoutePlannerSettings settings;

    public RoutePlannerService(
        ILogger<RoutePlannerService> log,
        RouteOptimizer optimizer,
        RouteReviewAnalyzer reviewAnalyzer,
        RoutePlannerSettings settings)
    {
        this.log = log;
        this.optimizer = optimizer;
        this.reviewAnalyzer = reviewAnalyzer;
        this.settings = settings;
    }

    public async Task<RoutePlanResult> PlanAsync(
        string filePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        log.InfoRouteAnalysisStarted(filePath);
        progress?.Report("訪問先CSVを読み込み中...");

        await using var stream = File.OpenRead(filePath);
        var visits = await VisitCsvLoader.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
        log.InfoVisitsLoaded(visits.Count);

        // サンプルでは共通設定は既定値を使用する（将来は画面フォームから受け取る）。
        var common = new CommonSettings
        {
            WorkDate = DateOnly.FromDateTime(DateTime.Today)
        };

        progress?.Report("足順を最適化中...");
        var plan = optimizer.Optimize(visits, common);
        log.InfoRouteOptimized(plan.VisitCount, plan.OvertimeMinutes, plan.WindowViolationCount, plan.UnassignedVisits.Count);

        progress?.Report("Foundryで足順を検証・改善提案中...");
        var review = await reviewAnalyzer.AnalyzeAsync(plan, common, cancellationToken).ConfigureAwait(false);
        log.InfoRouteReviewCompleted(review.FeasibilityScore);

        return new RoutePlanResult(
            Path.GetFileName(filePath),
            common,
            plan,
            review,
            visits.Take(settings.PreviewRowCount).ToArray());
    }
}
