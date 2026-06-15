namespace RoutePlanner.Services;

using RoutePlanner.Models;

// 時間帯指定を考慮した最近傍法ベースの足順生成。
// 出発拠点から、最も早く着手できる訪問先を順に選び、昼休憩を挿入し、拠点へ帰着する。
// 地域結束: 時間帯指定の無い訪問は、現在いる地域(約250m四方のセル)の未訪問が残る限りそこを優先的に
// 片付け、「隣接地域を2回に分けて回る」分割訪問を抑制する。ただし時間帯指定が間近に迫る訪問が
// あるときは結束を一時停止して枠を優先する（空間的まとまりより時間帯遵守を上位に置く）。
// TODO: 2-opt / Or-opt の局所探索で移動時間をさらに短縮する（時間枠の実行可能性を保ちつつ入れ替える）。
public sealed class RouteOptimizer
{
    // 同一「地域」とみなすグリッドの一辺（度）。約250m四方。
    private const double AreaCellSizeDegrees = 0.0025;

    // 地域離脱に与える擬似コスト。実距離より十分大きくし、未訪問の残る地域を先に片付けさせる。
    private const double AreaCohesionPenalty = 1000.0;

    // 終了がこの分数以内に迫る時間帯指定訪問があれば「枠が間近」とみなし、地域結束を一時停止する。
    private const int WindowUrgencyMarginMinutes = 10;

    // 移動時間(分)は切り上げで粗いため、近距離だと候補が同点になり入力順に落ちてしまう。
    // 実距離(km)をごく小さな重みで加え、分が同点のとき地理的に近い候補を先に選ぶタイブレークにする。
    // 重みは 1 分の差(スコア約 1.1)を覆さない程度に小さく保つ。
    private const double DistanceTieBreakWeight = 0.1;

    // 時間帯遵守: 窓が開いていて今なら枠内に着手できる候補を、枠なし候補より一定だけ優先する。
    // 幾何（近さ）の順序は保ちつつ、近場の枠なし候補に埋もれて指定時刻から大きく外れるのを防ぐ。
    // 値はおよそ「この分数までの遠回りなら枠付きを先に取りに行く」を意味する。
    private const double WindowUrgencyBonus = 20.0;

    // 窓が「開いている」とみなす先読み余裕（分）。開始がこの分数以内に迫った窓も対象にする。
    private const int WindowActiveHorizonMinutes = 0;

    public RoutePlan Optimize(IReadOnlyList<VisitTarget> visits, CommonSettings common)
    {
        ArgumentNullException.ThrowIfNull(visits);
        ArgumentNullException.ThrowIfNull(common);

        var remaining = visits.ToList();
        var stops = new List<RouteStop>();

        var departMinutes = ToMinutes(common.EffectiveDepartTime);
        var workEndMinutes = ToMinutes(common.WorkEnd);
        var lunchStartMinutes = ToMinutes(common.LunchStart);
        var endLimit = workEndMinutes + (common.AllowOvertime ? Math.Max(0, common.MaxOvertimeMinutes) : 0);
        var maxVisits = common.MaxVisits ?? int.MaxValue;

        var order = 1;
        var currentMinutes = departMinutes;
        var currentLat = common.OfficeLatitude;
        var currentLon = common.OfficeLongitude;
        var departingFromVisit = false;
        string? currentBuildingGroupId = null;
        (long Row, long Col)? currentArea = null;
        var lunchTaken = common.LunchMinutes <= 0;

        var totalKm = 0.0;
        var totalTravel = 0;
        var totalService = 0;
        var totalWait = 0;
        var violationCount = 0;
        var visitCount = 0;

        stops.Add(new RouteStop(
            order++,
            StopKind.Office,
            "OFFICE_START",
            "出発（事業所）",
            null,
            FromMinutes(currentMinutes),
            FromMinutes(currentMinutes),
            0,
            0,
            0,
            0,
            false));

        while (remaining.Count > 0 && visitCount < maxVisits)
        {
            if (!lunchTaken && currentMinutes >= lunchStartMinutes)
            {
                var lunchEndMinutes = currentMinutes + common.LunchMinutes;
                stops.Add(new RouteStop(
                    order++,
                    StopKind.Lunch,
                    "LUNCH",
                    "昼休憩",
                    null,
                    FromMinutes(currentMinutes),
                    FromMinutes(lunchEndMinutes),
                    common.LunchMinutes,
                    0,
                    0,
                    0,
                    false));
                currentMinutes = lunchEndMinutes;
                lunchTaken = true;
                continue;
            }

            VisitTarget? best = null;
            var bestKm = 0.0;
            var bestTravel = 0;
            var bestArrive = 0;
            var bestStart = 0;
            var bestWait = 0;
            var bestViolation = false;
            var bestScore = double.MaxValue;

            // 現在地域に時間帯指定の無い未訪問が残っているか（残っていれば地域結束の対象）。
            var currentAreaHasWindowlessRest = currentArea.HasValue
                && remaining.Any(v => !v.WindowEnd.HasValue && AreaKey(v.Latitude, v.Longitude) == currentArea.Value);

            // 終了が間近に迫る時間帯指定訪問があるか（あれば結束を停止し、枠を優先して取りに行く）。
            var windowUrgentPending = remaining.Any(v =>
                v.WindowEnd.HasValue
                && (!v.WindowStart.HasValue || ToMinutes(v.WindowStart.Value) <= currentMinutes + WindowUrgencyMarginMinutes)
                && ToMinutes(v.WindowEnd.Value) - currentMinutes <= WindowUrgencyMarginMinutes);

            foreach (var candidate in remaining)
            {
                var leg = TravelTimeMatrixBuilder.Build(currentLat, currentLon, candidate.Latitude, candidate.Longitude, common);

                // 同一建物（集合住宅）内の連続調査は移動・準備バッファを要しないため 0 とする。
                var sameBuilding = !string.IsNullOrEmpty(candidate.BuildingGroupId)
                    && string.Equals(candidate.BuildingGroupId, currentBuildingGroupId, StringComparison.Ordinal);
                var buffer = departingFromVisit && !sameBuilding ? common.BufferMinutes : 0;
                var arrive = currentMinutes + leg.TravelMinutes + buffer;

                int? windowStart = candidate.WindowStart.HasValue ? ToMinutes(candidate.WindowStart.Value) : null;
                int? windowEnd = candidate.WindowEnd.HasValue ? ToMinutes(candidate.WindowEnd.Value) : null;

                var startService = windowStart > arrive ? windowStart.Value : arrive;
                var wait = startService - arrive;

                var violation = false;
                if (startService > windowEnd)
                {
                    if (candidate.WindowStrict == TimeWindowStrictness.Strict)
                    {
                        continue;
                    }

                    violation = true;
                }

                var service = EffectiveService(candidate, common);
                var finish = startService + service;
                if (finish > endLimit)
                {
                    continue;
                }

                var score = startService
                    + (leg.TravelMinutes * 0.1)
                    + (leg.DistanceKm * DistanceTieBreakWeight)
                    + PriorityPenalty(candidate.Priority)
                    + (windowEnd.HasValue ? 0 : 5);

                // 時間帯遵守: 窓が開いていて今なら枠内に着手できる候補を一定だけ優先する。
                // 指定時刻から大きく外れた割当（枠超過）を防ぎつつ、近さの順序は保つ。
                if (windowEnd.HasValue
                    && startService <= windowEnd.Value
                    && (!windowStart.HasValue || windowStart.Value <= currentMinutes + WindowActiveHorizonMinutes))
                {
                    score -= WindowUrgencyBonus;
                }

                // 地域結束: 現在地域に未訪問(時間帯指定なし)が残るのに、別地域の時間帯指定なし候補へ移ろうと
                // する場合は擬似コストを加算して抑制する。枠が間近のときは結束を止め、枠を優先する。
                if (departingFromVisit
                    && currentAreaHasWindowlessRest
                    && !windowUrgentPending
                    && !windowEnd.HasValue
                    && AreaKey(candidate.Latitude, candidate.Longitude) != currentArea!.Value)
                {
                    score += AreaCohesionPenalty;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                    bestKm = leg.DistanceKm;
                    bestTravel = leg.TravelMinutes;
                    bestArrive = arrive;
                    bestStart = startService;
                    bestWait = wait;
                    bestViolation = violation;
                }
            }

            if (best is null)
            {
                break;
            }

            // 同一建物・同一時間帯の住戸は名称順（部屋番号順）に回す。
            // どの建物へ向かうかはスコアで決め、その建物・同一時間帯の中では最小名称の住戸へ寄せる。
            // 同一座標・同一時間帯のため移動/到着/着手/違反は best と同一で、所要のみ住戸ごとに異なる。
            if (!string.IsNullOrEmpty(best.BuildingGroupId))
            {
                var nameFirst = remaining
                    .Where(v => string.Equals(v.BuildingGroupId, best.BuildingGroupId, StringComparison.Ordinal)
                        && Nullable.Equals(v.WindowStart, best.WindowStart)
                        && Nullable.Equals(v.WindowEnd, best.WindowEnd))
                    .OrderBy(v => v.CustomerName, StringComparer.Ordinal)
                    .First();

                // 名称順の先頭が勤務終了(＋許容残業)を超えない場合のみ寄せる。
                if (!ReferenceEquals(nameFirst, best)
                    && bestStart + EffectiveService(nameFirst, common) <= endLimit)
                {
                    best = nameFirst;
                }
            }

            var bestService = EffectiveService(best, common);
            var departure = bestStart + bestService;

            stops.Add(new RouteStop(
                order++,
                StopKind.Visit,
                best.VisitId,
                string.IsNullOrWhiteSpace(best.CustomerName) ? best.VisitId : best.CustomerName,
                best,
                FromMinutes(bestArrive),
                FromMinutes(departure),
                bestService,
                bestKm,
                bestTravel,
                bestWait,
                bestViolation));

            totalKm += bestKm;
            totalTravel += bestTravel;
            totalService += bestService;
            totalWait += bestWait;
            if (bestViolation)
            {
                violationCount++;
            }

            currentMinutes = departure;
            currentLat = best.Latitude;
            currentLon = best.Longitude;
            currentBuildingGroupId = best.BuildingGroupId;
            currentArea = AreaKey(best.Latitude, best.Longitude);
            departingFromVisit = true;
            visitCount++;
            remaining.Remove(best);
        }

        var endMinutes = currentMinutes;
        if (common.ReturnToOffice)
        {
            var backLeg = TravelTimeMatrixBuilder.Build(currentLat, currentLon, common.OfficeLatitude, common.OfficeLongitude, common);
            var buffer = departingFromVisit ? common.BufferMinutes : 0;
            endMinutes = currentMinutes + backLeg.TravelMinutes + buffer;
            totalKm += backLeg.DistanceKm;
            totalTravel += backLeg.TravelMinutes;
            stops.Add(new RouteStop(
                order,
                StopKind.Office,
                "OFFICE_END",
                "帰着（事業所）",
                null,
                FromMinutes(endMinutes),
                FromMinutes(endMinutes),
                0,
                backLeg.DistanceKm,
                backLeg.TravelMinutes,
                0,
                false));
        }

        var overtime = Math.Max(0, endMinutes - workEndMinutes);

        return new RoutePlan(
            stops,
            visitCount,
            Math.Round(totalKm, 2),
            totalTravel,
            totalService,
            totalWait,
            FromMinutes(departMinutes),
            FromMinutes(endMinutes),
            overtime,
            violationCount,
            remaining);
    }

    private static int EffectiveService(VisitTarget visit, CommonSettings common)
    {
        if (visit.ServiceMinutes.HasValue && visit.ServiceMinutes.Value > 0)
        {
            return visit.ServiceMinutes.Value;
        }

        return visit.Category == VisitCategory.Business
            ? common.BusinessServiceMinutes
            : common.GeneralServiceMinutes;
    }

    // 緯度経度を約250m四方のグリッドセルに丸め、近接訪問を同一「地域」として扱うキーにする。
    private static (long Row, long Col) AreaKey(double latitude, double longitude) =>
        ((long)Math.Round(latitude / AreaCellSizeDegrees), (long)Math.Round(longitude / AreaCellSizeDegrees));

    private static double PriorityPenalty(VisitPriority priority) => priority switch
    {
        VisitPriority.High => -20.0,
        VisitPriority.Low => 10.0,
        _ => 0.0
    };

    private static int ToMinutes(TimeOnly time) => (int)time.ToTimeSpan().TotalMinutes;

    private static TimeOnly FromMinutes(int minutes)
    {
        var clamped = Math.Clamp(minutes, 0, (24 * 60) - 1);
        return TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(clamped));
    }
}
