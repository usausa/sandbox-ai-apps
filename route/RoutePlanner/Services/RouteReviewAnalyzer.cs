namespace RoutePlanner.Services;

using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using RoutePlanner.Models;

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

    public RouteReviewAnalyzer(
        ILogger<RouteReviewAnalyzer> log,
        IChatClient chatClient)
    {
        this.log = log;
        this.chatClient = chatClient;

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

        var payload = BuildPayload(plan, common);
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

    private static object BuildPayload(RoutePlan plan, CommonSettings common)
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
                stop.WindowViolation
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
