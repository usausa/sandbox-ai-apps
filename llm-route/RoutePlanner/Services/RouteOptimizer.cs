namespace RoutePlanner.Services;

using RoutePlanner.Models;

// 時間帯指定を考慮した最近傍法ベースの足順生成 + 局所探索(2-opt/Or-opt)による交差解消。
// 1. 出発拠点から、最も早く着手できる訪問先を順に選ぶ貪欲法で初期順序を作る（昼休憩を挿入し拠点へ帰着）。
//    地域結束: 時間帯指定の無い訪問は、現在いる地域(約250m四方のセル)の未訪問が残る限りそこを優先的に
//    片付け、「隣接地域を2回に分けて回る」分割訪問を抑制する。ただし時間帯指定が間近に迫る訪問が
//    あるときは結束を一時停止して枠を優先する（空間的まとまりより時間帯遵守を上位に置く）。
// 2. 貪欲法は近視眼的で、A・B・Cが一直線に並ぶとき A→C→B のように「行き来(交差)」が残ることがある。
//    そこで初期順序に 2-opt(区間反転) と Or-opt(1件の移動) を掛け、時間枠の実行可能性を保ちつつ
//    総移動を減らす並べ替えだけを採用して交差を解消する。
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

    // 局所探索の最大反復回数。改善が無くなれば早期終了するため通常はこれ未満で収束する。
    private const int MaxImprovementPasses = 20;

    public RoutePlan Optimize(IReadOnlyList<VisitTarget> visits, CommonSettings common)
    {
        ArgumentNullException.ThrowIfNull(visits);
        ArgumentNullException.ThrowIfNull(common);

        // 1. 貪欲法で初期順序と未割当を決める。
        var (order, unassigned) = BuildInitialOrder(visits, common);

        // 2. 2-opt / Or-opt で交差(行き来)を解消する。実行可能性を保つ並べ替えのみ採用。
        var improved = ImproveOrder(order, common);

        // 3. 同一建物・同一時間帯の住戸は名称順(部屋番号順)に整える。座標・時間帯が同一のため所要に影響しない。
        var normalized = NormalizeBuildingRuns(improved);

        var schedule = SimulateOrder(normalized, common);
        if (schedule is null)
        {
            normalized = improved;
            schedule = SimulateOrder(normalized, common)!;
        }

        return AssemblePlan(schedule, unassigned, common);
    }

    // 貪欲(最近傍)法で訪問順序を決める。スコア最小の候補を順に選び、選べなくなったら残りを未割当として返す。
    private static (List<VisitTarget> Order, List<VisitTarget> Unassigned) BuildInitialOrder(
        IReadOnlyList<VisitTarget> visits,
        CommonSettings common)
    {
        var remaining = visits.ToList();
        var order = new List<VisitTarget>();

        var departMinutes = ToMinutes(common.EffectiveDepartTime);
        var workEndMinutes = ToMinutes(common.WorkEnd);
        var lunchStartMinutes = ToMinutes(common.LunchStart);
        var endLimit = workEndMinutes + (common.AllowOvertime ? Math.Max(0, common.MaxOvertimeMinutes) : 0);
        var maxVisits = common.MaxVisits ?? int.MaxValue;

        var currentMinutes = departMinutes;
        var currentLat = common.OfficeLatitude;
        var currentLon = common.OfficeLongitude;
        var departingFromVisit = false;
        string? currentBuildingGroupId = null;
        (long Row, long Col)? currentArea = null;
        var lunchTaken = common.LunchMinutes <= 0;
        var visitCount = 0;

        while (remaining.Count > 0 && visitCount < maxVisits)
        {
            if (!lunchTaken && currentMinutes >= lunchStartMinutes)
            {
                currentMinutes += common.LunchMinutes;
                lunchTaken = true;
                continue;
            }

            VisitTarget? best = null;
            var bestStart = 0;
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

                if (startService > windowEnd && candidate.WindowStrict == TimeWindowStrictness.Strict)
                {
                    continue;
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
                    bestStart = startService;
                }
            }

            if (best is null)
            {
                break;
            }

            // 同一建物・同一時間帯の住戸は名称順（部屋番号順）に回す。
            // どの建物へ向かうかはスコアで決め、その建物・同一時間帯の中では最小名称の住戸へ寄せる。
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

            currentMinutes = bestStart + EffectiveService(best, common);
            currentLat = best.Latitude;
            currentLon = best.Longitude;
            currentBuildingGroupId = best.BuildingGroupId;
            currentArea = AreaKey(best.Latitude, best.Longitude);
            departingFromVisit = true;
            visitCount++;
            order.Add(best);
            remaining.Remove(best);
        }

        return (order, remaining);
    }

    // 与えられた訪問順序を固定したまま昼休憩・待機・移動を再現してスケジュールを組む。
    // Strict 枠に間に合わない／勤務終了(＋許容残業)を超える順序は実行不可として null を返す。
    private static OrderSchedule? SimulateOrder(List<VisitTarget> order, CommonSettings common)
    {
        var departMinutes = ToMinutes(common.EffectiveDepartTime);
        var workEndMinutes = ToMinutes(common.WorkEnd);
        var lunchStartMinutes = ToMinutes(common.LunchStart);
        var endLimit = workEndMinutes + (common.AllowOvertime ? Math.Max(0, common.MaxOvertimeMinutes) : 0);

        var stops = new List<RouteStop>();
        var stopOrder = 1;
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

        stops.Add(new RouteStop(
            stopOrder++,
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

        foreach (var visit in order)
        {
            if (!lunchTaken && currentMinutes >= lunchStartMinutes)
            {
                var lunchEndMinutes = currentMinutes + common.LunchMinutes;
                stops.Add(new RouteStop(
                    stopOrder++,
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
            }

            var leg = TravelTimeMatrixBuilder.Build(currentLat, currentLon, visit.Latitude, visit.Longitude, common);

            var sameBuilding = !string.IsNullOrEmpty(visit.BuildingGroupId)
                && string.Equals(visit.BuildingGroupId, currentBuildingGroupId, StringComparison.Ordinal);
            var buffer = departingFromVisit && !sameBuilding ? common.BufferMinutes : 0;
            var arrive = currentMinutes + leg.TravelMinutes + buffer;

            int? windowStart = visit.WindowStart.HasValue ? ToMinutes(visit.WindowStart.Value) : null;
            int? windowEnd = visit.WindowEnd.HasValue ? ToMinutes(visit.WindowEnd.Value) : null;

            var startService = windowStart > arrive ? windowStart.Value : arrive;
            var wait = startService - arrive;

            var violation = false;
            if (startService > windowEnd)
            {
                if (visit.WindowStrict == TimeWindowStrictness.Strict)
                {
                    return null;
                }

                violation = true;
            }

            var service = EffectiveService(visit, common);
            var finish = startService + service;
            if (finish > endLimit)
            {
                return null;
            }

            stops.Add(new RouteStop(
                stopOrder++,
                StopKind.Visit,
                visit.VisitId,
                string.IsNullOrWhiteSpace(visit.CustomerName) ? visit.VisitId : visit.CustomerName,
                visit,
                FromMinutes(arrive),
                FromMinutes(finish),
                service,
                leg.DistanceKm,
                leg.TravelMinutes,
                wait,
                violation));

            totalKm += leg.DistanceKm;
            totalTravel += leg.TravelMinutes;
            totalService += service;
            totalWait += wait;
            if (violation)
            {
                violationCount++;
            }

            currentMinutes = finish;
            currentLat = visit.Latitude;
            currentLon = visit.Longitude;
            currentBuildingGroupId = visit.BuildingGroupId;
            departingFromVisit = true;
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
                stopOrder,
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

        return new OrderSchedule(
            stops,
            order.Count,
            totalKm,
            totalTravel,
            totalService,
            totalWait,
            endMinutes,
            overtime,
            violationCount);
    }

    // 2-opt(区間反転)と Or-opt(1件の移動)で初期順序を改善する。
    // 実行可能(SimulateOrder が非null)かつ「枠違反→残業→移動時間→実距離」の優先順で良化する並べ替えのみ採用する。
    private static List<VisitTarget> ImproveOrder(List<VisitTarget> initial, CommonSettings common)
    {
        var best = new List<VisitTarget>(initial);
        if (best.Count < 3)
        {
            return best;
        }

        var bestSchedule = SimulateOrder(best, common);
        if (bestSchedule is null)
        {
            return best;
        }

        var improved = true;
        var passes = 0;
        while (improved && passes++ < MaxImprovementPasses)
        {
            improved = false;

            // 2-opt: 区間 [i..j] を反転し、交差を解消する。
            for (var i = 0; i < best.Count - 1; i++)
            {
                for (var j = i + 1; j < best.Count; j++)
                {
                    var candidate = TwoOptSwap(best, i, j);
                    var schedule = SimulateOrder(candidate, common);
                    if (schedule is not null && IsBetter(schedule, bestSchedule))
                    {
                        best = candidate;
                        bestSchedule = schedule;
                        improved = true;
                    }
                }
            }

            // Or-opt: 1件を別の位置へ移動し、はみ出した訪問の挿し直しで距離を縮める。
            for (var i = 0; i < best.Count; i++)
            {
                for (var insertBefore = 0; insertBefore <= best.Count; insertBefore++)
                {
                    if (insertBefore == i || insertBefore == i + 1)
                    {
                        continue;
                    }

                    var candidate = Relocate(best, i, insertBefore);
                    var schedule = SimulateOrder(candidate, common);
                    if (schedule is not null && IsBetter(schedule, bestSchedule))
                    {
                        best = candidate;
                        bestSchedule = schedule;
                        improved = true;
                    }
                }
            }
        }

        return best;
    }

    // 枠違反 → 残業 → 移動時間 → 実距離(km) の優先順で厳密に良くなっていれば true。
    // 移動時間(分)は切り上げで粗く同点になりやすいため、最後に実距離で交差解消を見分ける。
    private static bool IsBetter(OrderSchedule candidate, OrderSchedule current)
    {
        if (candidate.ViolationCount != current.ViolationCount)
        {
            return candidate.ViolationCount < current.ViolationCount;
        }

        if (candidate.Overtime != current.Overtime)
        {
            return candidate.Overtime < current.Overtime;
        }

        if (candidate.TotalTravel != current.TotalTravel)
        {
            return candidate.TotalTravel < current.TotalTravel;
        }

        return candidate.TotalKm < current.TotalKm - 1e-9;
    }

    private static List<VisitTarget> TwoOptSwap(List<VisitTarget> source, int i, int j)
    {
        var result = new List<VisitTarget>(source);
        result.Reverse(i, j - i + 1);
        return result;
    }

    private static List<VisitTarget> Relocate(List<VisitTarget> source, int from, int insertBefore)
    {
        var item = source[from];
        var result = new List<VisitTarget>(source.Count);
        for (var k = 0; k <= source.Count; k++)
        {
            if (k == insertBefore)
            {
                result.Add(item);
            }

            if (k < source.Count && k != from)
            {
                result.Add(source[k]);
            }
        }

        return result;
    }

    // 同一建物・同一時間帯の連続する住戸を名称順(部屋番号順)に整える。
    // 座標・時間帯が同一のため、この並べ替えは移動/到着/着手/違反に影響しない。
    private static List<VisitTarget> NormalizeBuildingRuns(List<VisitTarget> order)
    {
        var result = new List<VisitTarget>(order.Count);
        var i = 0;
        while (i < order.Count)
        {
            var current = order[i];
            if (string.IsNullOrEmpty(current.BuildingGroupId))
            {
                result.Add(current);
                i++;
                continue;
            }

            var j = i + 1;
            while (j < order.Count
                && string.Equals(order[j].BuildingGroupId, current.BuildingGroupId, StringComparison.Ordinal)
                && Nullable.Equals(order[j].WindowStart, current.WindowStart)
                && Nullable.Equals(order[j].WindowEnd, current.WindowEnd))
            {
                j++;
            }

            if (j - i > 1)
            {
                result.AddRange(order.Skip(i).Take(j - i).OrderBy(v => v.CustomerName, StringComparer.Ordinal));
            }
            else
            {
                result.Add(current);
            }

            i = j;
        }

        return result;
    }

    private static RoutePlan AssemblePlan(OrderSchedule schedule, IReadOnlyList<VisitTarget> unassigned, CommonSettings common)
    {
        var departMinutes = ToMinutes(common.EffectiveDepartTime);

        return new RoutePlan(
            schedule.Stops,
            schedule.VisitCount,
            Math.Round(schedule.TotalKm, 2),
            schedule.TotalTravel,
            schedule.TotalService,
            schedule.TotalWait,
            FromMinutes(departMinutes),
            FromMinutes(schedule.EndMinutes),
            schedule.Overtime,
            schedule.ViolationCount,
            unassigned);
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

    // 訪問順序を固定して組んだスケジュールの集計結果。
    private sealed record OrderSchedule(
        IReadOnlyList<RouteStop> Stops,
        int VisitCount,
        double TotalKm,
        int TotalTravel,
        int TotalService,
        int TotalWait,
        int EndMinutes,
        int Overtime,
        int ViolationCount);
}
