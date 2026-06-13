namespace PosChecker.Models;

public sealed record PosCheckResult(
    string UploadedFileName,
    PosFeatureSummary FeatureSummary,
    PosAnalysisResult Analysis,
    IReadOnlyList<TransactionRecord> PreviewRows);
