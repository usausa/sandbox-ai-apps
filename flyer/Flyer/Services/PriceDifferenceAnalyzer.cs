namespace Flyer.Services;

using System.Text;
using System.Text.Json;

using Flyer.Services.Models;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

/// <summary>
/// Asks the LLM to compare a flyer item with the matching master record(s) and report the price difference.
/// </summary>
public sealed class PriceDifferenceAnalyzer
{
    private const string SystemPrompt =
        "あなたはチラシの広告価格と公式マスタ価格リストを比較するアシスタントです。" +
        "マークダウンや説明文を一切含めず、以下のスキーマのJSONオブジェクトのみを返してください: " +
        "{\"matched_name\":\"<候補の中で最も一致する公式商品名>\",\"matched_price\":<整数>,\"difference\":<整数>,\"comment\":\"<短いコメント>\"}. " +
        "候補の中から最も一致する1件を選択してください。'difference' = チラシ価格 - マスタ価格（負の値はマスタより安いことを意味します）。" +
        "一致する候補がない場合は matched_name を空文字、matched_price と difference を 0 にし、comment に理由を記載してください。";

    private readonly IChatClient chatClient;
    private readonly ILogger<PriceDifferenceAnalyzer> logger;

    public PriceDifferenceAnalyzer(IChatClient chatClient, ILogger<PriceDifferenceAnalyzer> logger)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(logger);
        this.chatClient = chatClient;
        this.logger = logger;
    }

    public async Task<PriceCheckResult> AnalyzeAsync(
        FlyerItem flyerItem,
        IReadOnlyList<ProductRecord> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flyerItem);
        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0)
        {
            return new PriceCheckResult(flyerItem.Name, flyerItem.Price, string.Empty, 0, 0, "候補なし");
        }

        var userPrompt = BuildUserPrompt(flyerItem, candidates);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, userPrompt)
        };

        var options = new ChatOptions
        {
            Temperature = 0,
            ResponseFormat = ChatResponseFormat.Json
        };

        var response = await chatClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        var text = response.Text ?? string.Empty;
        logger.LogDebug("Diff LLM response: {Response}", text);

        return Parse(flyerItem, text);
    }

    private static string BuildUserPrompt(FlyerItem item, IReadOnlyList<ProductRecord> candidates)
    {
        var sb = new StringBuilder();
        sb.Append("Flyer item: name=\"").Append(item.Name).Append("\", price=").Append(item.Price).AppendLine();
        sb.AppendLine("Candidates from master:");
        foreach (var c in candidates)
        {
            sb.Append("- name=\"").Append(c.Name).Append("\", price=").Append(c.Price).AppendLine();
        }
        return sb.ToString();
    }

    private static PriceCheckResult Parse(FlyerItem item, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PriceCheckResult(item.Name, item.Price, string.Empty, 0, 0, "応答解析失敗");
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var matchedName = root.TryGetProperty("matched_name", out var n) ? (n.GetString() ?? string.Empty) : string.Empty;
            var matchedPrice = root.TryGetProperty("matched_price", out var p) && p.TryGetInt32(out var pv) ? pv : 0;
            var difference = root.TryGetProperty("difference", out var d) && d.TryGetInt32(out var dv) ? dv : (item.Price - matchedPrice);
            var comment = root.TryGetProperty("comment", out var c) ? (c.GetString() ?? string.Empty) : string.Empty;

            return new PriceCheckResult(item.Name, item.Price, matchedName, matchedPrice, difference, comment);
        }
        catch (JsonException)
        {
            return new PriceCheckResult(item.Name, item.Price, string.Empty, 0, 0, "応答解析失敗");
        }
    }
}
