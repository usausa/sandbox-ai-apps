namespace PosChecker.Services;

using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using PosChecker.Models;

public sealed class PosFraudAnalyzer
{
    private static readonly JsonSerializerOptions PromptJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<PosFraudAnalyzer> log;
    private readonly IChatClient chatClient;
    private readonly string systemPrompt;

    public PosFraudAnalyzer(
        ILogger<PosFraudAnalyzer> log,
        IChatClient chatClient)
    {
        this.log = log;
        this.chatClient = chatClient;

        var promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "pos_analyzer.txt");
        systemPrompt = File.ReadAllText(promptPath).Trim();
    }

    public async Task<PosAnalysisResult> AnalyzeAsync(
        PosFeatureSummary featureSummary,
        IReadOnlyList<TransactionRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(featureSummary);
        ArgumentNullException.ThrowIfNull(records);

        var payload = new
        {
            datasetSummary = new
            {
                featureSummary.RecordCount,
                featureSummary.CashierId,
                featureSummary.StartDate,
                featureSummary.EndDate,
                featureSummary.BusinessDayCount,
                featureSummary.SalesCount,
                featureSummary.SalesAmount,
                featureSummary.VoidCount,
                featureSummary.VoidAmount,
                featureSummary.ReturnCount,
                featureSummary.ReturnAmount,
                featureSummary.NoSaleCount,
                featureSummary.VoidRatio,
                featureSummary.ReturnAmountRatio,
                featureSummary.NoReceiptReturnRatio,
                featureSummary.PointsEarnedTotal,
                featureSummary.PointsRedeemedTotal,
                featureSummary.NonMemberPointsEarnedCount,
                featureSummary.NonMemberPointsRedeemedCount,
                featureSummary.PointsRedeemedCashCount
            },
            typeDistribution = featureSummary.TypeDistribution,
            paymentDistribution = featureSummary.PaymentDistribution,
            dailySummaries = featureSummary.DailySummaries,
            sequenceAnomalies = featureSummary.SequenceAnomalies,
            rawRows = records.Select(x => new
            {
                x.BusinessDate,
                x.Time,
                x.TransactionId,
                x.Type,
                x.Amount,
                x.ItemCount,
                x.PaymentMethod,
                x.DiscountAmount,
                x.PointsEarned,
                x.PointsRedeemed,
                x.MembershipId,
                x.OriginalTransactionId,
                x.HasReceipt
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
        log.DebugPosLlmResponse(text);

        return Normalize(Parse(text), featureSummary.DailySummaries);
    }

    private static string BuildUserPrompt(object payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload, PromptJsonOptions);

        return
            """
            以下は小売店のレジ担当者1名分・複数営業日のPOSトランザクションを要約したものです。
            判定は「1営業日×1従業員」単位で行ってください。
            取引種別は 売上(Sale)/取消(Void)/返品(Return)/レジ開放(NoSale) です。
            繁忙日に取引数や返品数が増えるのは自然なので、件数の絶対値ではなく比率・反復・時間帯の不自然さを見てください。
            次の特徴は強い不正シグナルです:
            - 売上直後に同額を取り消すVoidの反復、Void率が高く現金取引に偏る(取消抜き取り)
            - 返金額比率が高い・レシート無し返品が多い・現金返金が大きい・同額返品の反復(返品抜き取り)
            - 会員IDが無いのにポイント付与/利用がある、ポイント利用が特定会員IDに偏る、ポイント利用が現金売上に集中、ポイント利用直後の取消(ポイント不正)
            - レジ開放(NoSale)の多発、値引き比率が高い、閑散帯(営業時間外)への取消・返品の集中

            JSONのみで回答してください。

            入力データ:
            """
            + Environment.NewLine
            + payloadJson;
    }

    private static PosAnalysisResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PosAnalysisResult
            {
                Summary = "LLMからの応答が空でした。",
                RecommendedAction = "設定と接続状態を確認して再実行してください。"
            };
        }

        try
        {
            return JsonSerializer.Deserialize<PosAnalysisResult>(json, ResponseJsonOptions) ?? new PosAnalysisResult
            {
                Summary = "LLM応答のデシリアライズに失敗しました。",
                RecommendedAction = "再実行するか、プロンプトを確認してください。"
            };
        }
        catch (JsonException)
        {
            return new PosAnalysisResult
            {
                Summary = "LLM応答のJSON解析に失敗しました。",
                RecommendedAction = "再実行するか、プロンプトを確認してください。"
            };
        }
    }

    private static PosAnalysisResult Normalize(
        PosAnalysisResult result,
        IReadOnlyList<DailyFeatureSummary> dailySummaries)
    {
        var dayMap = result.DailyResults
            .GroupBy(x => x.BusinessDate)
            .ToDictionary(x => x.Key, x => x.First());

        var normalizedDays = dailySummaries
            .Select(summary =>
            {
                if (!dayMap.TryGetValue(summary.BusinessDate, out var existing))
                {
                    return new DailyRiskResult
                    {
                        BusinessDate = summary.BusinessDate,
                        Score = 0,
                        Reason = "日別評価は返されませんでした。",
                        SuspiciousTransactionIds = []
                    };
                }

                return new DailyRiskResult
                {
                    BusinessDate = existing.BusinessDate,
                    Score = Math.Clamp(existing.Score, 0, 100),
                    Reason = string.IsNullOrWhiteSpace(existing.Reason) ? "特記事項なし" : existing.Reason.Trim(),
                    SuspiciousTransactionIds = existing.SuspiciousTransactionIds
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.BusinessDate)
            .ToArray();

        return new PosAnalysisResult
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
