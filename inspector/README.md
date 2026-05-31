# InspectorChecker

電気の調査員が、実際には顧客を訪問せずに調査値を入力していないかを、30日分の CSV から判定する Blazor Server アプリケーションです。  
`flyer` と同じ .NET 10 / Blazor / Foundry / Serilog の構成に合わせつつ、画像チェックではなく CSV ベースの不正パターン検出に置き換えています。

## できること

- 担当者1名分の 30 日調査結果 CSV を画面からアップロード
- 顧客ごとの平均電圧と日別分布を事前集計
- Microsoft Foundry の LLM に、集計結果と生データを渡して不正確率を 0〜100 で判定
- 日単位のスコア、疑わしい顧客 ID、注目パターンを画面に表示
- 正常 2 パターン / 不正 2 パターンのサンプル CSV を同梱

## 想定する不正パターン

- 同じ日に複数顧客で 100.0V 前後の固定値が大量に並ぶ
- 99.8, 99.9, 100.0, 100.1 のような機械的な 0.1V 刻みが続く
- 顧客ごとの基準値があるはずなのに、全顧客でほぼ同じ値になる
- ある日の値並びが、別日にそのまま再利用される

## アーキテクチャ

```text
30日分の調査CSV
    ↓
CSVローダーで行を読み込み
    ↓
顧客別平均 / 日別重複率 / 100V集中率 / テンプレート再利用を集計
    ↓
Microsoft Foundry (gpt-5.4-mini) に要約 + 生データを送信
    ↓
不正確率 / 日別スコア / 注目パターンを JSON で取得
    ↓
Blazor 画面で表示
```

## CSV フォーマット

UTF-8、ヘッダ付きの CSV を想定します。

```csv
InvestigationDate,CustomerId,Voltage
2026-04-01,C1001,99.0
2026-04-01,C1002,99.9
```

## サンプルデータ

画面からダウンロードできるサンプルは次の 4 つです。

- `normal-organic-variance.csv`
  - 顧客ごとの平均値を保ちながら自然に揺れる正常データ
- `normal-route-weather-shift.csv`
  - 天候や負荷で全体が少し上下するが、顧客差は残る正常データ
- `fraud-default-100-template.csv`
  - 100.0V 前後の固定値を多用する不正データ
- `fraud-repeated-daily-template.csv`
  - 日次テンプレートを再利用する不正データ

## 設定

`appsettings.json` には検証用の Foundry エンドポイントとデプロイ名を設定済みです。

```json
{
  "InspectorChecker": {
    "UploadPath": "./upload",
    "PreviewRowCount": 12
  },
  "Foundry": {
    "Endpoint": "https://foundry-usausa-resource.services.ai.azure.com",
    "ApiKey": "",
    "ChatDeployment": "gpt-5.4-mini"
  }
}
```

API キーはコミットせず、`.NET user-secrets` で設定してください。

```pwsh
cd InspectorChecker
dotnet user-secrets set "Foundry:ApiKey" "<受領したAPIキー>"
```

## 起動方法

```pwsh
cd InspectorChecker
dotnet run
```

ブラウザで `http://localhost:5142` を開きます。

## 画面の見方

- 不正確率
  - 担当者全体に対する LLM のスコア
- 日別スコア
  - 再確認が必要な日を上から並べて表示
- 機械集計の特徴
  - 重複率、100V 集中率、最多値、テンプレート再利用を表示
- 顧客ごとの基準値
  - 平均値・標準偏差・完全一致率を表示
- 再利用された日次テンプレート
  - 完全一致の並びが複数日に出た場合に表示

## 補足

- 本アプリケーションはデモ用途です。判定ロジックは「入力値が特徴的なパターンを持つか」を説明しやすいように設計しています。
- 実運用に進める場合は、訪問予定、ルート順、端末時刻、GPS、顧客ごとの長期履歴などを追加すると精度を上げやすくなります。
- サンプル CSV は 2026 年 4 月の営業日 22 日分で、1 日 9 顧客、合計 198 レコードです。
