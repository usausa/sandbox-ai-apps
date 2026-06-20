namespace PosChecker.Services;

using System.Globalization;

using CsvHelper;
using CsvHelper.Configuration;

using PosChecker.Models;

// SalesHeader / SalesDetail / Promotion の3ビューCSVを読み込み、検証し、
// キー (StoreCode, SalesDate, PosNo, SalesNo) で明細をヘッダに結合する。
public sealed class PosDataLoader
{
    public async Task<PosDataset> LoadAsync(
        Stream headerStream,
        Stream detailStream,
        Stream promotionStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(headerStream);
        ArgumentNullException.ThrowIfNull(detailStream);
        ArgumentNullException.ThrowIfNull(promotionStream);

        var headers = await ReadHeadersAsync(headerStream, cancellationToken).ConfigureAwait(false);
        var details = await ReadDetailsAsync(detailStream, cancellationToken).ConfigureAwait(false);
        var promotions = await ReadPromotionsAsync(promotionStream, cancellationToken).ConfigureAwait(false);

        if (headers.Count == 0)
        {
            throw new InvalidOperationException("SalesHeader CSV に有効なデータ行がありません。");
        }

        var detailsByKey = details
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<SalesDetailRecord>)x.ToArray(), StringComparer.Ordinal);

        var transactions = headers
            .OrderBy(x => x.SalesDate)
            .ThenBy(x => x.SystemTime)
            .ThenBy(x => x.Sequence)
            .Select(header => new SalesTransaction(
                header,
                detailsByKey.TryGetValue(header.Key, out var d) ? d : []))
            .ToArray();

        var orderedPromotions = promotions
            .OrderBy(x => x.SalesDate)
            .ThenBy(x => x.Sequence)
            .ToArray();

        return new PosDataset(transactions, orderedPromotions);
    }

    private static async Task<List<SalesHeaderRecord>> ReadHeadersAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var csv = CreateReader(stream);
        var records = new List<SalesHeaderRecord>();
        var sequence = 0;

        await csv.ReadAsync().ConfigureAwait(false);
        csv.ReadHeader();

        while (await csv.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            records.Add(new SalesHeaderRecord(
                RequireField(csv.GetField("StoreCode"), "StoreCode"),
                ParseDate(csv.GetField("SalesDate"), "SalesDate"),
                ParseNonNegativeInt(csv.GetField("PosNo"), "PosNo"),
                ParseNonNegativeInt(csv.GetField("SalesNo"), "SalesNo"),
                ParseEnum<TransactionType>(csv.GetField("TransactionType"), "TransactionType"),
                ParseEnum<ProcessType>(csv.GetField("ProcessType"), "ProcessType"),
                ParseEnum<RegisterType>(csv.GetField("RegisterType"), "RegisterType"),
                RequireField(csv.GetField("CashierCode"), "CashierCode"),
                RequireField(csv.GetField("CashierName"), "CashierName"),
                NormalizeOptional(csv.GetField("MemberCode")),
                ParseTime(csv.GetField("SystemTime"), "SystemTime"),
                ParseEnum<TenderType>(csv.GetField("TenderType"), "TenderType"),
                ParseOptionalEnum<SettlementType>(csv.GetField("SettlementType"), "SettlementType"),
                NormalizeOptional(csv.GetField("AccountNumber")),
                ParseNonNegativeInt(csv.GetField("PointsUsed"), "PointsUsed"),
                ParseNonNegativeInt(csv.GetField("TotalAmount"), "TotalAmount"),
                sequence));
            sequence++;
        }

        return records;
    }

    private static async Task<List<SalesDetailRecord>> ReadDetailsAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var csv = CreateReader(stream);
        var records = new List<SalesDetailRecord>();

        await csv.ReadAsync().ConfigureAwait(false);
        csv.ReadHeader();

        while (await csv.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            records.Add(new SalesDetailRecord(
                RequireField(csv.GetField("StoreCode"), "StoreCode"),
                ParseDate(csv.GetField("SalesDate"), "SalesDate"),
                ParseNonNegativeInt(csv.GetField("PosNo"), "PosNo"),
                ParseNonNegativeInt(csv.GetField("SalesNo"), "SalesNo"),
                RequireField(csv.GetField("Jancode"), "Jancode"),
                RequireField(csv.GetField("ProductName"), "ProductName"),
                RequireField(csv.GetField("UsageCode"), "UsageCode"),
                RequireField(csv.GetField("UsageName"), "UsageName"),
                ParseNonNegativeInt(csv.GetField("UnitPrice"), "UnitPrice"),
                ParseNonNegativeInt(csv.GetField("Quantity"), "Quantity"),
                ParseNonNegativeInt(csv.GetField("LineAmount"), "LineAmount")));
        }

        return records;
    }

    private static async Task<List<PromotionRecord>> ReadPromotionsAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var csv = CreateReader(stream);
        var records = new List<PromotionRecord>();
        var sequence = 0;

        await csv.ReadAsync().ConfigureAwait(false);
        csv.ReadHeader();

        while (await csv.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            records.Add(new PromotionRecord(
                RequireField(csv.GetField("StoreCode"), "StoreCode"),
                ParseDate(csv.GetField("SalesDate"), "SalesDate"),
                ParseNonNegativeInt(csv.GetField("PosNo"), "PosNo"),
                ParseNonNegativeInt(csv.GetField("SlipNo"), "SlipNo"),
                RequireField(csv.GetField("PlanCode"), "PlanCode"),
                RequireField(csv.GetField("PlanName"), "PlanName"),
                RequireField(csv.GetField("CouponCode"), "CouponCode"),
                NormalizeOptional(csv.GetField("ScannedMemberCode")),
                RequireField(csv.GetField("CouponJan"), "CouponJan"),
                ParseEnum<IssueType>(csv.GetField("IssueType"), "IssueType"),
                RequireField(csv.GetField("CouponName"), "CouponName"),
                ParseOptionalDate(csv.GetField("StartDate"), "StartDate"),
                ParseOptionalDate(csv.GetField("EndDate"), "EndDate"),
                ParseEnum<MemberTargetType>(csv.GetField("MemberTargetType"), "MemberTargetType"),
                sequence));
            sequence++;
        }

        return records;
    }

    private static CsvReader CreateReader(Stream stream)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            BadDataFound = null
        };

        var reader = new StreamReader(stream, leaveOpen: true);
        return new CsvReader(reader, config);
    }

    private static DateOnly ParseDate(string? text, string columnName)
    {
        if (!DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            throw new FormatException($"{columnName} の日付形式が不正です: {text}");
        }

        return value;
    }

    private static DateOnly? ParseOptionalDate(string? text, string columnName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return ParseDate(text, columnName);
    }

    private static TimeOnly ParseTime(string? text, string columnName)
    {
        if (!TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            throw new FormatException($"{columnName} の時刻形式が不正です: {text}");
        }

        return value;
    }

    private static TEnum ParseEnum<TEnum>(string? text, string columnName)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(text, ignoreCase: true, out var value) || !Enum.IsDefined(value))
        {
            throw new FormatException($"{columnName} の値が不正です: {text}");
        }

        return value;
    }

    private static TEnum? ParseOptionalEnum<TEnum>(string? text, string columnName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return ParseEnum<TEnum>(text, columnName);
    }

    private static int ParseNonNegativeInt(string? text, string columnName)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || (value < 0))
        {
            throw new FormatException($"{columnName} が不正です: {text}");
        }

        return value;
    }

    private static string RequireField(string? text, string columnName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException($"{columnName} が空の行があります。");
        }

        return text.Trim();
    }

    private static string? NormalizeOptional(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
