namespace InspectorChecker.Services;

using System.Text.Json;

using InspectorChecker.Models;
using InspectorChecker.Settings;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

public sealed class InspectionFraudAnalyzer
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

    private readonly ILogger<InspectionFraudAnalyzer> log;
    private readonly string systemPrompt;
    private readonly IChatClient chatClient;
    private readonly FoundrySettings foundrySettings;

    public InspectionFraudAnalyzer(
        ILogger<InspectionFraudAnalyzer> log,
        IChatClient chatClient,
        FoundrySettings foundrySettings)
    {
        this.log = log;
        this.chatClient = chatClient;
        this.foundrySettings = foundrySettings;

        var promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "inspection_analyzer.txt");
        systemPrompt = File.ReadAllText(promptPath).Trim();
    }

    public async Task<(InspectionAnalysisResult Analysis, TokenUsageResult Usage)> AnalyzeAsync(
        InspectionFeatureSummary featureSummary,
        IReadOnlyList<SurveyRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(featureSummary);
        ArgumentNullException.ThrowIfNull(records);

        var payload = new
        {
            datasetSummary = new
            {
                featureSummary.RecordCount,
                featureSummary.CustomerCount,
                featureSummary.StartDate,
                featureSummary.EndDate,
                featureSummary.MeanCurrent,
                featureSummary.StandardDeviation,
                featureSummary.NearDefaultRatio
            },
            valueDistribution = featureSummary.ValueDistribution,
            dailySummaries = featureSummary.DailySummaries,
            repeatedDailyTemplates = featureSummary.RepeatedDailyTemplates,
            rawRows = records.Select(x => new
            {
                x.InvestigationDate,
                x.CustomerId,
                x.Current
            })
        };

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
        log.DebugInspectionLlmResponse(text);

        var usage = BuildUsage(response.Usage);
        log.InfoTokenUsage(usage.InputTokens, usage.OutputTokens, usage.TotalTokens);

        return (Normalize(Parse(text), featureSummary.DailySummaries), usage);
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

    private static string BuildUserPrompt(object payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload, PromptJsonOptions);

        return
            """
            以下は調査員1名分の漏れ電流(mA)巡回調査CSVを要約したものです。
            各調査日は別々の顧客を巡回しており、合格基準は「漏れ電流 1mA 未満」です。
            漏れ電流は設備ごとに自然なばらつきがあるため、値が散らばっているだけでは不正と判断しないでください。
            ただし次の特徴は強い不正シグナルです:
            - 同じ日に複数顧客で同一の電流値が多発する(日内のばらつきが不自然に小さい)
            - 0.97mA のような 1mA 直下の既定値が不自然に集中する
            - 日ごとの値の並び(顧客ID非依存)が複数日で完全一致で再登場する
            - 0.01mA刻みの階段状パターンや、説明しづらい規則性がある

            JSONのみで回答してください。

            入力データ:
            """
            + Environment.NewLine
            + payloadJson;
    }

    private static InspectionAnalysisResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new InspectionAnalysisResult
            {
                Summary = "LLMからの応答が空でした。",
                RecommendedAction = "設定と接続状態を確認して再実行してください。"
            };
        }

        try
        {
            return JsonSerializer.Deserialize<InspectionAnalysisResult>(json, ResponseJsonOptions) ?? new InspectionAnalysisResult
            {
                Summary = "LLM応答のデシリアライズに失敗しました。",
                RecommendedAction = "再実行するか、プロンプトを確認してください。"
            };
        }
        catch (JsonException)
        {
            return new InspectionAnalysisResult
            {
                Summary = "LLM応答のJSON解析に失敗しました。",
                RecommendedAction = "再実行するか、プロンプトを確認してください。"
            };
        }
    }

    private static InspectionAnalysisResult Normalize(
        InspectionAnalysisResult result,
        IReadOnlyList<DailyFeatureSummary> dailySummaries)
    {
        var dayMap = result.DailyResults
            .GroupBy(x => x.InvestigationDate)
            .ToDictionary(
                x => x.Key,
                x => x.First());

        var normalizedDays = dailySummaries
            .Select(summary =>
            {
                if (!dayMap.TryGetValue(summary.InvestigationDate, out var existing))
                {
                    return new DailyRiskResult
                    {
                        InvestigationDate = summary.InvestigationDate,
                        Score = 0,
                        Reason = "日別評価は返されませんでした。",
                        SuspiciousCustomerIds = []
                    };
                }

                return new DailyRiskResult
                {
                    InvestigationDate = existing.InvestigationDate,
                    Score = Math.Clamp(existing.Score, 0, 100),
                    Reason = string.IsNullOrWhiteSpace(existing.Reason) ? "特記事項なし" : existing.Reason.Trim(),
                    SuspiciousCustomerIds = existing.SuspiciousCustomerIds
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.InvestigationDate)
            .ToArray();

        return new InspectionAnalysisResult
        {
            OverallScore = Math.Clamp(result.OverallScore, 0, 100),
            Summary = string.IsNullOrWhiteSpace(result.Summary) ? "要約なし" : result.Summary.Trim(),
            RecommendedAction = string.IsNullOrWhiteSpace(result.RecommendedAction) ? "上位の高スコア日を再確認してください。" : result.RecommendedAction.Trim(),
            SuspiciousPatterns = result.SuspiciousPatterns
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            DailyResults = normalizedDays
        };
    }
}
