namespace InspectorChecker.Services;

using System.Text.Json;

using InspectorChecker.Models;

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

    public InspectionFraudAnalyzer(
        ILogger<InspectionFraudAnalyzer> log,
        IChatClient chatClient)
    {
        this.log = log;
        this.chatClient = chatClient;

        var promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "inspection_analyzer.txt");
        systemPrompt = File.ReadAllText(promptPath).Trim();
    }

    public async Task<InspectionAnalysisResult> AnalyzeAsync(
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
                featureSummary.MeanVoltage,
                featureSummary.StandardDeviation
            },
            customerProfiles = featureSummary.CustomerProfiles,
            dailySummaries = featureSummary.DailySummaries,
            repeatedDailyTemplates = featureSummary.RepeatedDailyTemplates,
            rawRows = records.Select(x => new
            {
                x.InvestigationDate,
                x.CustomerId,
                x.Voltage
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

        return Normalize(Parse(text), featureSummary.DailySummaries);
    }

    private static string BuildUserPrompt(object payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload, PromptJsonOptions);

        return
            """
            以下は担当者1名分の30日調査CSVを要約したものです。
            顧客ごとの平均電圧は多少異なるため、顧客間の差だけでは不正と判断しないでください。
            ただし次の特徴は強い不正シグナルです:
            - 同じ日に複数顧客で同一電圧が多発する
            - 100.0V、99.9V、100.1Vのような既定値を機械的に使い回している
            - 顧客ごとの基準値からのズレ方が不自然に揃っている
            - 日ごとのテンプレートが完全一致で再登場する
            - 0.1V刻みの階段状パターンや、説明しづらい規則性がある

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
