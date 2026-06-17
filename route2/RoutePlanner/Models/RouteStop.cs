namespace RoutePlanner.Models;

// 足順上の1立ち寄り（出発拠点・訪問先・昼休憩）。到着/出発の予定時刻を持つ。
public sealed record RouteStop(
    int Order,
    StopKind Kind,
    string StopId,
    string Label,
    VisitTarget? Visit,
    TimeOnly Arrival,
    TimeOnly Departure,
    int ServiceMinutes,
    double TravelKmFromPrev,
    int TravelMinutesFromPrev,
    int WaitMinutes,
    bool WindowViolation);
