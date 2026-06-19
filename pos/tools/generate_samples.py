#!/usr/bin/env python3
"""PosChecker のデモ用サンプルCSV(3ビュー)を生成する。

出力: pos/PosChecker/wwwroot/samples/<set>/{SalesHeader,SalesDetail,Promotion}.csv
セット: normal / fraud-point / fraud-cart / fraud-coupon / fraud-return / fraud-rekey

不正一覧の5事象を、特定の担当者/会員に注入して再現する。乱数シードを固定し再現可能。
"""

import csv
import os
import random
from datetime import date, time, timedelta

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SAMPLES_DIR = os.path.join(ROOT, "PosChecker", "wwwroot", "samples")

STORES = ["0001", "0002"]
CASHIERS = {
    "1001": "佐藤一郎",
    "1002": "鈴木二郎",
    "1003": "高橋三郎",
    "1004": "田中四郎",
    "1005": "渡辺五郎",
}
MEMBERS = [f"M{1000 + i:04d}" for i in range(40)]  # M1000..M1039
STAFF_CARD = "M9001"  # 不正店員が使う自分のカード

# (Jancode, 商品名, 用途コード, 用途名, 標準価格)
PRODUCTS = [
    ("4901001000017", "牛乳1L", "01", "食品", 220),
    ("4901001000024", "食パン", "01", "食品", 150),
    ("4901001000031", "卵10個", "01", "食品", 240),
    ("4902002000018", "ポテトチップス", "03", "菓子", 130),
    ("4902002000025", "チョコレート", "03", "菓子", 200),
    ("4903003000019", "緑茶500ml", "04", "飲料", 140),
    ("4903003000026", "コーラ500ml", "04", "飲料", 160),
    ("4904004000010", "ビール350ml", "05", "酒類", 230),
    ("4904004000027", "缶チューハイ", "05", "酒類", 180),
    ("4905005000011", "洗剤", "02", "日用品", 320),
    ("4905005000028", "ティッシュ5箱", "02", "日用品", 360),
    ("4905005000035", "歯ブラシ", "02", "日用品", 280),
]

# (CouponCode, PlanCode, PlanName, CouponName, CouponJan, IssueType, MemberTargetType, StartOffset, EndOffset)
COUPONS = [
    ("C1001", "9001", "5月会員限定セール", "会員限定10%OFF", "2900000010019", "DM", "SpecifiedMembers", 0, 13),
    ("C1002", "9002", "アプリ配信クーポン", "アプリ限定50円引", "2900000010026", "Other", "AllMembers", 0, 13),
    ("C1003", "9003", "レシートクーポン", "次回30円引", "2900000010033", "Receipt", "None", 0, 6),
    ("C1004", "9004", "チラシ特売", "チラシ100円引", "2900000010040", "Flyer", "AllMembers", 0, 13),
]

START_DATE = date(2026, 5, 1)
DAYS = 7


def biz_time(rng, hour_min=9, hour_max=20):
    h = rng.randint(hour_min, hour_max)
    m = rng.randint(0, 59)
    s = rng.randint(0, 59)
    return time(h, m, s)


class Builder:
    def __init__(self, seed):
        self.rng = random.Random(seed)
        self.headers = []
        self.details = []
        self.promotions = []
        self.sales_no = {s: 0 for s in STORES}
        self.slip_no = {s: 0 for s in STORES}

    def next_sales_no(self, store):
        self.sales_no[store] += 1
        return self.sales_no[store]

    def next_slip_no(self, store):
        self.slip_no[store] += 1
        return self.slip_no[store]

    def add_sale(self, store, day, cashier, *, member=None, tx_type="Sale",
                 process="Normal", tender=None, t=None, n_lines=None,
                 force_product=None, force_qty=None, points=0):
        rng = self.rng
        sales_no = self.next_sales_no(store)
        pos_no = rng.randint(1, 3)
        if t is None:
            t = biz_time(rng)
        if tender is None:
            tender = rng.choices(["Cash", "Credit", "GiftCard"], weights=[55, 30, 15])[0]
        settlement = ""
        account = ""
        if tender == "Credit":
            settlement = rng.choice(["Credit", "EMoney", "BarcodePay", "UnionPay"])
            account = str(rng.randint(10**11, 10**12 - 1))

        if n_lines is None:
            n_lines = rng.randint(1, 4)

        total = 0
        lines = []
        # 正常会計は異なる商品を購入する（同一JANの複数明細は不正シグナル側で表現する）。
        normal_products = rng.sample(PRODUCTS, k=min(n_lines, len(PRODUCTS)))
        for i in range(n_lines):
            prod = force_product if force_product else normal_products[i % len(normal_products)]
            qty = force_qty if force_qty else rng.choices([1, 2, 3], weights=[78, 18, 4])[0]
            unit = prod[4] + rng.choice([0, 0, 0, 10, 20])
            line_amount = unit * qty
            total += line_amount
            lines.append((prod, unit, qty, line_amount))

        self.headers.append({
            "StoreCode": store,
            "SalesDate": day.isoformat(),
            "PosNo": pos_no,
            "SalesNo": sales_no,
            "TransactionType": tx_type,
            "ProcessType": process,
            "RegisterType": "Normal",
            "CashierCode": cashier,
            "CashierName": CASHIERS[cashier],
            "MemberCode": member or "",
            "SystemTime": t.strftime("%H:%M:%S"),
            "TenderType": tender,
            "SettlementType": settlement,
            "AccountNumber": account,
            "PointsUsed": points,
            "TotalAmount": total,
        })
        for prod, unit, qty, line_amount in lines:
            self.details.append({
                "StoreCode": store,
                "SalesDate": day.isoformat(),
                "PosNo": pos_no,
                "SalesNo": sales_no,
                "Jancode": prod[0],
                "ProductName": prod[1],
                "UsageCode": prod[2],
                "UsageName": prod[3],
                "UnitPrice": unit,
                "Quantity": qty,
                "LineAmount": line_amount,
            })
        return store, day, pos_no, sales_no, member

    def add_promotion(self, store, day, member, coupon):
        slip_no = self.next_slip_no(store)
        pos_no = self.rng.randint(1, 3)
        code, plan, plan_name, coupon_name, jan, issue, target, so, eo = coupon
        self.promotions.append({
            "StoreCode": store,
            "SalesDate": day.isoformat(),
            "PosNo": pos_no,
            "SlipNo": slip_no,
            "PlanCode": plan,
            "PlanName": plan_name,
            "CouponCode": code,
            "ScannedMemberCode": member or "",
            "CouponJan": jan,
            "IssueType": issue,
            "CouponName": coupon_name,
            "StartDate": (START_DATE + timedelta(days=so)).isoformat(),
            "EndDate": (START_DATE + timedelta(days=eo)).isoformat(),
            "MemberTargetType": target,
        })

    def generate_baseline(self):
        """正常の土台。返品/打直は低率、ポイント/クーポンは自然。"""
        rng = self.rng
        for d in range(DAYS):
            day = START_DATE + timedelta(days=d)
            for store in STORES:
                for cashier in CASHIERS:
                    n = rng.randint(7, 11)
                    for _ in range(n):
                        roll = rng.random()
                        member = rng.choice(MEMBERS) if rng.random() < 0.55 else None
                        if roll < 0.04:
                            self.add_sale(store, day, cashier, member=member, tx_type="Return")
                        elif roll < 0.06:
                            self.add_sale(store, day, cashier, member=member, process="Rekey")
                        else:
                            points = rng.choice([0, 0, 0, 0, 100, 200]) if member else 0
                            ref = self.add_sale(store, day, cashier, member=member, points=points)
                            # 会員売上の一部でクーポン利用
                            if member and rng.random() < 0.12:
                                self.add_promotion(store, day, member, rng.choice(COUPONS))


def write_set(name, builder):
    out_dir = os.path.join(SAMPLES_DIR, name)
    os.makedirs(out_dir, exist_ok=True)

    header_cols = ["StoreCode", "SalesDate", "PosNo", "SalesNo", "TransactionType",
                   "ProcessType", "RegisterType", "CashierCode", "CashierName",
                   "MemberCode", "SystemTime", "TenderType", "SettlementType",
                   "AccountNumber", "PointsUsed", "TotalAmount"]
    detail_cols = ["StoreCode", "SalesDate", "PosNo", "SalesNo", "Jancode",
                   "ProductName", "UsageCode", "UsageName", "UnitPrice", "Quantity", "LineAmount"]
    promo_cols = ["StoreCode", "SalesDate", "PosNo", "SlipNo", "PlanCode", "PlanName",
                  "CouponCode", "ScannedMemberCode", "CouponJan", "IssueType",
                  "CouponName", "StartDate", "EndDate", "MemberTargetType"]

    _write(os.path.join(out_dir, "SalesHeader.csv"), header_cols, builder.headers)
    _write(os.path.join(out_dir, "SalesDetail.csv"), detail_cols, builder.details)
    _write(os.path.join(out_dir, "Promotion.csv"), promo_cols, builder.promotions)
    print(f"{name}: headers={len(builder.headers)} details={len(builder.details)} promotions={len(builder.promotions)}")


def _write(path, cols, rows):
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=cols)
        w.writeheader()
        for row in rows:
            w.writerow(row)


def make_normal():
    b = Builder(seed=1)
    b.generate_baseline()
    return b


def make_fraud_point():
    """事象1: 担当者1002(店0001)が自分のカードM9001へ偏って付与・短時間連続会計。"""
    b = Builder(seed=2)
    b.generate_baseline()
    store, cashier = "0001", "1002"
    for d in range(DAYS):
        day = START_DATE + timedelta(days=d)
        # 短時間連続で自分のカードに付与
        base = time(13, 0, 0)
        for k in range(6):
            t = (base.hour * 3600 + base.minute * 60 + k * 90)
            tt = time(t // 3600, (t % 3600) // 60, t % 60)
            b.add_sale(store, day, cashier, member=STAFF_CARD, tender="Cash", t=tt, points=0)
    return b


def make_fraud_cart():
    """事象2: 担当者1003(店0001)が同一JAN/用途の複数購入会計を頻発。"""
    b = Builder(seed=3)
    b.generate_baseline()
    store, cashier = "0001", "1003"
    for d in range(DAYS):
        day = START_DATE + timedelta(days=d)
        for _ in range(5):
            prod = b.rng.choice(PRODUCTS)
            b.add_sale(store, day, cashier, member=None, force_product=prod, force_qty=3, n_lines=2)
    return b


def make_fraud_coupon():
    """事象3: 会員M1007が会員限定/アプリクーポンを反復スキャン。"""
    b = Builder(seed=4)
    b.generate_baseline()
    store, member = "0001", "M1007"
    member_coupon = COUPONS[0]  # 会員限定
    app_coupon = COUPONS[1]     # アプリ
    for d in range(DAYS):
        day = START_DATE + timedelta(days=d)
        b.add_sale(store, day, "1001", member=member)
        b.add_promotion(store, day, member, member_coupon)
        b.add_promotion(store, day, member, app_coupon)
    return b


def make_fraud_return():
    """事象4: 担当者1004(店0001)の返品率が突出、時間帯に偏る。"""
    b = Builder(seed=5)
    b.generate_baseline()
    store, cashier = "0001", "1004"
    for d in range(DAYS):
        day = START_DATE + timedelta(days=d)
        for k in range(6):
            tt = time(20, 30 + k, 0) if 30 + k < 60 else time(21, (30 + k) - 60, 0)
            b.add_sale(store, day, cashier, member=None, tx_type="Return", tender="Cash", t=tt)
    return b


def make_fraud_rekey():
    """事象5: 担当者1005(店0001)の打直率が突出、時間帯に偏る。"""
    b = Builder(seed=6)
    b.generate_baseline()
    store, cashier = "0001", "1005"
    for d in range(DAYS):
        day = START_DATE + timedelta(days=d)
        for k in range(6):
            tt = time(19, 40 + k, 0) if 40 + k < 60 else time(20, (40 + k) - 60, 0)
            b.add_sale(store, day, cashier, member=None, process="Rekey", t=tt)
    return b


def main():
    write_set("normal", make_normal())
    write_set("fraud-point", make_fraud_point())
    write_set("fraud-cart", make_fraud_cart())
    write_set("fraud-coupon", make_fraud_coupon())
    write_set("fraud-return", make_fraud_return())
    write_set("fraud-rekey", make_fraud_rekey())


if __name__ == "__main__":
    main()
