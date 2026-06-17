namespace RoutePlanner.Services;

using Microsoft.Extensions.Logging;

using RoutePlanner.Models;
using RoutePlanner.Settings;

// 足順決定パイプラインの統括。CSV読込 → LLM に足順生成とレビューを一括で委譲する。
// route と異なり前処理(最適化アルゴリズム)は行わず、訪問順序も移動・時刻の数値も全て LLM が生成する。
public sealed class RoutePlannerService
{
    private readonly ILogger<RoutePlannerService> log;
    private readonly RoutePlanGenerator generator;
    private readonly RoutePlannerSettings settings;

    public RoutePlannerService(
        ILogger<RoutePlannerService> log,
        RoutePlanGenerator generator,
        RoutePlannerSettings settings)
    {
        this.log = log;
        this.generator = generator;
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

        progress?.Report("Foundryで足順を生成・検証中...");
        var (plan, review, usage) = await generator.GenerateAsync(visits, common, cancellationToken).ConfigureAwait(false);
        log.InfoRoutePlanGenerated(plan.VisitCount, plan.OvertimeMinutes, plan.WindowViolationCount, plan.UnassignedVisits.Count);
        log.InfoRouteReviewCompleted(review.FeasibilityScore);

        return new RoutePlanResult(
            Path.GetFileName(filePath),
            common,
            plan,
            review,
            usage,
            visits.Take(settings.PreviewRowCount).ToArray());
    }
}
