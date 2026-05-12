using WordComplianceValidator.Core.Checklist;

namespace WordComplianceValidator.Core.Checks;

public interface IRuleCheck
{
    ChecklistRef Ref { get; }
    Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default);
}
