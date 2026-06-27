// CA1515: Blazor のコンポーネント等はフレームワークが実体化する public 型のため、internal 化の提案は抑制する。
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "Public types are instantiated by the framework (Blazor components, etc.).")]
