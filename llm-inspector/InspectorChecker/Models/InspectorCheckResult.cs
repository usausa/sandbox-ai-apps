namespace InspectorChecker.Models;

public sealed record InspectorCheckResult(
    string UploadedFileName,
    InspectionFeatureSummary FeatureSummary,
    InspectionAnalysisResult Analysis,
    TokenUsageResult Usage,
    IReadOnlyList<SurveyRecord> PreviewRows);
