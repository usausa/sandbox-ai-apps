namespace InspectorChecker.Services;

using System.Globalization;

using CsvHelper;
using CsvHelper.Configuration;

using InspectorChecker.Models;

public sealed class SurveyCsvLoader
{
    public async Task<IReadOnlyList<SurveyRecord>> LoadAsync(
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

        var records = new List<SurveyRecord>();
        var sequence = 0;

        await csv.ReadAsync().ConfigureAwait(false);
        csv.ReadHeader();

        while (await csv.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var investigationDate = csv.GetField("InvestigationDate") ?? string.Empty;
            var customerId = csv.GetField("CustomerId") ?? string.Empty;
            var voltageText = csv.GetField("Voltage") ?? string.Empty;

            if (!double.TryParse(voltageText, CultureInfo.InvariantCulture, out var voltage))
            {
                throw new FormatException($"Voltage の形式が不正です: {voltageText}");
            }

            if (!DateOnly.TryParse(investigationDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                throw new FormatException($"調査日の形式が不正です: {investigationDate}");
            }

            if (string.IsNullOrWhiteSpace(customerId))
            {
                throw new FormatException("CustomerId が空の行があります。");
            }

            if (double.IsNaN(voltage) || voltage <= 0)
            {
                throw new FormatException($"Voltage が不正です: {voltage}");
            }

            records.Add(new SurveyRecord(
                date,
                customerId.Trim(),
                Math.Round(voltage, 1),
                sequence));

            sequence++;
        }

        if (records.Count == 0)
        {
            throw new InvalidOperationException("CSVに有効なデータ行がありません。");
        }

        return records
            .OrderBy(x => x.InvestigationDate)
            .ThenBy(x => x.Sequence)
            .ToArray();
    }
}
