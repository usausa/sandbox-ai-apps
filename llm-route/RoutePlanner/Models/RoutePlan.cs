namespace RoutePlanner.Models;

// アルゴリズムが算出した1日分の足順と、その集計指標。
public sealed record RoutePlan(
    IReadOnlyList<RouteStop> Stops,
    int VisitCount,
    double TotalDistanceKm,
    int TotalTravelMinutes,
    int TotalServiceMinutes,
    int TotalWaitMinutes,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int OvertimeMinutes,
    int WindowViolationCount,
    IReadOnlyList<VisitTarget> UnassignedVisits);
