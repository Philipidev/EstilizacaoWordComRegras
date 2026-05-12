using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Core.Rules;

public interface IRuleParser
{
    Task<RuleSet> ParseAsync(
        string clientName,
        string versionLabel,
        string rulesText,
        DocumentStructure templateStructure,
        CancellationToken cancellationToken = default);
}
