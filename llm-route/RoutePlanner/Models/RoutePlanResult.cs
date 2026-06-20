namespace RoutePlanner.Models;

// 画面に返す最終結果。共通設定・算出した足順・AIレビュー・トークン使用量・入力プレビューを束ねる。
public sealed record RoutePlanResult(
    string UploadedFileName,
    CommonSettings Common,
    RoutePlan Plan,
    RouteReviewResult Review,
    TokenUsageResult Usage,
    IReadOnlyList<VisitTarget> PreviewRows);
