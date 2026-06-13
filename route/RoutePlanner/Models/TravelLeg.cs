namespace RoutePlanner.Models;

// 2地点間の移動区間。距離(km)と推定移動時間(分)を持つ。
public sealed record TravelLeg(
    double DistanceKm,
    int TravelMinutes);
