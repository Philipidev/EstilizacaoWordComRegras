using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Core.Rules;

public interface IValidator
{
    IEnumerable<Violation> Validate(RuleSet ruleSet, DocumentStructure document);
}
