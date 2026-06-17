namespace RoutePlanner.Services;

using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using RoutePlanner.Models;
using RoutePlanner.Settings;

// 算出済みの足順を Foundry(LLM) に渡し、ルール検証と改善提案を JSON で受け取る。
public sealed class RouteReviewAnalyzer
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

    private readonly ILogger<RouteReviewAnalyzer> log;
    private readonly string systemPrompt;
    private readonly IChatClient chatClient;
    private readonly RoutePlannerSettings settings;

    public RouteReviewAnalyzer(
        ILogger<RouteReviewAnalyzer> log,
        IChatClient chatClient,
        RoutePlannerSettings settings)
    {
        this.log = log;
        this.chatClient = chatClient;
        this.settings = settings;

        var promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "route_reviewer.txt");
        systemPrompt = File.ReadAllText(promptPath).Trim();
    }

    public async Task<RouteReviewResult> AnalyzeAsync(
        RoutePlan plan,
        CommonSettings common,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(common);

        var payload = BuildPayload(plan, common, Math.Max(0, settings.ReviewLookaheadCount));
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

        return Normalize(Parse(text));
    }

    private static object BuildPayload(RoutePlan plan, CommonSettings common, int lookaheadCount)
    {
        // 各訪問先から「足順順で次の数件先」までの距離を前計算する。
        // LLM が座標から距離を推測せず、行き来(交差)を数値で判定できるようにするための補助情報。
        var nextDistances = BuildNextDistances(plan, common, lookaheadCount);

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
                travelMode = common.TravelMode.ToString(),
                averageSpeedKmh = common.AverageSpeedKmh,
                weather = common.Weather.ToString(),
                allowOvertime = common.AllowOvertime,
                maxOvertimeMinutes = common.MaxOvertimeMinutes,
                objective = common.Objective.ToString()
            },
            planSummary = new
            {
                plan.VisitCount,
                plan.TotalDistanceKm,
                plan.TotalTravelMinutes,
                plan.TotalServiceMinutes,
                plan.TotalWaitMinutes,
                startTime = plan.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                endTime = plan.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                plan.OvertimeMinutes,
                plan.WindowViolationCount,
                unassignedCount = plan.UnassignedVisits.Count
            },
            stops = plan.Stops.Select(stop => new
            {
                stop.Order,
                kind = stop.Kind.ToString(),
                stopId = stop.StopId,
                stop.Label,
                address = stop.Visit?.Address,
                latitude = stop.Visit?.Latitude,
                longitude = stop.Visit?.Longitude,
                buildingGroupId = stop.Visit?.BuildingGroupId,
                category = stop.Visit?.Category.ToString(),
                windowStart = stop.Visit?.WindowStart?.ToString("HH:mm", CultureInfo.InvariantCulture),
                windowEnd = stop.Visit?.WindowEnd?.ToString("HH:mm", CultureInfo.InvariantCulture),
                windowStrict = stop.Visit?.WindowStrict.ToString(),
                priority = stop.Visit?.Priority.ToString(),
                arrival = stop.Arrival.ToString("HH:mm", CultureInfo.InvariantCulture),
                departure = stop.Departure.ToString("HH:mm", CultureInfo.InvariantCulture),
                stop.ServiceMinutes,
                stop.TravelMinutesFromPrev,
                stop.WaitMinutes,
                stop.WindowViolation,
                nextDistances = nextDistances.GetValueOrDefault(stop.StopId)
            }),
            unassigned = plan.UnassignedVisits.Select(visit => new
            {
                visit.VisitId,
                category = visit.Category.ToString(),
                windowStart = visit.WindowStart?.ToString("HH:mm", CultureInfo.InvariantCulture),
                windowEnd = visit.WindowEnd?.ToString("HH:mm", CultureInfo.InvariantCulture),
                windowStrict = visit.WindowStrict.ToString(),
                priority = visit.Priority.ToString()
            })
        };
    }

    // 足順順に並ぶ訪問先について、各訪問先から次の lookaheadCount 件先までの距離(km)・移動時間(分)を前計算する。
    // 直前からの距離(travelMinutesFromPrev)だけでは並べ替えの良否を判断できないため、先読みの距離を補助情報として渡す。
    private static Dictionary<string, IReadOnlyList<object>> BuildNextDistances(RoutePlan plan, CommonSettings common, int lookaheadCount)
    {
        var result = new Dictionary<string, IReadOnlyList<object>>(StringComparer.Ordinal);
        if (lookaheadCount <= 0)
        {
            return result;
        }

        var visitStops = plan.Stops.Where(stop => stop is { Kind: StopKind.Visit, Visit: not null }).ToList();
        for (var i = 0; i < visitStops.Count; i++)
        {
            var from = visitStops[i].Visit!;
            var legs = new List<object>(lookaheadCount);
            for (var offset = 1; offset <= lookaheadCount && i + offset < visitStops.Count; offset++)
            {
                var toStop = visitStops[i + offset];
                var to = toStop.Visit!;
                var leg = TravelTimeMatrixBuilder.Build(from.Latitude, from.Longitude, to.Latitude, to.Longitude, common);
                legs.Add(new
                {
                    rank = offset,
                    stopId = toStop.StopId,
                    distanceKm = leg.DistanceKm,
                    travelMinutes = leg.TravelMinutes
                });
            }

            if (legs.Count > 0)
            {
                result[visitStops[i].StopId] = legs;
            }
        }

        return result;
    }

    private static string BuildUserPrompt(object payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload, PromptJsonOptions);

        return
            """
            以下は電気設備点検の調査員1名・1日分の「足順（訪問順）」案です。
            アルゴリズムが移動時間と時間帯指定を考慮して作成しました。
            この足順が業務ルールや常識に反していないかを検証し、必要なら具体的な改善を提案してください。
            特に次の点に注目してください:
            - Strict(厳守)の指定時間帯に間に合っているか（windowViolation=true は要注意）
            - 残業(overtimeMinutes)が発生していないか
            - 未割当(unassignedCount)の訪問先がないか
            - 昼休憩が確保されているか
            - 天候による移動余裕や安全上の注意
            - 移動効率: 時間帯指定（windowStart/windowEnd）が無いのに、いったん離れた地域へ移動した後で
              元の隣接地域へ戻る「地域の分割訪問」や「A→C→B のような行き来(交差)」が起きていないか。
              各訪問先には nextDistances（足順順で次の数件先までの距離distanceKm・移動時間travelMinutes）が
              付与されています。距離は座標から推測せず、必ずこの nextDistances の数値を使って判断してください。
              例えばある訪問先から「次の1件目より2件目の方が近い」場合は行き来の疑いがあり、入れ替えで
              総移動を減らせるなら reorder で並べ替えを提案してください（2-optの考え方）。
              ただし時間帯指定で離れざるを得ない移動は問題視しないでください。

            JSONのみで回答してください。

            入力データ:
            """
            + Environment.NewLine
            + payloadJson;
    }

    private static RouteReviewResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new RouteReviewResult
            {
                Summary = "LLMからの応答が空でした。",
                RecommendedAction = "設定と接続状態を確認して再実行してください。"
            };
        }

        try
        {
            return JsonSerializer.Deserialize<RouteReviewResult>(json, ResponseJsonOptions) ?? new RouteReviewResult
            {
                Summary = "LLM応答のデシリアライズに失敗しました。",
                RecommendedAction = "再実行するか、プロンプトを確認してください。"
            };
        }
        catch (JsonException)
        {
            return new RouteReviewResult
            {
                Summary = "LLM応答のJSON解析に失敗しました。",
                RecommendedAction = "再実行するか、プロンプトを確認してください。"
            };
        }
    }

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
}
