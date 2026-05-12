using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Core.Checks;

public enum CheckStatus
{
    Passed,
    Failed,
    Skipped,
    Error
}

public sealed record RuleCheckResult(
    ChecklistRef Ref,
    CheckStatus Status,
    IReadOnlyList<Violation> Violations,
    string? Note = null);
