namespace PosChecker.Services;

using PosChecker.Models;
using PosChecker.Settings;

// 不正一覧の5事象に対応する特徴量を、店舗×担当者・会員の軸で決定論的に集計する。
public sealed class PosFeatureSummaryBuilder
{
    // FraudSignal を立てる最小件数（誤検知抑制）。
    private const int MinSuspiciousCount = 3;

    // MemberSummaries に載せる会員数の上限（ペイロード肥大化対策）。
    private const int MemberSummaryCap = 50;

    private readonly PosCheckerSettings settings;

    public PosFeatureSummaryBuilder(PosCheckerSettings settings)
    {
        this.settings = settings;
    }

    public PosFeatureSummary Build(PosDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        if (dataset.Transactions.Count == 0)
        {
            throw new InvalidOperationException("分析対象の取引がありません。");
        }

        var all = dataset.Transactions;
        var effective = settings.ExcludeNonNormalRegister
            ? all.Where(x => x.Header.RegisterType == RegisterType.Normal).ToArray()
            : all.ToArray();

        var signals = new List<FraudSignal>();

        var cashierSummaries = effective
            .GroupBy(x => (x.Header.StoreCode, x.Header.CashierCode))
            .OrderBy(x => x.Key.StoreCode, StringComparer.Ordinal)
            .ThenBy(x => x.Key.CashierCode, StringComparer.Ordinal)
            .Select(group => BuildCashier(group.Key.StoreCode, group.Key.CashierCode, group.ToArray(), signals))
            .ToArray();

        var memberSummaries = BuildMembers(effective, dataset.Promotions, signals);

        var startDate = all.Min(x => x.Header.SalesDate);
        var endDate = all.Max(x => x.Header.SalesDate);

        var sales = all.Where(x => x.Header.TransactionType == TransactionType.Sale).ToArray();
        var returns = all.Where(x => x.Header.TransactionType == TransactionType.Return).ToArray();

        return new PosFeatureSummary(
            all.Count,
            all.Sum(x => x.Details.Count),
            dataset.Promotions.Count,
            all.Select(x => x.Header.StoreCode).Distinct(StringComparer.Ordinal).Count(),
            all.Select(x => (x.Header.StoreCode, x.Header.CashierCode)).Distinct().Count(),
            all.Where(x => x.Header.MemberCode is not null)
                .Select(x => x.Header.MemberCode!)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            startDate,
            endDate,
            sales.Length,
            sales.Sum(x => (long)x.Header.TotalAmount),
            returns.Length,
            returns.Sum(x => (long)x.Header.TotalAmount),
            all.Count(x => x.Header.ProcessType == ProcessType.Rekey),
            BuildTypeDistribution(all),
            BuildTenderDistribution(all),
            cashierSummaries,
            memberSummaries,
            signals
                .OrderBy(x => x.Kind, StringComparer.Ordinal)
                .ThenBy(x => x.StoreCode, StringComparer.Ordinal)
                .ThenBy(x => x.CashierCode, StringComparer.Ordinal)
                .ToArray());
    }

    private CashierFeatureSummary BuildCashier(
        string storeCode,
        string cashierCode,
        SalesTransaction[] records,
        List<FraudSignal> signals)
    {
        var cashierName = records[0].Header.CashierName;
        var transactionCount = records.Length;

        var sales = records.Where(x => x.Header.TransactionType == TransactionType.Sale).ToArray();
        var returns = records.Where(x => x.Header.TransactionType == TransactionType.Return).ToArray();
        var rekeys = records.Where(x => x.Header.ProcessType == ProcessType.Rekey).ToArray();

        var returnRatio = Ratio(returns.Length, transactionCount);
        var rekeyRatio = Ratio(rekeys.Length, transactionCount);

        // 事象1: 担当者が特定会員へ偏る（自カード差し込みの疑い）。
        var memberSales = sales.Where(x => x.Header.MemberCode is not null).ToArray();
        var memberGroups = memberSales
            .GroupBy(x => x.Header.MemberCode!, StringComparer.Ordinal)
            .Select(g => new { Member = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Member, StringComparer.Ordinal)
            .ToArray();

        var distinctMemberCount = memberGroups.Length;
        var topMember = memberGroups.FirstOrDefault();
        var topMemberCode = topMember?.Member;
        var topMemberCount = topMember?.Count ?? 0;
        var memberConcentration = Ratio(topMemberCount, memberSales.Length);

        // 事象2: 同一JAN/用途を複数購入している会計の頻度。
        var sameItemRepeatCount = sales.Count(IsSameItemMultiBuy);

        // 事象1: 同一会員の短時間連続会計。
        var shortIntervalCount = CountShortIntervalSameMember(sales);

        // 事象4・5: 営業時間外の返品・打直。
        var afterHoursCount = records.Count(x =>
            (x.Header.TransactionType == TransactionType.Return || x.Header.ProcessType == ProcessType.Rekey) &&
            (x.Header.SystemTime.Hour < settings.BusinessHoursStartHour ||
             x.Header.SystemTime.Hour >= settings.BusinessHoursEndHour));

        AppendCashierSignals(
            storeCode,
            cashierCode,
            signals,
            returns.Length,
            returnRatio,
            returns.Sum(x => (long)x.Header.TotalAmount),
            rekeys.Length,
            rekeyRatio,
            topMemberCode,
            topMemberCount,
            memberConcentration,
            sameItemRepeatCount,
            shortIntervalCount);

        return new CashierFeatureSummary(
            storeCode,
            cashierCode,
            cashierName,
            transactionCount,
            sales.Length,
            returns.Length,
            returnRatio,
            returns.Sum(x => (long)x.Header.TotalAmount),
            rekeys.Length,
            rekeyRatio,
            distinctMemberCount,
            topMemberCode,
            topMemberCount,
            memberConcentration,
            sameItemRepeatCount,
            shortIntervalCount,
            afterHoursCount);
    }

    private void AppendCashierSignals(
        string storeCode,
        string cashierCode,
        List<FraudSignal> signals,
        int returnCount,
        double returnRatio,
        long returnAmount,
        int rekeyCount,
        double rekeyRatio,
        string? topMemberCode,
        int topMemberCount,
        double memberConcentration,
        int sameItemRepeatCount,
        int shortIntervalCount)
    {
        if (topMemberCode is not null &&
            topMemberCount >= MinSuspiciousCount &&
            memberConcentration >= settings.MemberConcentrationThreshold)
        {
            signals.Add(new FraudSignal(
                "CashierMemberConcentration",
                storeCode,
                cashierCode,
                topMemberCode,
                $"会員{topMemberCode}が担当売上の{Percent(memberConcentration)}を占有",
                topMemberCount,
                memberConcentration));
        }

        if (sameItemRepeatCount >= MinSuspiciousCount)
        {
            signals.Add(new FraudSignal(
                "SameItemMultiBuy",
                storeCode,
                cashierCode,
                null,
                $"同一JAN/用途を複数購入した会計が{sameItemRepeatCount}件",
                sameItemRepeatCount,
                null));
        }

        if (shortIntervalCount >= MinSuspiciousCount)
        {
            signals.Add(new FraudSignal(
                "ShortIntervalSameMember",
                storeCode,
                cashierCode,
                null,
                $"同一会員の短時間連続会計が{shortIntervalCount}件",
                shortIntervalCount,
                null));
        }

        if (returnCount >= MinSuspiciousCount && returnRatio >= settings.HighReturnRatioThreshold)
        {
            signals.Add(new FraudSignal(
                "HighReturnCashier",
                storeCode,
                cashierCode,
                null,
                $"返品{returnCount}件・返品率{Percent(returnRatio)}（返金{returnAmount:N0}円）",
                returnCount,
                returnRatio));
        }

        if (rekeyCount >= MinSuspiciousCount && rekeyRatio >= settings.HighRekeyRatioThreshold)
        {
            signals.Add(new FraudSignal(
                "HighRekeyCashier",
                storeCode,
                cashierCode,
                null,
                $"打直{rekeyCount}件・打直率{Percent(rekeyRatio)}",
                rekeyCount,
                rekeyRatio));
        }
    }

    private bool IsSameItemMultiBuy(SalesTransaction transaction)
    {
        if (transaction.Details.Count == 0)
        {
            return false;
        }

        var min = settings.SameItemMinOccurrences;

        // 同一JANが複数明細に分かれて打たれている（同一商品を分けて購入）。
        var repeatedJan = transaction.Details
            .GroupBy(x => x.Jancode, StringComparer.Ordinal)
            .Any(g => g.Count() >= min);

        if (repeatedJan)
        {
            return true;
        }

        // 1明細で数量がまとまって多い（同一商品の大量購入）。通常購入と区別するため min+1 以上。
        return transaction.Details.Any(x => x.Quantity >= min + 1);
    }

    private int CountShortIntervalSameMember(SalesTransaction[] sales)
    {
        var ordered = sales
            .Where(x => x.Header.MemberCode is not null)
            .OrderBy(x => x.Header.SalesDate)
            .ThenBy(x => x.Header.SystemTime)
            .ThenBy(x => x.Header.Sequence)
            .ToArray();

        var count = 0;
        for (var i = 1; i < ordered.Length; i++)
        {
            var prev = ordered[i - 1].Header;
            var cur = ordered[i].Header;
            if (prev.SalesDate != cur.SalesDate ||
                !string.Equals(prev.MemberCode, cur.MemberCode, StringComparison.Ordinal))
            {
                continue;
            }

            var seconds = (cur.SystemTime.ToTimeSpan() - prev.SystemTime.ToTimeSpan()).TotalSeconds;
            if (seconds >= 0 && seconds <= settings.ShortIntervalSeconds)
            {
                count++;
            }
        }

        return count;
    }

    private MemberFeatureSummary[] BuildMembers(
        SalesTransaction[] effective,
        IReadOnlyList<PromotionRecord> promotions,
        List<FraudSignal> signals)
    {
        var salesByMember = effective
            .Where(x => x.Header.TransactionType == TransactionType.Sale && x.Header.MemberCode is not null)
            .GroupBy(x => x.Header.MemberCode!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);

        var promotionsByMember = promotions
            .Where(x => x.ScannedMemberCode is not null)
            .GroupBy(x => x.ScannedMemberCode!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);

        var memberCodes = salesByMember.Keys
            .Union(promotionsByMember.Keys, StringComparer.Ordinal)
            .ToArray();

        var summaries = new List<MemberFeatureSummary>();

        foreach (var memberCode in memberCodes)
        {
            var sales = salesByMember.TryGetValue(memberCode, out var s) ? s : [];
            var memberPromotions = promotionsByMember.TryGetValue(memberCode, out var p) ? p : [];

            var cashierGroups = sales
                .GroupBy(x => x.Header.CashierCode, StringComparer.Ordinal)
                .Select(g => new { Cashier = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Cashier, StringComparer.Ordinal)
                .ToArray();

            var topCashier = cashierGroups.FirstOrDefault();
            var cashierConcentration = Ratio(topCashier?.Count ?? 0, sales.Length);

            var couponGroups = memberPromotions
                .GroupBy(x => x.CouponCode, StringComparer.Ordinal)
                .ToArray();
            var repeatedCouponCount = couponGroups.Count(g => g.Count() >= settings.RepeatedCouponMinOccurrences);

            summaries.Add(new MemberFeatureSummary(
                memberCode,
                sales.Length,
                cashierGroups.Length,
                topCashier?.Cashier,
                cashierConcentration,
                memberPromotions.Length,
                repeatedCouponCount,
                memberPromotions.Count(x => x.IsMemberLimited),
                memberPromotions.Count(x => x.IsAppCoupon)));

            // 事象3: 同一会員が同一クーポンを反復スキャン。
            foreach (var coupon in couponGroups.Where(g => g.Count() >= settings.RepeatedCouponMinOccurrences))
            {
                var store = coupon.First().StoreCode;
                var limited = coupon.First().IsMemberLimited ? "会員限定" : (coupon.First().IsAppCoupon ? "アプリ" : "一般");
                signals.Add(new FraudSignal(
                    "RepeatedCouponByMember",
                    store,
                    null,
                    memberCode,
                    $"クーポン{coupon.Key}({limited})を{coupon.Count()}回利用",
                    coupon.Count(),
                    null));
            }
        }

        // fraudSignals(担当者の会員偏り / 同一会員のクーポン反復)で裏付けのある会員のみ残し、
        // 母数の小さい偶然の偏りによる誤検知を排除する。
        var signalMembers = signals
            .Where(x => x.MemberCode is not null)
            .Select(x => x.MemberCode!)
            .ToHashSet(StringComparer.Ordinal);

        return summaries
            .Where(x => signalMembers.Contains(x.MemberCode))
            .OrderByDescending(x => x.RepeatedCouponCount)
            .ThenByDescending(x => x.CashierConcentration)
            .ThenByDescending(x => x.SalesCount)
            .ThenBy(x => x.MemberCode, StringComparer.Ordinal)
            .Take(MemberSummaryCap)
            .ToArray();
    }

    private static TransactionTypeBreakdown[] BuildTypeDistribution(IReadOnlyList<SalesTransaction> records) =>
        records
            .GroupBy(x => x.Header.TransactionType)
            .Select(g => new TransactionTypeBreakdown(g.Key, g.Count(), g.Sum(y => (long)y.Header.TotalAmount), Ratio(g.Count(), records.Count)))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Type)
            .ToArray();

    private static TenderBreakdown[] BuildTenderDistribution(IReadOnlyList<SalesTransaction> records) =>
        records
            .GroupBy(x => x.Header.TenderType)
            .Select(g => new TenderBreakdown(g.Key, g.Count(), g.Sum(y => (long)y.Header.TotalAmount), Ratio(g.Count(), records.Count)))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Tender)
            .ToArray();

    private static double Ratio(long numerator, long denominator) =>
        denominator <= 0 ? 0d : Round(numerator / (double)denominator);

    private static double Round(double value) => Math.Round(value, 3);

    private static string Percent(double ratio) => $"{ratio * 100:0.#}%";
}
