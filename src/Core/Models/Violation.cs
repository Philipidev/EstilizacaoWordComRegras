namespace WordComplianceValidator.Core.Models;

public sealed record ViolationLocation(
    string? ParagraphId,
    string? HeaderFooterKind,
    int? SectionIndex,
    string? Detail = null);

public sealed record Violation(
    string RuleId,
    Severity Severity,
    string Message,
    ViolationLocation? Location);
