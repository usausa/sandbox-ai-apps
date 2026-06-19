#!/usr/bin/env python3
"""C# の PosFeatureSummaryBuilder と同じ集計＋ pos_analyzer.txt を Python で再現し、
各サンプルセットを Foundry に POST して overall_score と担当者スコアの分離を確認する。

APIキーは環境変数 FOUNDRY_API_KEY からのみ読む(コミット・出力しない)。

  $env:FOUNDRY_API_KEY="<APIキー>"; python tools/validate_foundry.py; Remove-Item Env:\FOUNDRY_API_KEY
"""

import csv
import json
import os
import sys
from collections import defaultdict

import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SAMPLES_DIR = os.path.join(ROOT, "PosChecker", "wwwroot", "samples")
PROMPT_PATH = os.path.join(ROOT, "PosChecker", "Prompts", "pos_analyzer.txt")

ENDPOINT = "https://foundry-usausa-resource.services.ai.azure.com"
DEPLOYMENT = "gpt-5.4-mini"
API_VERSION = "2024-12-01-preview"

# しきい値 (PosCheckerSettings の既定値)
SHORT_INTERVAL_SECONDS = 300
MEMBER_CONCENTRATION_THRESHOLD = 0.3
HIGH_RETURN_RATIO = 0.1
HIGH_REKEY_RATIO = 0.1
REPEATED_COUPON_MIN = 3
SAME_ITEM_MIN = 2
BIZ_START, BIZ_END = 9, 21
MIN_SUSPICIOUS = 3
MEMBER_CAP = 50

SETS = ["normal", "fraud-point", "fraud-cart", "fraud-coupon", "fraud-return", "fraud-rekey"]
MEMBER_LIMITED = {"SpecifiedMembers", "CreditMembers", "RakutenMembers", "SdAndRakuten", "SdNotRakuten"}


def rnd(num, den):
    return round(num / den, 3) if den > 0 else 0.0


def read_csv(path):
    with open(path, newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def to_seconds(hhmmss):
    h, m, s = (int(x) for x in hhmmss.split(":"))
    return h * 3600 + m * 60 + s


def opt(v):
    return v if v else None


def load(set_name):
    base = os.path.join(SAMPLES_DIR, set_name)
    headers = read_csv(os.path.join(base, "SalesHeader.csv"))
    details = read_csv(os.path.join(base, "SalesDetail.csv"))
    promos = read_csv(os.path.join(base, "Promotion.csv"))

    det_by_key = defaultdict(list)
    for d in details:
        det_by_key[(d["StoreCode"], d["SalesDate"], d["PosNo"], d["SalesNo"])].append(d)

    txns = []
    for i, h in enumerate(headers):
        key = (h["StoreCode"], h["SalesDate"], h["PosNo"], h["SalesNo"])
        txns.append({"h": h, "d": det_by_key.get(key, []), "seq": i})
    return txns, promos


def is_same_item_multibuy(det):
    if not det:
        return False
    jan_lines = defaultdict(int)
    for d in det:
        jan_lines[d["Jancode"]] += 1
    if any(v >= SAME_ITEM_MIN for v in jan_lines.values()):
        return True
    return any(int(d["Quantity"]) >= SAME_ITEM_MIN + 1 for d in det)


def build_payload(set_name):
    txns, promos = load(set_name)
    effective = [t for t in txns if t["h"]["RegisterType"] == "Normal"]
    signals = []

    # 担当者集計
    by_cashier = defaultdict(list)
    for t in effective:
        by_cashier[(t["h"]["StoreCode"], t["h"]["CashierCode"])].append(t)

    cashier_summaries = []
    for (store, cashier), recs in sorted(by_cashier.items()):
        name = recs[0]["h"]["CashierName"]
        sales = [r for r in recs if r["h"]["TransactionType"] == "Sale"]
        returns = [r for r in recs if r["h"]["TransactionType"] == "Return"]
        rekeys = [r for r in recs if r["h"]["ProcessType"] == "Rekey"]
        return_ratio = rnd(len(returns), len(recs))
        rekey_ratio = rnd(len(rekeys), len(recs))
        return_amount = sum(int(r["h"]["TotalAmount"]) for r in returns)

        member_sales = [r for r in sales if r["h"]["MemberCode"]]
        mcount = defaultdict(int)
        for r in member_sales:
            mcount[r["h"]["MemberCode"]] += 1
        top_member, top_member_count = (None, 0)
        if mcount:
            top_member = sorted(mcount.items(), key=lambda kv: (-kv[1], kv[0]))[0][0]
            top_member_count = mcount[top_member]
        member_conc = rnd(top_member_count, len(member_sales))

        same_item = sum(1 for r in sales if is_same_item_multibuy(r["d"]))

        ordered = sorted(
            [r for r in sales if r["h"]["MemberCode"]],
            key=lambda r: (r["h"]["SalesDate"], to_seconds(r["h"]["SystemTime"]), r["seq"]))
        short_interval = 0
        for i in range(1, len(ordered)):
            p, c = ordered[i - 1]["h"], ordered[i]["h"]
            if p["SalesDate"] == c["SalesDate"] and p["MemberCode"] == c["MemberCode"]:
                gap = to_seconds(c["SystemTime"]) - to_seconds(p["SystemTime"])
                if 0 <= gap <= SHORT_INTERVAL_SECONDS:
                    short_interval += 1

        after_hours = sum(
            1 for r in recs
            if (r["h"]["TransactionType"] == "Return" or r["h"]["ProcessType"] == "Rekey")
            and (int(r["h"]["SystemTime"][:2]) < BIZ_START or int(r["h"]["SystemTime"][:2]) >= BIZ_END))

        if top_member and top_member_count >= MIN_SUSPICIOUS and member_conc >= MEMBER_CONCENTRATION_THRESHOLD:
            signals.append({"kind": "CashierMemberConcentration", "storeCode": store, "cashierCode": cashier,
                            "memberCode": top_member, "detail": f"会員{top_member}が担当売上の{member_conc*100:.1f}%を占有",
                            "count": top_member_count, "ratio": member_conc})
        if same_item >= MIN_SUSPICIOUS:
            signals.append({"kind": "SameItemMultiBuy", "storeCode": store, "cashierCode": cashier,
                            "memberCode": None, "detail": f"同一JAN/用途を複数購入した会計が{same_item}件",
                            "count": same_item, "ratio": None})
        if short_interval >= MIN_SUSPICIOUS:
            signals.append({"kind": "ShortIntervalSameMember", "storeCode": store, "cashierCode": cashier,
                            "memberCode": None, "detail": f"同一会員の短時間連続会計が{short_interval}件",
                            "count": short_interval, "ratio": None})
        if len(returns) >= MIN_SUSPICIOUS and return_ratio >= HIGH_RETURN_RATIO:
            signals.append({"kind": "HighReturnCashier", "storeCode": store, "cashierCode": cashier,
                            "memberCode": None, "detail": f"返品{len(returns)}件・返品率{return_ratio*100:.1f}%",
                            "count": len(returns), "ratio": return_ratio})
        if len(rekeys) >= MIN_SUSPICIOUS and rekey_ratio >= HIGH_REKEY_RATIO:
            signals.append({"kind": "HighRekeyCashier", "storeCode": store, "cashierCode": cashier,
                            "memberCode": None, "detail": f"打直{len(rekeys)}件・打直率{rekey_ratio*100:.1f}%",
                            "count": len(rekeys), "ratio": rekey_ratio})

        cashier_summaries.append({
            "storeCode": store, "cashierCode": cashier, "cashierName": name,
            "transactionCount": len(recs), "salesCount": len(sales), "returnCount": len(returns),
            "returnRatio": return_ratio, "returnAmount": return_amount,
            "rekeyCount": len(rekeys), "rekeyRatio": rekey_ratio,
            "distinctMemberCount": len(mcount), "topMemberCode": top_member,
            "topMemberSalesCount": top_member_count, "memberConcentration": member_conc,
            "sameItemRepeatCount": same_item, "shortIntervalSameMemberCount": short_interval,
            "afterHoursReturnRekeyCount": after_hours})

    # 会員集計
    sales_by_member = defaultdict(list)
    for t in effective:
        if t["h"]["TransactionType"] == "Sale" and t["h"]["MemberCode"]:
            sales_by_member[t["h"]["MemberCode"]].append(t)
    promos_by_member = defaultdict(list)
    for p in promos:
        if p["ScannedMemberCode"]:
            promos_by_member[p["ScannedMemberCode"]].append(p)

    member_summaries = []
    for member in set(sales_by_member) | set(promos_by_member):
        sales = sales_by_member.get(member, [])
        mpromos = promos_by_member.get(member, [])
        ccount = defaultdict(int)
        for r in sales:
            ccount[r["h"]["CashierCode"]] += 1
        top_cashier = sorted(ccount.items(), key=lambda kv: (-kv[1], kv[0]))[0][0] if ccount else None
        cashier_conc = rnd(max(ccount.values()) if ccount else 0, len(sales))
        coupon_groups = defaultdict(list)
        for p in mpromos:
            coupon_groups[p["CouponCode"]].append(p)
        repeated = sum(1 for g in coupon_groups.values() if len(g) >= REPEATED_COUPON_MIN)
        limited = sum(1 for p in mpromos if p["MemberTargetType"] in MEMBER_LIMITED)
        app = sum(1 for p in mpromos if "アプリ" in p["CouponName"] or "アプリ" in p["PlanName"])
        member_summaries.append({
            "memberCode": member, "salesCount": len(sales), "distinctCashierCount": len(ccount),
            "topCashierCode": top_cashier, "cashierConcentration": cashier_conc,
            "couponUseCount": len(mpromos), "repeatedCouponCount": repeated,
            "memberLimitedCouponCount": limited, "appCouponCount": app})
        for code, g in coupon_groups.items():
            if len(g) >= REPEATED_COUPON_MIN:
                tag = "会員限定" if g[0]["MemberTargetType"] in MEMBER_LIMITED else (
                    "アプリ" if ("アプリ" in g[0]["CouponName"] or "アプリ" in g[0]["PlanName"]) else "一般")
                signals.append({"kind": "RepeatedCouponByMember", "storeCode": g[0]["StoreCode"],
                                "cashierCode": None, "memberCode": member,
                                "detail": f"クーポン{code}({tag})を{len(g)}回利用", "count": len(g), "ratio": None})

    signal_members = {s["memberCode"] for s in signals if s["memberCode"]}
    member_summaries = [m for m in member_summaries if m["memberCode"] in signal_members]
    member_summaries.sort(key=lambda m: (-m["repeatedCouponCount"], -m["cashierConcentration"],
                                         -m["salesCount"], m["memberCode"]))
    member_summaries = member_summaries[:MEMBER_CAP]
    signals.sort(key=lambda s: (s["kind"], s["storeCode"], s["cashierCode"] or "", s["memberCode"] or ""))

    # 全体
    all_txns = txns
    sales_all = [t for t in all_txns if t["h"]["TransactionType"] == "Sale"]
    returns_all = [t for t in all_txns if t["h"]["TransactionType"] == "Return"]
    n = len(all_txns)

    def dist(group_key):
        groups = defaultdict(list)
        for t in all_txns:
            groups[group_key(t)].append(t)
        rows = []
        for k, g in groups.items():
            rows.append((k, len(g), sum(int(x["h"]["TotalAmount"]) for x in g), rnd(len(g), n)))
        rows.sort(key=lambda r: (-r[1], r[0]))
        return rows

    type_dist = [{"type": k, "count": c, "amount": a, "ratio": r}
                 for k, c, a, r in dist(lambda t: t["h"]["TransactionType"])]
    tender_dist = [{"tender": k, "count": c, "amount": a, "ratio": r}
                   for k, c, a, r in dist(lambda t: t["h"]["TenderType"])]

    dates = sorted(t["h"]["SalesDate"] for t in all_txns)
    dataset_summary = {
        "transactionCount": n,
        "detailCount": sum(len(t["d"]) for t in all_txns),
        "promotionCount": len(promos),
        "storeCount": len({t["h"]["StoreCode"] for t in all_txns}),
        "cashierCount": len({(t["h"]["StoreCode"], t["h"]["CashierCode"]) for t in all_txns}),
        "memberCount": len({t["h"]["MemberCode"] for t in all_txns if t["h"]["MemberCode"]}),
        "startDate": dates[0], "endDate": dates[-1],
        "salesCount": len(sales_all), "salesAmount": sum(int(t["h"]["TotalAmount"]) for t in sales_all),
        "returnCount": len(returns_all), "returnAmount": sum(int(t["h"]["TotalAmount"]) for t in returns_all),
        "rekeyCount": sum(1 for t in all_txns if t["h"]["ProcessType"] == "Rekey")}

    raw_promos = [{
        "storeCode": p["StoreCode"], "salesDate": p["SalesDate"], "posNo": int(p["PosNo"]),
        "slipNo": int(p["SlipNo"]), "planCode": p["PlanCode"], "planName": p["PlanName"],
        "couponCode": p["CouponCode"], "scannedMemberCode": opt(p["ScannedMemberCode"]),
        "couponJan": p["CouponJan"], "issueType": p["IssueType"], "couponName": p["CouponName"],
        "startDate": opt(p["StartDate"]), "endDate": opt(p["EndDate"]),
        "memberTargetType": p["MemberTargetType"]} for p in promos]

    return {
        "datasetSummary": dataset_summary,
        "typeDistribution": type_dist,
        "tenderDistribution": tender_dist,
        "cashierSummaries": cashier_summaries,
        "memberSummaries": member_summaries,
        "fraudSignals": signals,
        "rawPromotions": raw_promos,
    }


def build_user_prompt(payload):
    preamble = (
        "以下は小売店の複数店舗・複数担当者・複数会員のPOSトランザクションを要約したものです。\n"
        "判定は「店舗×担当者」を主軸に、ポイント不正・クーポン不正は会員も評価してください。\n"
        "取引種別は 売上(Sale)/返品(Return)、処理区分には打ち直し(Rekey)が含まれます。\n"
        "次の特徴は不正一覧に基づく強い不正シグナルです:\n"
        "- 担当者の売上が特定会員コードに偏る/同一会員の短時間連続会計(ポイント不正付与: PointAbuse)\n"
        "- 同一JAN・同一用途を複数購入する会計を頻発する担当者(かご抜け: CartBypass)\n"
        "- 同一会員が同一クーポンを反復スキャン、会員限定/アプリクーポンの配信期間と乖離(クーポン不正: CouponAbuse)\n"
        "- 担当者の返品件数・返品率が他より高い、時間帯に偏る(フリー返品不正: ReturnFraud)\n"
        "- 担当者の打直件数・打直率が他より高い、時間帯に偏る(打ち直し不正: RekeyFraud)\n\n"
        "件数の絶対値ではなく、他担当者との比率の偏り・反復・時間帯の不自然さで判断してください。\n"
        "JSONのみで回答してください。\n\n入力データ:\n")
    return preamble + json.dumps(payload, ensure_ascii=False, indent=2)


def call_foundry(system_prompt, user_prompt, api_key):
    url = f"{ENDPOINT}/openai/deployments/{DEPLOYMENT}/chat/completions?api-version={API_VERSION}"
    body = json.dumps({
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": user_prompt},
        ],
        "temperature": 0,
        "response_format": {"type": "json_object"},
        "max_completion_tokens": 4000,
    }).encode("utf-8")
    req = urllib.request.Request(url, data=body, method="POST")
    req.add_header("Content-Type", "application/json")
    req.add_header("api-key", api_key)
    with urllib.request.urlopen(req, timeout=120) as resp:
        data = json.loads(resp.read().decode("utf-8"))
    content = data["choices"][0]["message"]["content"]
    usage = data.get("usage", {})
    return json.loads(content), usage


def main():
    api_key = os.environ.get("FOUNDRY_API_KEY")
    if not api_key:
        print("FOUNDRY_API_KEY が未設定です。", file=sys.stderr)
        sys.exit(1)

    with open(PROMPT_PATH, encoding="utf-8") as f:
        system_prompt = f.read().strip()

    print(f"{'set':<14} overall  top-cashier-scores")
    print("-" * 70)
    for name in SETS:
        payload = build_payload(name)
        user_prompt = build_user_prompt(payload)
        result, usage = call_foundry(system_prompt, user_prompt, api_key)
        overall = result.get("overall_score")
        cashiers = sorted(result.get("cashier_results", []), key=lambda c: -c.get("score", 0))[:3]
        tops = ", ".join(f"{c['store_code']}/{c['cashier_code']}={c['score']}" for c in cashiers)
        members = [m for m in result.get("member_results", []) if m.get("score", 0) >= 60]
        mtops = ", ".join(f"{m['member_code']}={m['score']}" for m in members[:3])
        print(f"{name:<14} {overall:>5}   {tops}")
        if mtops:
            print(f"{'':<14}         members: {mtops}")
        print(f"{'':<14}         tokens: prompt={usage.get('prompt_tokens')} completion={usage.get('completion_tokens')}")


if __name__ == "__main__":
    main()
