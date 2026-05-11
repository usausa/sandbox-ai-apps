namespace FlyerChecker.Services;

using System.Globalization;

using CsvHelper;
using CsvHelper.Configuration;

using FlyerChecker.Models;

// CSVファイルからマスタレコードを読み込むローダー
public sealed class MasterCsvLoader
{
    public async IAsyncEnumerable<MasterRecord> LoadAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            BadDataFound = null
        };

        using var reader = new StreamReader(stream, leaveOpen: true);
        using var csv = new CsvReader(reader, config);

        await foreach (var record in csv.GetRecordsAsync<MasterRecord>(cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(record.Name))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.Id))
            {
                record.Id = Guid.NewGuid().ToString("N");
            }

            yield return record;
        }
    }
}
