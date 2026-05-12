using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Rules;

namespace WordComplianceValidator.Core.Validation;

public sealed class RuleEngine
{
    private readonly IReadOnlyList<IValidator> _validators;

    public RuleEngine(IEnumerable<IValidator>? validators = null)
    {
        _validators = validators?.ToList() ?? new List<IValidator>
        {
            new StyleValidator(),
            new HeaderFooterValidator(),
            new GovernanceValidator()
        };
    }

    public IReadOnlyList<Violation> Run(RuleSet ruleSet, DocumentStructure document)
    {
        var results = new List<Violation>();
        foreach (var validator in _validators)
        {
            results.AddRange(validator.Validate(ruleSet, document));
        }
        return results;
    }
}
