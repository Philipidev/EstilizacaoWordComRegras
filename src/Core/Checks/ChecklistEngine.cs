using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Core.Checks;

public sealed class ChecklistEngine
{
    private readonly IReadOnlyDictionary<ChecklistRef, IRuleCheck> _checks;

    public ChecklistEngine(IEnumerable<IRuleCheck> checks)
    {
        _checks = checks.ToDictionary(c => c.Ref);
    }

    public async Task<IReadOnlyList<RuleCheckResult>> RunAsync(
        DocumentContext ctx,
        IReadOnlyList<ChecklistEntry> catalog,
        CancellationToken cancellationToken = default)
    {
        var results = new List<RuleCheckResult>(catalog.Count);

        foreach (var entry in catalog)
        {
            if (!entry.IaAutomatizavel)
            {
                results.Add(new RuleCheckResult(entry.Ref, CheckStatus.Skipped,
                    Array.Empty<Violation>(),
                    Note: "Revisão manual (IA=Não)."));
                continue;
            }

            if (!_checks.TryGetValue(entry.Ref, out var check))
            {
                results.Add(new RuleCheckResult(entry.Ref, CheckStatus.Skipped,
                    Array.Empty<Violation>(),
                    Note: "Check ainda não implementado."));
                continue;
            }

            try
            {
                var r = await check.RunAsync(ctx, cancellationToken);
                results.Add(r);
            }
            catch (Exception ex)
            {
                results.Add(new RuleCheckResult(entry.Ref, CheckStatus.Error,
                    Array.Empty<Violation>(),
                    Note: $"Erro ao executar check: {ex.Message}"));
            }
        }

        return results;
    }
}
