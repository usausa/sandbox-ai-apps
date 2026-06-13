# PosChecker 設計書 ③ 不正判定ルール

機械集計の特徴量（決定論）と、LLM に与える判断ルール・プロンプトを定義する。inspector と同じく「機械が特徴量を作り、LLM が説明とスコアを出す」。

## 1. 判定単位と特徴量の階層

- **日別特徴量（`DailyFeatureSummary`）= 1 営業日 × 1 従業員 = 判定単位。**
- 全体特徴量（`PosFeatureSummary`）= ファイル全体（従業員 1 名分・全営業日）の集計＋分布＋シーケンス異常。

すべての比率は 0 除算回避のため分母を `max(n, 1)` とし、`Math.Round(x, 3)`（inspector の `Round` 踏襲）で丸める。ポイントは 1pt=1 円等価で集計する。

## 2. 日別特徴量の算出式

営業日 `d` の取引集合を対象に算出する。`Sales(d)` = 種別 Sale の集合、など。

| 特徴量 | 算出式 | 意図 |
|---|---|---|
| `transactionCount` | 全取引件数 | 規模（繁忙度の代理） |
| `salesCount` / `salesAmount` | `Sales(d)` の件数 / `Amount` 合計 | 母数 |
| `voidCount` / `voidAmount` | `Void(d)` の件数 / 金額合計 | 取消の規模 |
| `voidRatio` | `voidCount / max(salesCount,1)` | 取消の多さ |
| `returnCount` / `returnAmount` | `Return(d)` の件数 / 金額合計 | 返品の規模 |
| `returnRatio` | `returnAmount / max(salesAmount,1)` | 売上に対する返金額の比率（金額ベース） |
| `noReceiptReturnCount` | `Return(d)` かつ `HasReceipt==false` の件数 | レシート無し返品 |
| `noReceiptReturnRatio` | `noReceiptReturnCount / max(returnCount,1)` | 返品の正当性の薄さ |
| `cashReturnAmount` | `Return(d)` かつ `Cash` の `Amount` 合計 | 現金化された返金 |
| `noSaleCount` | `NoSale(d)` の件数 | ドロワー開放 |
| `discountAmount` / `discountRatio` | `Sales(d)` の `DiscountAmount` 合計 / `max(salesAmount,1)` | 値引き乱用 |
| `pointsRedeemed` / `pointsEarned` | 当日合計 | ポイント規模 |
| `nonMemberPointsEarnedCount` | `MembershipId` 空かつ `PointsEarned>0` の件数 | 付与先不明ポイント（付替え疑い） |
| `nonMemberPointsRedeemedCount` | `MembershipId` 空かつ `PointsRedeemed>0` の件数 | **会員不在のポイント利用＝論理矛盾（差し込み疑い）** |
| `pointsRedeemedCashCount` | `PointsRedeemed>0` かつ `Cash` の Sale 件数 | **他人ポイントで現金売上を圧縮→差額着服の温床** |
| `redeemMembershipConcentration` | `PointsRedeemed>0` の Sale における最頻 `MembershipId` の占有率（最頻会員の利用数 / ポイント利用 Sale 数） | **特定会員 ID へのポイント利用の偏り（従業員の自会員付替え）** |
| `topRedeemMembershipId` | ポイント利用 Sale の最頻 `MembershipId`（任意） | 偏りの主体 |
| `topReturnMembershipId` | `Return(d)` に紐づく `MembershipId` の最頻値（任意） | 返金先の偏り |
| `afterHoursCount` | `Void`/`Return` かつ時刻が営業時間外帯の件数（§5） | 閑散帯の操作 |
| `saleThenVoidCount` | 同額 Sale → `Void` が `WindowSeconds` 以内に発生した回数（§4） | 売上直後取消 |
| `repeatedRefundAmountCount` | 同一営業日で同額 `Return` が 2 件以上ある「金額」の数 | 定額返金の反復 |

## 3. 全体特徴量

- `datasetSummary`：件数・金額・各種比率（日別と同式を全期間で）。ポイント系は `pointsEarnedTotal` / `pointsRedeemedTotal` / `nonMemberPointsEarnedCount` / `nonMemberPointsRedeemedCount` / `pointsRedeemedCashCount` を含む。
- `typeDistribution`：`Type` ごとの件数・金額・構成比。
- `paymentDistribution`：`PaymentMethod` ごとの件数・金額・構成比。
- `dailySummaries`：日別特徴量の配列。
- `sequenceAnomalies`：シーケンス異常の配列（§4）。

## 4. シーケンス異常の検出（決定論）

取引を `(BusinessDate, Time, Sequence)` でソートしてスキャンする。

- **SaleThenVoid**：`Void` 行について、同一営業日で `OriginalTransactionId` が指す `Sale`（無ければ直前の同額 Sale）との時刻差が `WindowSeconds`（既定 180 秒）以内なら記録。売上直後に取り消す現金抜きの典型。
- **RepeatedRefundAmount**：同一営業日で同額の `Return` が `MinOccurrences`（既定 2）以上。架空返品はキリの良い同額を反復しやすい。
- **PointsRedeemThenVoid**：`PointsRedeemed>0` の Sale 直後（`WindowSeconds` 以内）に同額 `Void`。ポイント利用と現金/ポイントの差を作る手口。
- **RepeatedRefundMembership**（任意）：同一 `MembershipId` への `Return` が反復。返金先の付け替え。

各異常は `{ date, kind, transactionId?, originalTransactionId?, amount?, occurrences?, secondsApart? }` として `sequenceAnomalies` に格納し、LLM に渡す。

## 5. しきい値パラメータ（設定可能にする）

`PosCheckerSettings` に持たせ、サンプル調整・誤検知制御を容易にする。

| パラメータ | 既定値 | 用途 |
|---|---|---|
| `SaleThenVoidWindowSeconds` | 180 | SaleThenVoid / PointsRedeemThenVoid の時間窓 |
| `RepeatedRefundMinOccurrences` | 2 | 定額返金反復の最小回数 |
| `BusinessHoursStart` / `BusinessHoursEnd` | 09:00 / 21:00 | 営業時間帯（外を `afterHours`） |
| `HighValueReturnAmount` | 5000 | 高額返品の目安（プロンプトに供給） |
| `RedeemConcentrationThreshold` | 0.5 | ポイント利用の会員偏りの目安（プロンプトに供給） |

> これらは **機械集計の入力**であり、最終スコアは LLM が決める。inspector が `DefaultCurrent=0.97`・`NearDefaultTolerance=0.02` を定数で持っていたのと同じ役割。

## 6. 不正パターン・カタログ（強シグナル）

LLM に「強い不正シグナル」として教える主パターンと対応特徴量。

| # | パターン | 主な特徴量 | 現金化経路 |
|---|---|---|---|
| 1 | **取消抜き取り**（Void skim） | 高 `voidRatio`・`saleThenVoidCount`・Cash 偏り | 売上計上→直後取消で釣銭差を着服 |
| 2 | **返品抜き取り**（Refund fraud） | 高 `returnRatio`・`noReceiptReturnRatio`・`cashReturnAmount`・`repeatedRefundAmountCount` | 架空/水増し返品で現金を出金 |
| 3a | **ポイント不正・付与側**（Earned） | `nonMemberPointsEarnedCount`・付与の特定会員偏り | 顧客が使わないポイントを自会員に付与し後で換金 |
| 3b | **ポイント不正・利用側**（Redeemed） | `nonMemberPointsRedeemedCount`・`pointsRedeemedCashCount`・高 `redeemMembershipConcentration`・`PointsRedeemThenVoid` | 他人/自分のポイントを客の現金取引に差し込み、満額現金を受け取り差額を着服 |
| 4 | **レジ開放多発**（No-Sale） | 高 `noSaleCount` | 売上を介さずドロワーを開け現金操作 |
| 5 | **値引き乱用** | 高 `discountRatio` | 不正値引きで差額・便宜供与 |
| 6 | **閑散帯への集中** | 高 `afterHoursCount` | 客・監視の少ない時間に操作 |

## 7. 誤検知を避けるための「正常の前提」

inspector の「自然なばらつきは不正としない」に対応する POS 版の前提。プロンプトに明記する。

- 繁忙日・週末は取引数・返品数が増えるのは自然。**件数の絶対値ではなく比率・反復・時間帯の不自然さ**を見る。
- 客都合の返品・レシート無し返品は一定割合で正常に発生する。**単発の高額返品やレシート無しが少数あるだけでは高スコアにしない**。
- **ポイント利用・付与自体は正常**。顧客が自分のポイントを使って支払いを減らすのは日常的。**異常は「会員不在の付与/利用」「特定会員 ID への偏り」「現金売上への集中」「利用直後の取消」**であり、ポイント利用が多いこと自体では高スコアにしない。
- Void も訂正で日常的に発生する。**売上直後・同額・現金・反復**が揃って初めて強いシグナル。
- 強いシグナルが**複数種類あり、かつ複数営業日に広がる**場合に限り総合スコアを高くする。

## 8. スコアリング方針

inspector と同一の枠組み。

- `overall_score`：従業員全体の不正確率 0〜100（0=極めて正常、100=極めて不正）。
- 日別 `score`：各営業日 0〜100。`daily_results` は **入力に含まれる全営業日**を返す。
- 70 以上は「強いシグナルが複数 × 複数日」に限定。大半の日で比率が正常域なら 40 未満を優先。
- C# 側で `Math.Clamp(score,0,100)`、`daily_results` を日付で全営業日に正規化（欠損日は score 0・理由「日別評価なし」）。inspector の `Normalize` を踏襲。

### 検証基準（サンプル設計の目標値・temperature:0）

inspector の「normals<40 かつ frauds≥70 の分離」に倣う。

- `normal-typical` / `normal-busy-weekend` … 概ね **<40**（繁忙・正当返品・通常のポイント利用で誤検知しない）
- `fraud-void-skim` … 概ね **≥75**（高 voidRatio＋SaleThenVoid＋Cash）
- `fraud-refund-no-receipt` … 概ね **≥80**（高 returnRatio＋noReceipt＋現金＋反復）
- `fraud-point-abuse` … 概ね **70〜90**（会員不在/特定会員偏りのポイント利用・付与が主シグナルのため振れるが ≥70）

> 実測（temperature:0、gpt-5.4-mini、`tools/validate_foundry.py`）: normal-typical≈28 / normal-busy-weekend≈28 / fraud-void-skim≈78 / fraud-refund-no-receipt≈82 / fraud-point-abuse≈82。なお、出荷プロンプト `Prompts/pos_analyzer.txt` には本節の方針を満たすよう「正常の目安（数値アンカー）」「強いシグナルが複数営業日に及ぶ場合の overall 引き上げ」「ポイント不正の重大性に基づく下限」を明記して校正済み（プロンプト本体が最終仕様）。

## 9. プロンプト設計（`Prompts/pos_analyzer.txt`）

inspector の `inspection_analyzer.txt`（前提 → 判断ルール → 出力 JSON）構造を踏襲したシステムプロンプト案。

```text
あなたは小売店のPOSレジ操作ログを監査し、レジ担当者(従業員)の不正操作を検出するアシスタントです。
マークダウンや説明文を一切含めず、以下のJSONオブジェクトのみを返してください:
{"overall_score":0,"summary":"<全体要約>","recommended_action":"<推奨アクション>","suspicious_patterns":["<疑わしい特徴>"],"daily_results":[{"date":"YYYY-MM-DD","score":0,"reason":"<日別の短い理由>","suspicious_transaction_ids":["<取引ID>"]}]}.

前提:
・入力は従業員1名分・複数営業日のPOSトランザクションで、判定は「1営業日×1従業員」単位で行う。
・取引種別は 売上(Sale)/取消(Void)/返品(Return)/レジ開放(NoSale)。金額は商品総額(グロス)で非負、方向は種別で表す。
・ポイントは1pt=1円相当で、支払いを圧縮する点は値引きと同じだが、原資が販促負債である点が異なる。
・繁忙日は取引数・返品数が増えるのは自然であり、件数の絶対値ではなく比率・反復・時間帯の不自然さを見る。
・客都合の返品やレシート無し返品、訂正のための取消、顧客本人によるポイント利用は一定割合で正常に発生する。

強い不正シグナル:
・売上直後(数分以内)に同額を取り消すVoidが反復する(saleThenVoid)、Void率が高く現金取引に偏る → 取消抜き取り。
・返金額比率が高い、レシート無し返品が多い、現金返金が大きい、同額返品が反復する → 返品抜き取り。
・会員IDが無いのにポイント付与/利用がある、付与や利用が特定会員IDに偏る(redeemMembershipConcentrationが高い)、ポイント利用が現金売上に集中する、ポイント利用直後に取消 → ポイント不正(付替え・現金化)。
・レジ開放(NoSale)の多発、売上に対する値引き比率が高い、閑散帯(営業時間外)への取消・返品の集中。

判断ルール:
・単発の高額返品やレシート無しが少数あるだけでは高スコアにしないこと。
・正常な繁忙・客都合返品・通常のポイント利用を不正とみなさないこと。ポイント利用が多いこと自体は不正ではない。
・overall_score を 70 以上にするのは、強いシグナルが複数種類あり、かつ複数営業日に広がっている場合に限ること。
・大半の日で各比率が正常域に収まるなら overall_score は 40 未満を優先すること。
・overall_score は従業員全体の不正確率を 0〜100 で返すこと。0 は極めて正常、100 は極めて不正。
・daily_results には入力に含まれる営業日をすべて返すこと。
・reason は簡潔な1文にすること。suspicious_patterns は 3〜6 件以内にすること。
・不確かな点は断定しすぎず、入力パターンから説明可能な範囲で判断すること。
```

ユーザープロンプト（`BuildUserPrompt`）は inspector 同様、前文（前提と強シグナルの箇条書き）＋ `JSON のみで回答してください。` ＋ camelCase ペイロード（§02-7）を連結する。

## 10. 出力 → C# モデル対応

`PosAnalysisResult { OverallScore, Summary, RecommendedAction, SuspiciousPatterns[], DailyResults[] }`、`DailyRiskResult { BusinessDate, Score, Reason, SuspiciousTransactionIds[] }`。
inspector の `suspicious_customer_ids` を **`suspicious_transaction_ids`** に置き換える（巡回の顧客 ID → 取引 ID）。
