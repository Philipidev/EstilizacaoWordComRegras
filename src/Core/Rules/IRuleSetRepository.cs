using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Core.Rules;

public interface IRuleSetRepository
{
    Task<string> SaveAsync(RuleSet ruleSet, CancellationToken cancellationToken = default);
    Task<RuleSet> LoadAsync(string path, CancellationToken cancellationToken = default);
    Task<string> NextVersionAsync(string clientName, bool major, CancellationToken cancellationToken = default);
}
