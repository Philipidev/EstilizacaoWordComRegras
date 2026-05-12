namespace WordComplianceValidator.Core.Models;

public sealed record ViolationLocation(
    string? ParagraphId,
    string? HeaderFooterKind,
    int? SectionIndex);

public sealed record Violation(
    string RuleId,
    Severity Severity,
    string Message,
    ViolationLocation? Location);
