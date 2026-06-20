namespace PosChecker.Services;

using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using PosChecker.Models;
using PosChecker.Settings;

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
    private readonly FoundrySettings foundrySettings;
    private readonly string systemPrompt;

    public PosFraudAnalyzer(
        ILogger<PosFraudAnalyzer> log,
        IChatClient chatClient,
        FoundrySettings foundrySettings)
    {
        this.log = log;
        this.chatClient = chatClient;
        this.foundrySettings = foundrySettings;

        var promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "pos_analyzer.txt");
        systemPrompt = File.ReadAllText(promptPath).Trim();
    }

    public async Task<(PosAnalysisResult Analysis, TokenUsageResult Usage)> AnalyzeAsync(
        PosFeatureSummary featureSummary,
        PosDataset dataset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(featureSummary);
        ArgumentNullException.ThrowIfNull(dataset);

        var payload = BuildPayload(featureSummary, dataset);
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

        var usage = BuildUsage(response.Usage);
        log.InfoTokenUsage(usage.InputTokens, usage.OutputTokens, usage.TotalTokens);

        return (Normalize(Parse(text), featureSummary.CashierSummaries, featureSummary.MemberSummaries), usage);
    }

    private static object BuildPayload(PosFeatureSummary summary, PosDataset dataset)
    {
        return new
        {
            datasetSummary = new
            {
                summary.TransactionCount,
                summary.DetailCount,
                summary.PromotionCount,
                summary.StoreCount,
                summary.CashierCount,
                summary.MemberCount,
                summary.StartDate,
                summary.EndDate,
                summary.SalesCount,
                summary.SalesAmount,
                summary.ReturnCount,
                summary.ReturnAmount,
                summary.RekeyCount
            },
            typeDistribution = summary.TypeDistribution,
            tenderDistribution = summary.TenderDistribution,
            cashierSummaries = summary.CashierSummaries,
            memberSummaries = summary.MemberSummaries,
            fraudSignals = summary.FraudSignals,
            rawPromotions = dataset.Promotions.Select(x => new
            {
                x.StoreCode,
                x.SalesDate,
                x.PosNo,
                x.SlipNo,
                x.PlanCode,
                x.PlanName,
                x.CouponCode,
                x.ScannedMemberCode,
                x.CouponJan,
                x.IssueType,
                x.CouponName,
                x.StartDate,
                x.EndDate,
                x.MemberTargetType
            })
        };
    }

    // Foundry が返したトークン使用量を取り出し、USD単価×ドル円レートから概算費用(円)を算出する。
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
            以下は小売店の複数店舗・複数担当者・複数会員のPOSトランザクションを要約したものです。
            判定は「店舗×担当者」を主軸に、ポイント不正・クーポン不正は会員も評価してください。
            取引種別は 売上(Sale)/返品(Return)、処理区分には打ち直し(Rekey)が含まれます。
            次の特徴は不正一覧に基づく強い不正シグナルです:
            - 担当者の売上が特定会員コードに偏る/同一会員の短時間連続会計(ポイント不正付与: PointAbuse)
            - 同一JAN・同一用途を複数購入する会計を頻発する担当者(かご抜け: CartBypass)
            - 同一会員が同一クーポンを反復スキャン、会員限定/アプリクーポンの配信期間と乖離(クーポン不正: CouponAbuse)
            - 担当者の返品件数・返品率が他より高い、時間帯に偏る(フリー返品不正: ReturnFraud)
            - 担当者の打直件数・打直率が他より高い、時間帯に偏る(打ち直し不正: RekeyFraud)

            件数の絶対値ではなく、他担当者との比率の偏り・反復・時間帯の不自然さで判断してください。
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
        IReadOnlyList<CashierFeatureSummary> cashierSummaries,
        IReadOnlyList<MemberFeatureSummary> memberSummaries)
    {
        var validMembers = memberSummaries
            .Select(x => x.MemberCode)
            .ToHashSet(StringComparer.Ordinal);

        var resultMap = result.CashierResults
            .GroupBy(x => (x.StoreCode, x.CashierCode))
            .ToDictionary(x => x.Key, x => x.First());

        var normalizedCashiers = cashierSummaries
            .Select(summary =>
            {
                if (!resultMap.TryGetValue((summary.StoreCode, summary.CashierCode), out var existing))
                {
                    return new CashierRiskResult
                    {
                        StoreCode = summary.StoreCode,
                        CashierCode = summary.CashierCode,
                        CashierName = summary.CashierName,
                        Score = 0,
                        Reason = "担当者評価は返されませんでした。"
                    };
                }

                return new CashierRiskResult
                {
                    StoreCode = summary.StoreCode,
                    CashierCode = summary.CashierCode,
                    CashierName = string.IsNullOrWhiteSpace(existing.CashierName) ? summary.CashierName : existing.CashierName,
                    Score = Math.Clamp(existing.Score, 0, 100),
                    Reason = string.IsNullOrWhiteSpace(existing.Reason) ? "特記事項なし" : existing.Reason.Trim(),
                    Scenarios = CleanList(existing.Scenarios),
                    SuspiciousKeys = CleanList(existing.SuspiciousKeys)
                };
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.StoreCode, StringComparer.Ordinal)
            .ThenBy(x => x.CashierCode, StringComparer.Ordinal)
            .ToArray();

        var normalizedMembers = result.MemberResults
            .Where(x => !string.IsNullOrWhiteSpace(x.MemberCode) && validMembers.Contains(x.MemberCode))
            .GroupBy(x => x.MemberCode, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(x => new MemberRiskResult
            {
                MemberCode = x.MemberCode,
                Score = Math.Clamp(x.Score, 0, 100),
                Reason = string.IsNullOrWhiteSpace(x.Reason) ? "特記事項なし" : x.Reason.Trim(),
                Scenarios = CleanList(x.Scenarios)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.MemberCode, StringComparer.Ordinal)
            .ToArray();

        return new PosAnalysisResult
        {
            OverallScore = Math.Clamp(result.OverallScore, 0, 100),
            Summary = string.IsNullOrWhiteSpace(result.Summary) ? "要約なし" : result.Summary.Trim(),
            RecommendedAction = string.IsNullOrWhiteSpace(result.RecommendedAction) ? "上位の高スコア担当者を再確認してください。" : result.RecommendedAction.Trim(),
            SuspiciousPatterns = CleanList(result.SuspiciousPatterns),
            CashierResults = normalizedCashiers,
            MemberResults = normalizedMembers
        };
    }

    private static string[] CleanList(IReadOnlyList<string> values) =>
        values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
