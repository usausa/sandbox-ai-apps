namespace RoutePlanner.Services;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using RoutePlanner.Models;
using RoutePlanner.Settings;

// 訪問先一覧と共通設定を Foundry(LLM) に渡し、足順(順序・各stopの時刻/距離/移動時間/違反)とレビューを
// まとめて生成させる。route と異なり最適化アルゴリズムによる前処理は行わず、順序も数値も全て LLM が決める。
// stop_id から元の VisitTarget を引き当てる部分だけはコード側で行い、地図・画面表示に必要な属性を確定させる。
public sealed class RoutePlanGenerator
{
    private static readonly JsonSerializerOptions PromptJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<RoutePlanGenerator> log;
    private readonly string systemPrompt;
    private readonly IChatClient chatClient;
    private readonly FoundrySettings foundrySettings;

    public RoutePlanGenerator(
        ILogger<RoutePlanGenerator> log,
        IChatClient chatClient,
        FoundrySettings foundrySettings)
    {
        this.log = log;
        this.chatClient = chatClient;
        this.foundrySettings = foundrySettings;

        var promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "route_planner.txt");
        systemPrompt = File.ReadAllText(promptPath).Trim();
    }

    public async Task<(RoutePlan Plan, RouteReviewResult Review, TokenUsageResult Usage)> GenerateAsync(
        IReadOnlyList<VisitTarget> visits,
        CommonSettings common,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visits);
        ArgumentNullException.ThrowIfNull(common);

        var payload = BuildPayload(visits, common);
        var userPrompt = BuildUserPrompt(payload);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt)
        };

        var options = new ChatOptions
        {
            Temperature = 0,
            ResponseFormat = ChatResponseFormat.Json
        };

        var response = await chatClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        var text = response.Text;
        log.DebugRouteLlmResponse(text);

        var parsed = Parse(text);
        var plan = BuildPlan(parsed?.Plan, visits, common);
        var review = Normalize(parsed?.Review ?? new RouteReviewResult());

        var usage = BuildUsage(response.Usage);
        log.InfoTokenUsage(usage.InputTokens, usage.OutputTokens, usage.TotalTokens);

        return (plan, review, usage);
    }

    // Foundry が返したトークン使用量を取り出し、USD単価×ドル円レートから概算費用(円)を算出する。
    // 単価がどちらも未設定(0)、またはドル円レートが未設定(0以下)なら費用は算出しない。
    private TokenUsageResult BuildUsage(UsageDetails? usage)
    {
        var input = usage?.InputTokenCount ?? 0;
        var output = usage?.OutputTokenCount ?? 0;
        var total = usage?.TotalTokenCount ?? (input + output);

        decimal? costJpy = null;
        if (foundrySettings.UsdJpyRate > 0 &&
            (foundrySettings.InputPricePer1M > 0 || foundrySettings.OutputPricePer1M > 0))
        {
            var usd = ((input * foundrySettings.InputPricePer1M) + (output * foundrySettings.OutputPricePer1M)) / 1_000_000m;
            costJpy = usd * foundrySettings.UsdJpyRate;
        }

        return new TokenUsageResult(input, output, total, costJpy);
    }

    private static object BuildPayload(IReadOnlyList<VisitTarget> visits, CommonSettings common)
    {
        return new
        {
            workDate = common.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            inspectorId = common.InspectorId,
            office = new
            {
                address = common.OfficeAddress,
                latitude = common.OfficeLatitude,
                longitude = common.OfficeLongitude
            },
            constraints = new
            {
                workStart = common.WorkStart.ToString("HH:mm", CultureInfo.InvariantCulture),
                workEnd = common.WorkEnd.ToString("HH:mm", CultureInfo.InvariantCulture),
                lunchStart = common.LunchStart.ToString("HH:mm", CultureInfo.InvariantCulture),
                lunchEnd = common.LunchEnd.ToString("HH:mm", CultureInfo.InvariantCulture),
                lunchMinutes = common.LunchMinutes,
                returnToOffice = common.ReturnToOffice,
                travelMode = common.TravelMode.ToString(),
                averageSpeedKmh = common.AverageSpeedKmh,
                roadDetourFactor = common.RoadDetourFactor,
                weather = common.Weather.ToString(),
                weatherDurationFactor = common.WeatherDurationFactor,
                generalServiceMinutes = common.GeneralServiceMinutes,
                businessServiceMinutes = common.BusinessServiceMinutes,
                bufferMinutes = common.BufferMinutes,
                allowOvertime = common.AllowOvertime,
                maxOvertimeMinutes = common.MaxOvertimeMinutes,
                defaultWindowStrictness = common.DefaultWindowStrictness.ToString(),
                objective = common.Objective.ToString()
            },
            visits = visits.Select(visit => new
            {
                visit.VisitId,
                visit.CustomerName,
                visit.Address,
                visit.Latitude,
                visit.Longitude,
                category = visit.Category.ToString(),
                serviceMinutes = visit.ServiceMinutes,
                windowStart = visit.WindowStart?.ToString("HH:mm", CultureInfo.InvariantCulture),
                windowEnd = visit.WindowEnd?.ToString("HH:mm", CultureInfo.InvariantCulture),
                windowStrict = visit.WindowStrict.ToString(),
                priority = visit.Priority.ToString(),
                appointmentRequired = visit.AppointmentRequired,
                workType = visit.WorkType,
                buildingGroupId = visit.BuildingGroupId,
                accessNote = visit.AccessNote,
                hazardNote = visit.HazardNote
            })
        };
    }

    private static string BuildUserPrompt(object payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload, PromptJsonOptions);

        return
            """
            以下は電気設備点検の調査員1名・1日分の訪問先一覧(visits)と共通設定(office/constraints)です。
            出発拠点から全 visits を効率的な順序で巡回し、昼休憩を挟んで拠点へ帰着する1日の足順を生成してください。
            移動距離・移動時間・到着/出発時刻・待機・時間帯違反・残業・未割当まで、全ての数値をあなたが算出してください。

            JSONのみで回答してください。

            入力データ:
            """
            + Environment.NewLine
            + payloadJson;
    }

    private static LlmRouteResponse? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LlmRouteResponse>(json, ResponseJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // LLM が返した足順を、画面・地図が扱える RoutePlan に変換する。
    // stop_id から元の VisitTarget を引き当てて座標などの属性を確定させ、件数・違反数は stops から数え直す。
    private static RoutePlan BuildPlan(LlmRoutePlan? llmPlan, IReadOnlyList<VisitTarget> visits, CommonSettings common)
    {
        var visitMap = new Dictionary<string, VisitTarget>(StringComparer.Ordinal);
        foreach (var visit in visits)
        {
            visitMap[visit.VisitId] = visit;
        }

        var stops = new List<RouteStop>();
        var order = 1;
        var totalKm = 0.0;
        var totalTravel = 0;
        var totalService = 0;
        var totalWait = 0;
        var violationCount = 0;
        var visitCount = 0;

        foreach (var item in llmPlan?.Stops ?? [])
        {
            var kind = ParseStopKind(item.Kind);

            VisitTarget? visit = null;
            if (kind == StopKind.Visit && !string.IsNullOrWhiteSpace(item.StopId))
            {
                visitMap.TryGetValue(item.StopId.Trim(), out visit);
            }

            var label = ResolveLabel(kind, order, visit, item.StopId);
            var stopId = string.IsNullOrWhiteSpace(item.StopId) ? kind.ToString().ToUpperInvariant() : item.StopId.Trim();

            var arrival = ParseTime(item.Arrival, common.EffectiveDepartTime);
            var departure = ParseTime(item.Departure, arrival);
            var travelKm = item.TravelKmFromPrev > 0 ? Math.Round(item.TravelKmFromPrev, 2) : 0.0;
            var travelMin = Math.Max(0, item.TravelMinutesFromPrev);
            var serviceMin = Math.Max(0, item.ServiceMinutes);
            var waitMin = Math.Max(0, item.WaitMinutes);

            stops.Add(new RouteStop(
                order,
                kind,
                stopId,
                label,
                visit,
                arrival,
                departure,
                serviceMin,
                travelKm,
                travelMin,
                waitMin,
                item.WindowViolation));

            order++;
            totalKm += travelKm;
            totalTravel += travelMin;
            totalService += serviceMin;
            totalWait += waitMin;
            if (kind == StopKind.Visit)
            {
                visitCount++;
            }

            if (item.WindowViolation)
            {
                violationCount++;
            }
        }

        var startTime = stops.Count > 0 ? stops[0].Arrival : common.EffectiveDepartTime;
        var endTime = stops.Count > 0 ? stops[^1].Departure : common.EffectiveDepartTime;
        var overtime = Math.Max(0, llmPlan?.OvertimeMinutes ?? 0);

        var unassigned = new List<VisitTarget>();
        foreach (var id in llmPlan?.UnassignedVisitIds ?? [])
        {
            if (!string.IsNullOrWhiteSpace(id) && visitMap.TryGetValue(id.Trim(), out var visit))
            {
                unassigned.Add(visit);
            }
        }

        // 集計の合計系は LLM の plan サマリ値を優先し、未提供(0以下)なら stops の合計で補完する。
        var totalDistanceKm = llmPlan is { TotalDistanceKm: > 0 } ? Math.Round(llmPlan.TotalDistanceKm, 2) : Math.Round(totalKm, 2);
        var totalTravelMinutes = llmPlan is { TotalTravelMinutes: > 0 } ? llmPlan.TotalTravelMinutes : totalTravel;
        var totalServiceMinutes = llmPlan is { TotalServiceMinutes: > 0 } ? llmPlan.TotalServiceMinutes : totalService;
        var totalWaitMinutes = llmPlan is { TotalWaitMinutes: > 0 } ? llmPlan.TotalWaitMinutes : totalWait;

        return new RoutePlan(
            stops,
            visitCount,
            totalDistanceKm,
            totalTravelMinutes,
            totalServiceMinutes,
            totalWaitMinutes,
            startTime,
            endTime,
            overtime,
            violationCount,
            unassigned);
    }

    private static string ResolveLabel(StopKind kind, int order, VisitTarget? visit, string? stopId)
    {
        if (kind == StopKind.Visit)
        {
            if (visit is not null)
            {
                return string.IsNullOrWhiteSpace(visit.CustomerName) ? visit.VisitId : visit.CustomerName;
            }

            return string.IsNullOrWhiteSpace(stopId) ? "訪問" : stopId.Trim();
        }

        if (kind == StopKind.Lunch)
        {
            return "昼休憩";
        }

        return order == 1 ? "出発（事業所）" : "帰着（事業所）";
    }

    private static StopKind ParseStopKind(string? kind) => kind?.Trim().ToUpperInvariant() switch
    {
        "OFFICE" => StopKind.Office,
        "LUNCH" => StopKind.Lunch,
        _ => StopKind.Visit
    };

    private static TimeOnly ParseTime(string? value, TimeOnly fallback) =>
        !string.IsNullOrWhiteSpace(value) && TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static RouteReviewResult Normalize(RouteReviewResult result)
    {
        return new RouteReviewResult
        {
            FeasibilityScore = Math.Clamp(result.FeasibilityScore, 0, 100),
            Summary = string.IsNullOrWhiteSpace(result.Summary) ? "要約なし" : result.Summary.Trim(),
            RecommendedAction = string.IsNullOrWhiteSpace(result.RecommendedAction)
                ? "高重大度の違反から順に確認してください。"
                : result.RecommendedAction.Trim(),
            RuleViolations = result.RuleViolations
                .Where(x => !string.IsNullOrWhiteSpace(x.Detail))
                .ToArray(),
            ImprovementSuggestions = result.ImprovementSuggestions
                .Where(x => !string.IsNullOrWhiteSpace(x.Reason) || !string.IsNullOrWhiteSpace(x.Action))
                .ToArray(),
            RiskNotes = result.RiskNotes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray()
        };
    }

    // LLM 応答(足順 plan + レビュー review)のデシリアライズ用 DTO。
    public sealed class LlmRouteResponse
    {
        [JsonPropertyName("plan")]
        public LlmRoutePlan? Plan { get; init; }

        [JsonPropertyName("review")]
        public RouteReviewResult? Review { get; init; }
    }

    public sealed class LlmRoutePlan
    {
        [JsonPropertyName("stops")]
        public IReadOnlyList<LlmRouteStop> Stops { get; init; } = [];

        [JsonPropertyName("total_distance_km")]
        public double TotalDistanceKm { get; init; }

        [JsonPropertyName("total_travel_minutes")]
        public int TotalTravelMinutes { get; init; }

        [JsonPropertyName("total_service_minutes")]
        public int TotalServiceMinutes { get; init; }

        [JsonPropertyName("total_wait_minutes")]
        public int TotalWaitMinutes { get; init; }

        [JsonPropertyName("overtime_minutes")]
        public int OvertimeMinutes { get; init; }

        [JsonPropertyName("unassigned_visit_ids")]
        public IReadOnlyList<string> UnassignedVisitIds { get; init; } = [];
    }

    public sealed class LlmRouteStop
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; init; }

        [JsonPropertyName("stop_id")]
        public string? StopId { get; init; }

        [JsonPropertyName("arrival")]
        public string? Arrival { get; init; }

        [JsonPropertyName("departure")]
        public string? Departure { get; init; }

        [JsonPropertyName("service_minutes")]
        public int ServiceMinutes { get; init; }

        [JsonPropertyName("travel_km_from_prev")]
        public double TravelKmFromPrev { get; init; }

        [JsonPropertyName("travel_minutes_from_prev")]
        public int TravelMinutesFromPrev { get; init; }

        [JsonPropertyName("wait_minutes")]
        public int WaitMinutes { get; init; }

        [JsonPropertyName("window_violation")]
        public bool WindowViolation { get; init; }
    }
}
