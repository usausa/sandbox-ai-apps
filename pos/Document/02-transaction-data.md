# PosChecker 設計書 ② トランザクションデータ構造（LLM 入力）

LLM に喰わせるトランザクションデータの構造設計。**入力 CSV（生ログ）→ C# レコード → LLM ペイロード** の 3 段で定義する。

## 1. 設計方針

- 1 取引 = **取引ヘッダ単位の 1 行**。商品明細はアプリでは扱わず、`Amount`（取引金額）・`ItemCount`（点数）・`DiscountAmount`（値引き）に集約する。これにより inspector と同じくフラットな CSV を保つ。
- **金額は常に非負**で持ち、入出金の方向は `Type`（取引種別）で表す。集計時に Void / Return をマイナス寄与として扱う。
- **`Amount` は商品総額（グロス）** と定義する。値引き・ポイント利用を差し引く前の金額。これにより「本来受け取るべき現金」を後段で復元できる（§6）。
- **ポイントは 1 ポイント = 1 円等価**と仮定し、**利用（Redeemed）／付与（Earned）** を独立カラムで持つ。ポイント利用は値引きと同じく支払いを圧縮するが、**原資（販促負債）と現金フローが値引きと異なり、現金着服の手段になりうる**ため、会員 ID と組み合わせて利用側・付与側の両方を検出対象にする。
- 取消・返品は **元取引（`OriginalTransactionId`）** を参照できるようにし、シーケンス異常・繰り返し返金・ポイント利用直後の取消の検出に使う。
- 1 ファイル = 従業員 1 名分・複数営業日。`CashierId` は全行同一を想定するが、ローダーでは検証のみ行い構造上は複数も許容する（将来拡張用）。

## 2. 入力 CSV フォーマット

UTF-8・ヘッダ付き。1 行 = 1 取引。

```csv
BusinessDate,CashierId,TransactionId,Time,Type,Amount,ItemCount,PaymentMethod,DiscountAmount,PointsEarned,PointsRedeemed,MembershipId,OriginalTransactionId,HasReceipt
2026-05-01,E001,T0001,09:12:33,Sale,1280,3,Cash,0,12,0,M1001,,
2026-05-01,E001,T0002,09:15:02,Sale,640,1,Credit,0,6,0,,,
2026-05-01,E001,T0003,09:21:48,Void,640,1,Credit,0,0,0,,T0002,
2026-05-01,E001,T0048,16:30:11,Sale,1000,2,Cash,0,0,300,M9001,,
2026-05-01,E001,T0050,17:44:10,Return,3980,2,Cash,0,0,0,,T0031,true
2026-05-01,E001,T0051,17:46:55,Return,4200,1,Cash,0,0,0,,,false
```

### 2.1 カラム定義

| 列名 | 型 | 必須 | 説明 |
|---|---|---|---|
| `BusinessDate` | date (yyyy-MM-dd) | ○ | 営業日。**日別判定単位のキー**。 |
| `CashierId` | string | ○ | レジ担当者（従業員）ID。 |
| `TransactionId` | string | ○ | 取引番号（ファイル内一意）。 |
| `Time` | time (HH:mm:ss) | ○ | 取引時刻。時間帯分布・シーケンス判定に使用。 |
| `Type` | enum | ○ | 取引種別（§3）。 |
| `Amount` | int (円) | ○ | **商品総額（グロス、非負）**。値引き・ポイント利用を引く前。NoSale は 0。 |
| `ItemCount` | int | ○ | 商品点数。NoSale/一部 Void は 0 可。 |
| `PaymentMethod` | enum | ○ | 支払・返金手段（§4）。NoSale は `Other`。 |
| `DiscountAmount` | int (円) | ○ | 値引き額（非負、無ければ 0）。 |
| `PointsEarned` | int | ○ | 付与ポイント（無ければ 0、1pt=1円）。 |
| `PointsRedeemed` | int | ○ | 利用ポイント（無ければ 0、1pt=1円）。支払いを圧縮する。 |
| `MembershipId` | string | 空可 | 会員 ID。非会員は空。**ポイント付与・利用は本来この会員に帰属**。 |
| `OriginalTransactionId` | string | 空可 | 元取引番号（Void / Return 時）。 |
| `HasReceipt` | bool | 空可 | 返品時のレシート有無（`true`/`false`）。Sale 等は空。 |

### 2.2 バリデーション（ローダー）

inspector の `SurveyCsvLoader` に倣い、行ごとに検証して `FormatException` を投げる。

- `BusinessDate` / `Time` は厳密パース（`CultureInfo.InvariantCulture`）。
- `Type` / `PaymentMethod` は enum パース（不明値はエラー）。
- `Amount` / `ItemCount` / `Discount` / `Points*` は非負整数。
- **`MembershipId` 空かつ `PointsRedeemed>0`（または `PointsEarned>0`）は論理矛盾**だが、エラーにはせず「会員不在のポイント操作」として強い不正シグナルに回す（§03）。
- `Void` / `Return` は `OriginalTransactionId` を持つことを推奨（無い場合は「元取引なし」として弱い不正シグナルに回し、エラーにはしない）。
- 0 行はエラー。読み込み順を保持するため `Sequence`（連番）を付与（inspector と同じ）。

## 3. 取引種別 `Type`（enum）

| 値 | 日本語 | 金額の意味 | 不正観点での主な役割 |
|---|---|---|---|
| `Sale` | 売上 | 入金 | 基準となる正常取引。母数。ポイント利用・値引きの器。 |
| `Void` | 取消 | 売上の取り消し | 売上直後の取消で現金を抜く。率・直後性で判定。 |
| `Return` | 返品 | 返金（出金） | 架空・水増し返品。レシート無・高額・繰り返しで判定。 |
| `NoSale` | レジ開放 | 0 | 売上を伴わないドロワー開放。多発で判定。 |

> 値引きは独立種別にせず `Sale` の属性 `DiscountAmount` として持つ（過剰値引きは集計で別途検出）。将来 `ManualDiscount`・`PriceOverride` を追加する余地を残す。

## 4. 支払・返金手段 `PaymentMethod`（enum）

`Cash` / `Credit` / `QR` / `GiftCard` / `Other`

- 現金返金（`Return` × `Cash`）は最も現金化しやすく、重点監視対象。
- **ポイント利用 × 現金売上**（`PointsRedeemed>0` × `Cash`）は、他人のポイントで支払いを圧縮し差額の現金を抜く手口に直結するため重点監視対象（§03）。
- クレジット売上を現金で返金、などの **支払方法ミスマッチ** も後続で検出可能にする。

## 5. C# 入力モデル

```csharp
public enum TransactionType { Sale, Void, Return, NoSale }

public enum PaymentMethod { Cash, Credit, QR, GiftCard, Other }

public sealed record TransactionRecord(
    DateOnly BusinessDate,
    string CashierId,
    string TransactionId,
    TimeOnly Time,
    TransactionType Type,
    int Amount,                 // 商品総額（グロス）
    int ItemCount,
    PaymentMethod PaymentMethod,
    int DiscountAmount,
    int PointsEarned,
    int PointsRedeemed,
    string? MembershipId,
    string? OriginalTransactionId,
    bool? HasReceipt,
    int Sequence);
```

## 6. ポイント・値引きと現金の整合（着服検出の土台）

`Amount` をグロスと定義したことで、**本来ドロワーに入るべき現金**を機械的に復元できる。

```text
expectedCash(Sale) = Amount − DiscountAmount − PointsRedeemed      （Cash 売上の場合）
```

- 正常：顧客が自分のポイントを使えば `PointsRedeemed>0` で現金が減るのは当然（不正ではない）。
- 着服の核心は「**誰のポイントが、どの取引で使われ、差額の現金がどこへ行くか**」。よって金額の辻褄ではなく、**会員 ID の偏り・会員不在の利用・現金への集中・利用直後の取消**をシグナルにする（具体式は §03）。

## 7. LLM ペイロード構造

`PosFraudAnalyzer` が `IChatClient` に渡す JSON。inspector 同様 **camelCase**・`WriteIndented` で整形し、システムプロンプトの後にユーザープロンプトとして添付する。集計（要約）と生取引の両方を渡す。

```jsonc
{
  "datasetSummary": {
    "recordCount": 312,
    "cashierId": "E001",
    "startDate": "2026-05-01",
    "endDate": "2026-05-14",
    "businessDayCount": 14,
    "salesCount": 268, "salesAmount": 412300,
    "voidCount": 9, "voidAmount": 7820,
    "returnCount": 11, "returnAmount": 39800,
    "noSaleCount": 4,
    "voidRatio": 0.033,
    "returnAmountRatio": 0.096,
    "noReceiptReturnRatio": 0.18,
    "pointsEarnedTotal": 3120,
    "pointsRedeemedTotal": 900,
    "nonMemberPointsEarnedCount": 0,
    "nonMemberPointsRedeemedCount": 0,
    "pointsRedeemedCashCount": 1
  },
  "typeDistribution": [
    { "type": "Sale",   "count": 268, "amount": 412300, "ratio": 0.859 },
    { "type": "Return", "count": 11,  "amount": 39800,  "ratio": 0.035 },
    { "type": "Void",   "count": 9,   "amount": 7820,   "ratio": 0.029 },
    { "type": "NoSale", "count": 4,   "amount": 0,      "ratio": 0.013 }
  ],
  "paymentDistribution": [
    { "method": "Cash",   "count": 150, "amount": 210000, "ratio": 0.48 },
    { "method": "Credit", "count": 130, "amount": 202300, "ratio": 0.46 }
  ],
  "dailySummaries": [
    {
      "businessDate": "2026-05-03",
      "transactionCount": 41,
      "salesCount": 34, "salesAmount": 52100,
      "voidCount": 3,  "voidAmount": 2980,  "voidRatio": 0.088,
      "returnCount": 4, "returnAmount": 18600, "returnRatio": 0.357,
      "noReceiptReturnCount": 3, "noReceiptReturnRatio": 0.75,
      "cashReturnAmount": 18600,
      "noSaleCount": 2,
      "discountAmount": 1200, "discountRatio": 0.023,
      "pointsRedeemed": 300, "pointsEarned": 410,
      "nonMemberPointsEarnedCount": 0,
      "nonMemberPointsRedeemedCount": 1,
      "pointsRedeemedCashCount": 1,
      "redeemMembershipConcentration": 0.0,
      "topRedeemMembershipId": "M9001",
      "afterHoursCount": 1,
      "saleThenVoidCount": 2,
      "repeatedRefundAmountCount": 2,
      "topReturnMembershipId": "M2007"
    }
    // …営業日ごとに1件（= 判定単位）
  ],
  "sequenceAnomalies": [
    { "date": "2026-05-03", "kind": "SaleThenVoid", "transactionId": "T0188", "originalTransactionId": "T0187", "amount": 1980, "secondsApart": 36 },
    { "date": "2026-05-03", "kind": "RepeatedRefundAmount", "amount": 4200, "occurrences": 3 },
    { "date": "2026-05-03", "kind": "PointsRedeemThenVoid", "transactionId": "T0190", "amount": 1000, "secondsApart": 48 }
  ],
  "rawRows": [
    { "businessDate": "2026-05-01", "time": "09:12:33", "transactionId": "T0001", "type": "Sale", "amount": 1280, "itemCount": 3, "paymentMethod": "Cash", "discountAmount": 0, "pointsEarned": 12, "pointsRedeemed": 0, "membershipId": "M1001", "originalTransactionId": null, "hasReceipt": null }
    // …全取引
  ]
}
```

- `dailySummaries` の各要素が **「1 営業日 × 1 従業員」= 1 判定単位**で、LLM の `daily_results[]` と日付で対応づく。
- 各特徴量の算出式は `03-fraud-rules.md` で定義する。
- `rawRows` を渡すのは inspector と同じく、LLM が要約だけでなく原データのパターン（時刻の連続性・同額の並び・同一会員 ID のポイント利用反復）も読めるようにするため。データ量が大きい場合は「件数上限・要約優先」を `04` の実装メモで扱う。
