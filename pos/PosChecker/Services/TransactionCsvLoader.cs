namespace PosChecker.Services;

using System.Globalization;

using CsvHelper;
using CsvHelper.Configuration;

using PosChecker.Models;

public sealed class TransactionCsvLoader
{
    public async Task<IReadOnlyList<TransactionRecord>> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            BadDataFound = null
        };

        using var reader = new StreamReader(stream, leaveOpen: true);
        using var csv = new CsvReader(reader, config);

        var records = new List<TransactionRecord>();
        var sequence = 0;

        await csv.ReadAsync().ConfigureAwait(false);
        csv.ReadHeader();

        while (await csv.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var record = new TransactionRecord(
                ParseDate(csv.GetField("BusinessDate")),
                RequireField(csv.GetField("CashierId"), "CashierId"),
                RequireField(csv.GetField("TransactionId"), "TransactionId"),
                ParseTime(csv.GetField("Time")),
                ParseEnum<TransactionType>(csv.GetField("Type"), "Type"),
                ParseNonNegativeInt(csv.GetField("Amount"), "Amount"),
                ParseNonNegativeInt(csv.GetField("ItemCount"), "ItemCount"),
                ParseEnum<PaymentMethod>(csv.GetField("PaymentMethod"), "PaymentMethod"),
                ParseNonNegativeInt(csv.GetField("DiscountAmount"), "DiscountAmount"),
                ParseNonNegativeInt(csv.GetField("PointsEarned"), "PointsEarned"),
                ParseNonNegativeInt(csv.GetField("PointsRedeemed"), "PointsRedeemed"),
                NormalizeOptional(csv.GetField("MembershipId")),
                NormalizeOptional(csv.GetField("OriginalTransactionId")),
                ParseOptionalBool(csv.GetField("HasReceipt"), "HasReceipt"),
                sequence);

            records.Add(record);
            sequence++;
        }

        if (records.Count == 0)
        {
            throw new InvalidOperationException("CSVに有効なデータ行がありません。");
        }

        return records
            .OrderBy(x => x.BusinessDate)
            .ThenBy(x => x.Time)
            .ThenBy(x => x.Sequence)
            .ToArray();
    }

    private static DateOnly ParseDate(string? text)
    {
        if (!DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            throw new FormatException($"営業日の形式が不正です: {text}");
        }

        return value;
    }

    private static TimeOnly ParseTime(string? text)
    {
        if (!TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            throw new FormatException($"取引時刻の形式が不正です: {text}");
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

    private static bool? ParseOptionalBool(string? text, string columnName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!bool.TryParse(text, out var value))
        {
            throw new FormatException($"{columnName} が不正です: {text}");
        }

        return value;
    }
}
