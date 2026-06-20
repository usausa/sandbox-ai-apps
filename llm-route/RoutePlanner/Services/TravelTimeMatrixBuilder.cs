namespace RoutePlanner.Services;

using RoutePlanner.Models;

// 2地点間の移動距離・移動時間を推定する静的ユーティリティ。
// 地図APIに依存しないサンプルのため、Haversine直線距離に道路迂回係数を掛けて近似する。
// 実運用で実地図の経路/所要API に差し替える場合は、この Build を置き換える。
public static class TravelTimeMatrixBuilder
{
    private const double EarthRadiusKm = 6371.0;

    // 2地点間の直線距離(km)。
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
            + (Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    // 移動区間を生成する。距離 = 直線距離 × 道路迂回係数、時間 = 距離 ÷ 平均速度 × 天候係数。
    public static TravelLeg Build(
        double fromLat,
        double fromLon,
        double toLat,
        double toLon,
        CommonSettings common)
    {
        ArgumentNullException.ThrowIfNull(common);

        var straightKm = DistanceKm(fromLat, fromLon, toLat, toLon);
        var roadKm = straightKm * common.RoadDetourFactor;
        var speed = common.AverageSpeedKmh <= 0 ? 1.0 : common.AverageSpeedKmh;
        var minutes = roadKm / speed * 60.0 * common.WeatherDurationFactor;

        return new TravelLeg(Math.Round(roadKm, 2), (int)Math.Ceiling(minutes));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
