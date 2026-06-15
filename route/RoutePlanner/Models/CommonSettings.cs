namespace RoutePlanner.Models;

// 1日・調査員1名単位の共通インプット。訪問先一覧と組み合わせて足順を算出する。
public sealed class CommonSettings
{
    public DateOnly WorkDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public string InspectorId { get; set; } = "INS-01";

    public string OfficeAddress { get; set; } = "山梨県甲府市丸の内1-18-1（甲府市役所）";

    public double OfficeLatitude { get; set; } = 35.6618;

    public double OfficeLongitude { get; set; } = 138.5683;

    public bool ReturnToOffice { get; set; } = true;

    public TimeOnly WorkStart { get; set; } = new(9, 0);

    public TimeOnly WorkEnd { get; set; } = new(18, 0);

    public TimeOnly? DepartTime { get; set; }

    public TimeOnly LunchStart { get; set; } = new(12, 0);

    public TimeOnly LunchEnd { get; set; } = new(13, 0);

    public int LunchMinutes { get; set; } = 60;

    public TravelMode TravelMode { get; set; } = TravelMode.Car;

    public double AverageSpeedKmh { get; set; } = 25;

    public double RoadDetourFactor { get; set; } = 1.3;

    public WeatherKind Weather { get; set; } = WeatherKind.Sunny;

    public double WeatherDurationFactor { get; set; } = 1.0;

    public int GeneralServiceMinutes { get; set; } = 20;

    public int BusinessServiceMinutes { get; set; } = 40;

    public int BufferMinutes { get; set; } = 2;

    public int? MaxVisits { get; set; }

    public bool AllowOvertime { get; set; }

    public int MaxOvertimeMinutes { get; set; }

    public TimeWindowStrictness DefaultWindowStrictness { get; set; } = TimeWindowStrictness.Preferred;

    public OptimizeObjective Objective { get; set; } = OptimizeObjective.MinimizeTravel;

    // 実際の拠点出発時刻。未指定なら勤務開始時刻とみなす。
    public TimeOnly EffectiveDepartTime => DepartTime ?? WorkStart;
}
