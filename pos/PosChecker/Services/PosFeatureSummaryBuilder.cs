namespace PosChecker.Services;

using PosChecker.Models;
using PosChecker.Settings;

public sealed class PosFeatureSummaryBuilder
{
    private readonly PosCheckerSettings settings;

    public PosFeatureSummaryBuilder(PosCheckerSettings settings)
    {
        this.settings = settings;
    }

    public PosFeatureSummary Build(IReadOnlyList<TransactionRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            throw new InvalidOperationException("分析対象のデータがありません。");
        }

        var ordered = records
            .OrderBy(x => x.BusinessDate)
            .ThenBy(x => x.Time)
            .ThenBy(x => x.Sequence)
            .ToArray();

        var window = settings.SaleThenVoidWindowSeconds;
        var minOccurrences = settings.RepeatedRefundMinOccurrences;
        var startHour = settings.BusinessHoursStartHour;
        var endHour = settings.BusinessHoursEndHour;

        var anomalies = new List<SequenceAnomaly>();

        var dailySummaries = ordered
            .GroupBy(x => x.BusinessDate)
            .OrderBy(x => x.Key)
            .Select(group => BuildDaily(group.Key, group.ToArray(), anomalies, window, minOccurrences, startHour, endHour))
            .ToArray();

        var sales = ordered.Where(x => x.Type == TransactionType.Sale).ToArray();
        var voids = ordered.Where(x => x.Type == TransactionType.Void).ToArray();
        var returns = ordered.Where(x => x.Type == TransactionType.Return).ToArray();

        var salesCount = sales.Length;
        var salesAmount = sales.Sum(x => (long)x.Amount);
        var returnCount = returns.Length;
        var returnAmount = returns.Sum(x => (long)x.Amount);
        var noReceiptReturnCount = returns.Count(x => x.HasReceipt == false);

        var cashierId = ordered
            .GroupBy(x => x.CashierId, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .First().Key;

        return new PosFeatureSummary(
            ordered.Length,
            cashierId,
            ordered[0].BusinessDate,
            ordered[^1].BusinessDate,
            dailySummaries.Length,
            salesCount,
            salesAmount,
            voids.Length,
            voids.Sum(x => (long)x.Amount),
            returnCount,
            returnAmount,
            ordered.Count(x => x.Type == TransactionType.NoSale),
            Ratio(voids.Length, salesCount),
            Ratio(returnAmount, salesAmount),
            Ratio(noReceiptReturnCount, returnCount),
            sales.Sum(x => x.PointsEarned),
            sales.Sum(x => x.PointsRedeemed),
            ordered.Count(x => string.IsNullOrEmpty(x.MembershipId) && (x.PointsEarned > 0)),
            ordered.Count(x => string.IsNullOrEmpty(x.MembershipId) && (x.PointsRedeemed > 0)),
            sales.Count(x => (x.PointsRedeemed > 0) && (x.PaymentMethod == PaymentMethod.Cash)),
            BuildTypeDistribution(ordered),
            BuildPaymentDistribution(ordered),
            dailySummaries,
            anomalies
                .OrderBy(x => x.Date)
                .ThenBy(x => x.Kind, StringComparer.Ordinal)
                .ThenBy(x => x.TransactionId, StringComparer.Ordinal)
                .ToArray());
    }

    private static DailyFeatureSummary BuildDaily(
        DateOnly date,
        TransactionRecord[] dayRecords,
        List<SequenceAnomaly> anomalies,
        int window,
        int minOccurrences,
        int startHour,
        int endHour)
    {
        var sales = dayRecords.Where(x => x.Type == TransactionType.Sale).ToArray();
        var voids = dayRecords.Where(x => x.Type == TransactionType.Void).ToArray();
        var returns = dayRecords.Where(x => x.Type == TransactionType.Return).ToArray();

        var salesCount = sales.Length;
        var salesAmount = sales.Sum(x => (long)x.Amount);
        var returnCount = returns.Length;
        var returnAmount = returns.Sum(x => (long)x.Amount);
        var noReceiptReturnCount = returns.Count(x => x.HasReceipt == false);
        var discountAmount = sales.Sum(x => (long)x.DiscountAmount);

        var redeemSales = sales.Where(x => x.PointsRedeemed > 0).ToArray();
        var (redeemConcentration, topRedeemMembership) = ComputeRedeemConcentration(redeemSales);
        var topReturnMembership = ComputeTopMembership(returns);

        var saleThenVoidCount = DetectVoidAnomalies(date, dayRecords, anomalies, window);
        var repeatedRefundCount = DetectRepeatedRefunds(date, returns, anomalies, minOccurrences);

        var afterHoursCount = dayRecords.Count(x =>
            ((x.Type == TransactionType.Void) || (x.Type == TransactionType.Return)) &&
            ((x.Time.Hour < startHour) || (x.Time.Hour >= endHour)));

        return new DailyFeatureSummary(
            date,
            dayRecords.Length,
            salesCount,
            salesAmount,
            voids.Length,
            voids.Sum(x => (long)x.Amount),
            Ratio(voids.Length, salesCount),
            returnCount,
            returnAmount,
            Ratio(returnAmount, salesAmount),
            noReceiptReturnCount,
            Ratio(noReceiptReturnCount, returnCount),
            returns.Where(x => x.PaymentMethod == PaymentMethod.Cash).Sum(x => (long)x.Amount),
            dayRecords.Count(x => x.Type == TransactionType.NoSale),
            discountAmount,
            Ratio(discountAmount, salesAmount),
            sales.Sum(x => x.PointsRedeemed),
            sales.Sum(x => x.PointsEarned),
            dayRecords.Count(x => string.IsNullOrEmpty(x.MembershipId) && (x.PointsEarned > 0)),
            dayRecords.Count(x => string.IsNullOrEmpty(x.MembershipId) && (x.PointsRedeemed > 0)),
            sales.Count(x => (x.PointsRedeemed > 0) && (x.PaymentMethod == PaymentMethod.Cash)),
            redeemConcentration,
            topRedeemMembership,
            afterHoursCount,
            saleThenVoidCount,
            repeatedRefundCount,
            topReturnMembership);
    }

    private static int DetectVoidAnomalies(
        DateOnly date,
        TransactionRecord[] dayRecords,
        List<SequenceAnomaly> anomalies,
        int window)
    {
        var salesById = dayRecords
            .Where(x => x.Type == TransactionType.Sale)
            .GroupBy(x => x.TransactionId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        var saleThenVoidCount = 0;

        foreach (var voidRecord in dayRecords.Where(x => x.Type == TransactionType.Void))
        {
            if (voidRecord.OriginalTransactionId is null ||
                !salesById.TryGetValue(voidRecord.OriginalTransactionId, out var sale))
            {
                continue;
            }

            var secondsApart = (int)(voidRecord.Time.ToTimeSpan() - sale.Time.ToTimeSpan()).TotalSeconds;
            if ((secondsApart < 0) || (secondsApart > window))
            {
                continue;
            }

            saleThenVoidCount++;
            anomalies.Add(new SequenceAnomaly(date, "SaleThenVoid", voidRecord.TransactionId, sale.TransactionId, voidRecord.Amount, 0, secondsApart));

            if (sale.PointsRedeemed > 0)
            {
                anomalies.Add(new SequenceAnomaly(date, "PointsRedeemThenVoid", voidRecord.TransactionId, sale.TransactionId, sale.PointsRedeemed, 0, secondsApart));
            }
        }

        return saleThenVoidCount;
    }

    private static int DetectRepeatedRefunds(
        DateOnly date,
        TransactionRecord[] returns,
        List<SequenceAnomaly> anomalies,
        int minOccurrences)
    {
        var repeated = returns
            .GroupBy(x => x.Amount)
            .Select(g => new { Amount = g.Key, Count = g.Count() })
            .Where(x => x.Count >= minOccurrences)
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Amount)
            .ToArray();

        foreach (var group in repeated)
        {
            anomalies.Add(new SequenceAnomaly(date, "RepeatedRefundAmount", null, null, group.Amount, group.Count, 0));
        }

        return repeated.Length;
    }

    private static (double Concentration, string? TopMembership) ComputeRedeemConcentration(TransactionRecord[] redeemSales)
    {
        if (redeemSales.Length == 0)
        {
            return (0d, null);
        }

        var top = redeemSales
            .GroupBy(x => x.MembershipId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .First();

        var concentration = Round(top.Count / (double)redeemSales.Length);
        var membership = string.IsNullOrEmpty(top.Key) ? null : top.Key;
        return (concentration, membership);
    }

    private static string? ComputeTopMembership(TransactionRecord[] returns)
    {
        var withMember = returns.Where(x => !string.IsNullOrEmpty(x.MembershipId)).ToArray();
        if (withMember.Length == 0)
        {
            return null;
        }

        return withMember
            .GroupBy(x => x.MembershipId!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .First().Key;
    }

    private static TransactionTypeBreakdown[] BuildTypeDistribution(TransactionRecord[] records) =>
        records
            .GroupBy(x => x.Type)
            .Select(g => new TransactionTypeBreakdown(g.Key, g.Count(), g.Sum(y => (long)y.Amount), Ratio(g.Count(), records.Length)))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Type)
            .ToArray();

    private static PaymentBreakdown[] BuildPaymentDistribution(TransactionRecord[] records) =>
        records
            .GroupBy(x => x.PaymentMethod)
            .Select(g => new PaymentBreakdown(g.Key, g.Count(), g.Sum(y => (long)y.Amount), Ratio(g.Count(), records.Length)))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Method)
            .ToArray();

    private static double Ratio(long numerator, long denominator) =>
        denominator <= 0 ? 0d : Round(numerator / (double)denominator);

    private static double Round(double value) => Math.Round(value, 3);
}
