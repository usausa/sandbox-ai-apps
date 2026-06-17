namespace RoutePlanner.Models;

// CSV 1行 = 1訪問先。その日の調査先一覧の各要素を表す。
public sealed record VisitTarget(
    string VisitId,
    string CustomerName,
    string Address,
    double Latitude,
    double Longitude,
    VisitCategory Category,
    int? ServiceMinutes,
    TimeOnly? WindowStart,
    TimeOnly? WindowEnd,
    TimeWindowStrictness WindowStrict,
    VisitPriority Priority,
    bool AppointmentRequired,
    string? WorkType,
    string? BuildingGroupId,
    string? AccessNote,
    string? HazardNote,
    string? ContactPhone);
