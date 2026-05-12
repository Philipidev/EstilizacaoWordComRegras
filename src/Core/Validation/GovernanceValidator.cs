using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Rules;

namespace WordComplianceValidator.Core.Validation;

public sealed class GovernanceValidator : IValidator
{
    public IEnumerable<Violation> Validate(RuleSet ruleSet, DocumentStructure document)
    {
        var ruleMeta = ruleSet.Rules.FirstOrDefault(r => r.Type == RuleType.Governance);

        if (document.HasPendingTrackChanges)
        {
            yield return new Violation(
                RuleId: ruleMeta?.Id ?? "GOV-TRACK-CHANGES",
                Severity: ruleMeta?.Severity ?? Severity.Error,
                Message: "Documento possui alterações controladas (Track Changes) pendentes.",
                Location: null);
        }

        if (document.HasOpenComments)
        {
            yield return new Violation(
                RuleId: ruleMeta?.Id ?? "GOV-OPEN-COMMENTS",
                Severity: ruleMeta?.Severity ?? Severity.Warning,
                Message: "Documento possui comentários abertos.",
                Location: null);
        }
    }
}
