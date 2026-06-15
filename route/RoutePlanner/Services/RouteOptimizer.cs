namespace RoutePlanner.Services;

using RoutePlanner.Models;

// 時間帯指定を考慮した最近傍法ベースの足順生成。
// 出発拠点から、最も早く着手できる訪問先を順に選び、昼休憩を挿入し、拠点へ帰着する。
// TODO: 2-opt / Or-opt の局所探索で移動時間をさらに短縮する（時間枠の実行可能性を保ちつつ入れ替える）。
public sealed class RouteOptimizer
{
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
                    + PriorityPenalty(candidate.Priority)
                    + (windowEnd.HasValue ? 0 : 5);

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
