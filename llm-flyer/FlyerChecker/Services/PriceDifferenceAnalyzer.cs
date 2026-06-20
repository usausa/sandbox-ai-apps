namespace FlyerChecker.Services;

using System.Text;
using System.Text.Json;

using FlyerChecker.Models;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

// チラシ商品とマスタ候補の価格差異を分析するサービス
public sealed class PriceDifferenceAnalyzer
{
    private readonly ILogger<PriceDifferenceAnalyzer> log;
    private readonly string systemPrompt;
    private readonly IChatClient chatClient;

    public PriceDifferenceAnalyzer(
        ILogger<PriceDifferenceAnalyzer> log,
        IChatClient chatClient)
    {
        this.log = log;
        this.chatClient = chatClient;

        var promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "price_analyzer.txt");
        systemPrompt = File.ReadAllText(promptPath).Trim();
    }

    public async Task<PriceCheckResult> AnalyzeAsync(
        FlyerItem flyerItem,
        IReadOnlyList<ProductSearchResult> candidates,
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
        log.DebugDiffLlmResponse(text);

        return Parse(flyerItem, text);
    }

    private static string BuildUserPrompt(FlyerItem item, IEnumerable<ProductSearchResult> candidates)
    {
        var sb = new StringBuilder();
        sb.Append("チラシ商品: 商品名=\"").Append(item.Name).Append("\", 価格=").Append(item.Price).AppendLine("円");
        sb.AppendLine("マスタ候補(similarity_scoreの降順):");
        foreach (var c in candidates)
        {
            sb.Append("- 商品名=\"").Append(c.Name)
              .Append("\", 価格=").Append(c.Price)
              .Append("円, similarity_score=").Append(c.Score.ToString("F4", System.Globalization.CultureInfo.InvariantCulture))
              .AppendLine();
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
            var difference = root.TryGetProperty("difference", out var d) && d.TryGetInt32(out var dv) ? dv : item.Price - matchedPrice;
            var comment = root.TryGetProperty("comment", out var c) ? (c.GetString() ?? string.Empty) : string.Empty;

            return new PriceCheckResult(item.Name, item.Price, matchedName, matchedPrice, difference, comment);
        }
        catch (JsonException)
        {
            return new PriceCheckResult(item.Name, item.Price, string.Empty, 0, 0, "応答解析失敗");
        }
    }
}
