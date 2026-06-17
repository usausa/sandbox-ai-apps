namespace RoutePlanner.Services;

using System.Globalization;

using CsvHelper;
using CsvHelper.Configuration;

using RoutePlanner.Models;

// 訪問先一覧CSVを VisitTarget のリストへ変換する。英語ヘッダを前提とする。
public static class VisitCsvLoader
{
    public static async Task<IReadOnlyList<VisitTarget>> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            BadDataFound = null,
            HeaderValidated = null
        };

        using var reader = new StreamReader(stream, leaveOpen: true);
        using var csv = new CsvReader(reader, config);

        var visits = new List<VisitTarget>();

        await csv.ReadAsync().ConfigureAwait(false);
        csv.ReadHeader();

        while (await csv.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var visitId = Field(csv, "VisitId");
            if (string.IsNullOrWhiteSpace(visitId))
            {
                continue;
            }

            var latText = Field(csv, "Latitude");
            var lonText = Field(csv, "Longitude");
            if (!double.TryParse(latText, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                !double.TryParse(lonText, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
            {
                throw new FormatException($"緯度経度の形式が不正です: VisitId={visitId}");
            }

            visits.Add(new VisitTarget(
                visitId.Trim(),
                Field(csv, "CustomerName")?.Trim() ?? string.Empty,
                Field(csv, "Address")?.Trim() ?? string.Empty,
                latitude,
                longitude,
                ParseCategory(Field(csv, "Category")),
                ParseNullableInt(Field(csv, "ServiceMinutes")),
                ParseTime(Field(csv, "WindowStart")),
                ParseTime(Field(csv, "WindowEnd")),
                ParseStrictness(Field(csv, "WindowStrict")),
                ParsePriority(Field(csv, "Priority")),
                ParseBool(Field(csv, "AppointmentRequired")),
                NullIfEmpty(Field(csv, "WorkType")),
                NullIfEmpty(Field(csv, "BuildingGroupId")),
                NullIfEmpty(Field(csv, "AccessNote")),
                NullIfEmpty(Field(csv, "HazardNote")),
                NullIfEmpty(Field(csv, "ContactPhone"))));
        }

        if (visits.Count == 0)
        {
            throw new InvalidOperationException("CSVに有効なデータ行がありません。");
        }

        return visits;
    }

    private static string? Field(CsvReader csv, string name) =>
        csv.TryGetField<string>(name, out var value) ? value : null;

    private static VisitCategory ParseCategory(string? value) => value?.Trim() switch
    {
        "業務" or "Business" or "business" => VisitCategory.Business,
        _ => VisitCategory.General
    };

    private static TimeWindowStrictness ParseStrictness(string? value) => value?.Trim() switch
    {
        "厳守" or "Strict" or "strict" => TimeWindowStrictness.Strict,
        _ => TimeWindowStrictness.Preferred
    };

    private static VisitPriority ParsePriority(string? value) => value?.Trim() switch
    {
        "高" or "High" or "high" => VisitPriority.High,
        "低" or "Low" or "low" => VisitPriority.Low,
        _ => VisitPriority.Medium
    };

    private static bool ParseBool(string? value) => value?.Trim() switch
    {
        "true" or "True" or "1" or "○" or "要" or "yes" or "Yes" => true,
        _ => false
    };

    private static int? ParseNullableInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static TimeOnly? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
