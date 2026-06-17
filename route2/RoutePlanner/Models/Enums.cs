namespace RoutePlanner.Models;

// 調査先の種別。既定の平均調査時間や優先度の基準に用いる。
public enum VisitCategory
{
    General,
    Business
}

// 移動手段。平均速度・経路係数の既定値に影響する。
public enum TravelMode
{
    Car,
    Walk,
    Bicycle,
    PublicTransit
}

// その日の天気。移動・調査時間の延長係数や安全上の注意に用いる。
public enum WeatherKind
{
    Sunny,
    Cloudy,
    Rain,
    Snow,
    Heat
}

// 指定時間帯の厳守区分。Strict はハード制約、Preferred はソフト制約。
public enum TimeWindowStrictness
{
    Preferred,
    Strict
}

// 訪問先の優先度。当日必訪か繰延可かの判断に用いる。
public enum VisitPriority
{
    Low,
    Medium,
    High
}

// 最適化の目的関数の重み付け方針。
public enum OptimizeObjective
{
    MinimizeTravel,
    PrioritizeWindows
}

// 足順上の立ち寄り種別。
public enum StopKind
{
    Office,
    Visit,
    Lunch
}
