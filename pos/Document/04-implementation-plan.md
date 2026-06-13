# PosChecker 設計書 ④ 実装計画

設計書 ①〜③ を実装に落とすための構成・クラス・サンプル・手順・検証方法。inspector のプロジェクト構造をミラーする。

## 1. プロジェクト構成

```text
pos/
├─ Document/                      … 本設計書（01〜04）
├─ Directory.Build.targets        … inspector からコピー
├─ AGENTS.md                      … inspector と同じコーディング規約
├─ PosChecker.slnx                … ソリューション
└─ PosChecker/
   ├─ PosChecker.csproj           … net10.0 / 同パッケージ構成
   ├─ Program.cs                  … DI・ミドルウェア
   ├─ Log.cs                      … Serilog 拡張（DebugXxx）
   ├─ GlobalSuppressions.cs
   ├─ appsettings.json            … PosChecker / Foundry セクション
   ├─ appsettings.Development.json
   ├─ Models/
   │   ├─ TransactionRecord.cs
   │   ├─ TransactionType.cs        (enum)
   │   ├─ PaymentMethod.cs          (enum)
   │   ├─ TransactionTypeBreakdown.cs
   │   ├─ PaymentBreakdown.cs
   │   ├─ SequenceAnomaly.cs
   │   ├─ DailyFeatureSummary.cs
   │   ├─ PosFeatureSummary.cs
   │   ├─ PosAnalysisResult.cs
   │   ├─ DailyRiskResult.cs
   │   └─ PosCheckResult.cs
   ├─ Services/
   │   ├─ TransactionCsvLoader.cs
   │   ├─ PosFeatureSummaryBuilder.cs
   │   ├─ PosFraudAnalyzer.cs
   │   └─ PosCheckerService.cs
   ├─ Settings/
   │   ├─ FoundrySettings.cs        … inspector と同一
   │   └─ PosCheckerSettings.cs
   ├─ Prompts/
   │   └─ pos_analyzer.txt          … csproj で CopyToOutputDirectory
   ├─ Components/
   │   ├─ App.razor / Routes.razor / _Imports.razor
   │   ├─ Layout/…
   │   └─ Pages/
   │       ├─ Check.razor
   │       └─ Check.razor.cs
   └─ wwwroot/
       └─ samples/                  … サンプル CSV 5 種
```

`Prompts/*.txt` と `wwwroot/samples/*.csv` は `.csproj` で出力ディレクトリへコピー（inspector が `Prompts` を `AppContext.BaseDirectory` から読むのと同じ）。

## 2. C# モデル一覧

入力モデルは `02-transaction-data.md §5`。集計・結果モデルは以下（inspector の対応物を POS 化）。

```csharp
public sealed record TransactionTypeBreakdown(TransactionType Type, int Count, long Amount, double Ratio);
public sealed record PaymentBreakdown(PaymentMethod Method, int Count, long Amount, double Ratio);

public sealed record SequenceAnomaly(
    DateOnly Date, string Kind, string? TransactionId, string? OriginalTransactionId,
    int? Amount, int? Occurrences, int? SecondsApart);

public sealed record DailyFeatureSummary(
    DateOnly BusinessDate, int TransactionCount,
    int SalesCount, long SalesAmount,
    int VoidCount, long VoidAmount, double VoidRatio,
    int ReturnCount, long ReturnAmount, double ReturnRatio,
    int NoReceiptReturnCount, double NoReceiptReturnRatio, long CashReturnAmount,
    int NoSaleCount, long DiscountAmount, double DiscountRatio,
    int PointsRedeemed, int PointsEarned,
    int NonMemberPointsEarnedCount, int NonMemberPointsRedeemedCount,
    int PointsRedeemedCashCount, double RedeemMembershipConcentration, string? TopRedeemMembershipId,
    int AfterHoursCount, int SaleThenVoidCount, int RepeatedRefundAmountCount,
    string? TopReturnMembershipId);

public sealed record PosFeatureSummary(
    int RecordCount, string CashierId, DateOnly StartDate, DateOnly EndDate, int BusinessDayCount,
    int SalesCount, long SalesAmount, int VoidCount, long VoidAmount,
    int ReturnCount, long ReturnAmount, int NoSaleCount,
    double VoidRatio, double ReturnAmountRatio, double NoReceiptReturnRatio,
    int PointsEarnedTotal, int PointsRedeemedTotal,
    int NonMemberPointsEarnedCount, int NonMemberPointsRedeemedCount, int PointsRedeemedCashCount,
    IReadOnlyList<TransactionTypeBreakdown> TypeDistribution,
    IReadOnlyList<PaymentBreakdown> PaymentDistribution,
    IReadOnlyList<DailyFeatureSummary> DailySummaries,
    IReadOnlyList<SequenceAnomaly> SequenceAnomalies);

public sealed record DailyRiskResult {
    public DateOnly BusinessDate { get; init; }
    public int Score { get; init; }
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<string> SuspiciousTransactionIds { get; init; } = [];
}

public sealed record PosAnalysisResult {
    public int OverallScore { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
    public IReadOnlyList<string> SuspiciousPatterns { get; init; } = [];
    public IReadOnlyList<DailyRiskResult> DailyResults { get; init; } = [];
}

public sealed record PosCheckResult(
    PosFeatureSummary FeatureSummary, PosAnalysisResult Analysis, IReadOnlyList<TransactionRecord> Records);
```

> JSON デシリアライズ時、`DailyRiskResult.BusinessDate` ←→ LLM の `date`、`SuspiciousTransactionIds` ←→ `suspicious_transaction_ids` のマッピングに注意（`JsonPropertyName` か `Normalize` 側で日付突合）。inspector は `Normalize` で `DailySummaries` を基準に全営業日へ正規化している。

## 3. サービス層（inspector ミラー）

| クラス | 責務 | 主メソッド |
|---|---|---|
| `TransactionCsvLoader` | CSV → `TransactionRecord[]`、検証、`Sequence` 付与、日時ソート | `Task<IReadOnlyList<TransactionRecord>> LoadAsync(Stream, CancellationToken)` |
| `PosFeatureSummaryBuilder` | 決定論的に `PosFeatureSummary` を構築（§02/§03 の式） | `PosFeatureSummary Build(IReadOnlyList<TransactionRecord>)` |
| `PosFraudAnalyzer` | ペイロード生成 → `IChatClient` → JSON パース → `Normalize` | `Task<PosAnalysisResult> AnalyzeAsync(PosFeatureSummary, IReadOnlyList<TransactionRecord>, CancellationToken)` |
| `PosCheckerService` | 3 層のオーケストレーション | `Task<PosCheckResult> CheckAsync(Stream, CancellationToken)` |

`PosFraudAnalyzer` は inspector と同様に `PromptJsonOptions`(CamelCase, Indented) / `ResponseJsonOptions`(CaseInsensitive)、`Temperature=0`、`ResponseFormat=ChatResponseFormat.Json`、プロンプトは `AppContext.BaseDirectory/Prompts/pos_analyzer.txt` から読む。

`PosFeatureSummaryBuilder` のポイント利用側ロジック（§03-2）：

- `nonMemberPointsRedeemedCount` … `MembershipId` 空かつ `PointsRedeemed>0`。
- `pointsRedeemedCashCount` … `PointsRedeemed>0` かつ `PaymentMethod==Cash` の Sale。
- `redeemMembershipConcentration` … ポイント利用 Sale を `MembershipId` でグルーピングし、最頻グループの件数 / ポイント利用 Sale 件数。
- `topRedeemMembershipId` … 上記最頻グループの会員 ID（同率は ID 昇順）。
- `PointsRedeemThenVoid` … `PointsRedeemed>0` の Sale 直後（`SaleThenVoidWindowSeconds` 以内）に同額 Void。

## 4. DI（Program.cs）

inspector の `Program.cs` をほぼ流用。

```csharp
builder.Services.Configure<PosCheckerSettings>(builder.Configuration.GetSection("PosChecker"));
builder.Services.Configure<FoundrySettings>(builder.Configuration.GetSection("Foundry"));

builder.Services.AddSingleton<TransactionCsvLoader>();
builder.Services.AddSingleton<PosFeatureSummaryBuilder>();
builder.Services.AddSingleton<PosFraudAnalyzer>();
builder.Services.AddSingleton<PosCheckerService>();

builder.Services.AddSingleton<IChatClient>(provider =>
{
    var s = provider.GetRequiredService<IOptions<FoundrySettings>>().Value;
    var client = new AzureOpenAIClient(new Uri(s.Endpoint), new ApiKeyCredential(s.ApiKey));
    return client.GetChatClient(s.ChatDeployment).AsIChatClient();
});
```

`PosCheckerSettings`：`UploadPath`・`PreviewRowCount` に加え、§03-5 のしきい値（`SaleThenVoidWindowSeconds`・`RepeatedRefundMinOccurrences`・`BusinessHoursStart`/`End`・`HighValueReturnAmount`・`RedeemConcentrationThreshold`）を保持。

## 5. 画面（Check.razor）の表示項目

inspector の画面構成を POS 用に差し替える。

- **総合スコア**：`OverallScore` と `Summary` / `RecommendedAction`。
- **日別スコア**：`DailyResults` を score 降順に。各行に営業日・score・reason・疑わしい取引 ID。
- **機械集計の特徴**：日別の voidRatio / returnRatio / noReceiptReturnRatio / noSaleCount / discountRatio / ポイント関連（利用・付与・会員不在・現金集中・会員偏り）。
- **取引種別の構成**：`TypeDistribution`（件数・金額・構成比）。
- **支払方法の構成**：`PaymentDistribution`。
- **シーケンス異常**：`SequenceAnomalies`（SaleThenVoid / RepeatedRefundAmount / PointsRedeemThenVoid …）。
- サンプル CSV ダウンロードボタン（5 種）、アップロード、`IsProcessing` 状態、エラー表示。

## 6. サンプルデータ設計（`wwwroot/samples/`）

従業員 1 名・**14 営業日**・1 日 25〜60 取引程度（合計 ~500 行）を目安に、機械生成（後述スクリプト）する。

| ファイル | 種別 | 設計意図 | 目標スコア |
|---|---|---|---|
| `normal-typical.csv` | 正常 | 平日中心。Void/Return が低率で自然にばらつく。会員ポイントは通常利用 | <40 |
| `normal-busy-weekend.csv` | 正常 | 週末に取引・返品が増えるが、レシート有・比率は正常域 | <40 |
| `fraud-void-skim.csv` | 不正 | 複数日で売上直後の同額 Void が反復、現金偏り、voidRatio 上昇 | ≥75 |
| `fraud-refund-no-receipt.csv` | 不正 | レシート無し・高額・同額の現金返品が複数日に集中 | ≥80 |
| `fraud-point-abuse.csv` | 不正 | 非会員売上に特定会員 ID でポイント付与/利用、現金売上への集中、利用直後取消 | 70〜90 |

生成方針：

- 正常データは時刻・金額・点数・支払方法・会員有無に乱数ゆらぎを与え、Void/Return を低率（例 Void≈3%、Return≈4%、うちレシート無し≈15%）でランダム配置。ポイント利用も会員取引で自然に発生させる。
- 不正データは正常データをベースに、対象パターンのシグナルを**複数営業日に**注入（単発で終わらせない＝③-8 の「複数日に広がる」を満たす）。`fraud-point-abuse` は会員不在のポイント利用・特定会員 ID への偏り・ポイント利用×現金を織り込む。
- 生成スクリプトは `pos/tools/`（C# コンソール or Python）に置き、サンプルを再現可能にする。inspector のサンプル（11 日×18 件）と同じく仕様を README に明記。

## 7. 実装手順（フェーズ）

1. **雛形**：inspector を `pos/` に複製し名前空間・クラス名を `Pos*` にリネーム。ビルド通過（警告ゼロ）。
2. **入力**：`TransactionType`/`PaymentMethod`/`TransactionRecord` と `TransactionCsvLoader`＋バリデーション。
3. **集計**：`PosFeatureSummaryBuilder`（日別・全体・分布・ポイント利用側特徴量）。
4. **シーケンス異常**：SaleThenVoid / RepeatedRefundAmount / PointsRedeemThenVoid の検出。
5. **LLM**：`pos_analyzer.txt` 配置、`PosFraudAnalyzer`（ペイロード・パース・`Normalize`）。
6. **画面**：`Check.razor(.cs)` を POS 表示に差し替え、サンプル DL。
7. **サンプル生成**：5 種 CSV と生成スクリプト。
8. **検証**：§8 の Foundry 再検証でスコア分離（normals<40 / frauds≥70）を確認、しきい値・プロンプト微調整。
9. **README**：inspector に倣い CSV 仕様・起動手順・画面の見方を記載。

## 8. 検証方法（Foundry）

メモリ [[inspector-current-validation]] の再検証レシピを踏襲する。

- REST: `POST {Endpoint}/openai/deployments/gpt-5.4-mini/chat/completions?api-version=2024-12-01-preview`、ヘッダ `api-key`。`temperature:0`、`response_format:{"type":"json_object"}`、`max_completion_tokens`（`max_tokens` ではない）。
- API キーは user-secrets / 環境変数のみ（メモリ・コミットに残さない）。
- 忠実検証は、C# の `PosFeatureSummaryBuilder` と同じ集計（camelCase ペイロード）＋ `pos_analyzer.txt` ＋ユーザープロンプト前文を Python 等で再現して POST。
- 回帰確認：`normal-*` < 40 かつ `fraud-*` ≥ 70 の分離が保たれているか。崩れたらしきい値（§03-5）かプロンプト（§03-9）を調整。

## 9. 留意点

- **rawRows 肥大化**：合計 ~500 行なら全件添付で問題ないが、増える場合は「`dailySummaries`＋`sequenceAnomalies` を主、`rawRows` は対象日や上位異常に限定」する縮約を `PosFraudAnalyzer` に用意（トークン対策）。
- **金額型**：合計が int を超えうるため集計は `long`（`Amount` 自体は int）。
- **ポイント=円等価**：1pt=1 円で集計。等価レートを変える場合は集計層に係数を持たせる。
- **会員 ID の扱い**：個人情報を模した値は使わず `M0001` 形式のダミー。
- **デモ前提**：実運用では端末ログ・監視カメラ・ドロワー開閉センサ・シフト表との突合で精度を上げられる旨を README に明記（inspector の「補足」に対応）。
```
