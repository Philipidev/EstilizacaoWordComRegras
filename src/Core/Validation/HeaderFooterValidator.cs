using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Rules;

namespace WordComplianceValidator.Core.Validation;

public sealed class HeaderFooterValidator : IValidator
{
    public IEnumerable<Violation> Validate(RuleSet ruleSet, DocumentStructure document)
    {
        var headerSpecs = ruleSet.Headers.Select(h => (h.Required, h.Contains)).ToList();
        var footerSpecs = ruleSet.Footers.Select(f => (f.Required, f.Contains)).ToList();
        foreach (var v in Evaluate(headerSpecs, document.Headers, "header", RuleType.Header, ruleSet))
            yield return v;
        foreach (var v in Evaluate(footerSpecs, document.Footers, "footer", RuleType.Footer, ruleSet))
            yield return v;
    }

    private static IEnumerable<Violation> Evaluate(
        IReadOnlyList<(bool Required, IReadOnlyList<string> Contains)> specs,
        IReadOnlyList<ExtractedHeaderFooter> actual,
        string kind,
        RuleType ruleType,
        RuleSet ruleSet)
    {
        if (specs.Count == 0) yield break;
        var ruleMeta = ruleSet.Rules.FirstOrDefault(r => r.Type == ruleType);

        foreach (var rule in specs)
        {
            if (rule.Required && actual.Count == 0)
            {
                yield return new Violation(
                    RuleId: ruleMeta?.Id ?? $"{kind.ToUpperInvariant()}-MISSING",
                    Severity: ruleMeta?.Severity ?? Severity.Error,
                    Message: ruleMeta?.Message ?? $"{kind} obrigatório ausente.",
                    Location: new ViolationLocation(null, kind, null));
                continue;
            }

            var combined = string.Join("\n", actual.Select(a => a.Text));
            foreach (var token in rule.Contains)
            {
                if (!combined.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    yield return new Violation(
                        RuleId: ruleMeta?.Id ?? $"{kind.ToUpperInvariant()}-CONTENT",
                        Severity: ruleMeta?.Severity ?? Severity.Warning,
                        Message: $"{kind} não contém '{token}'.",
                        Location: new ViolationLocation(null, kind, null));
                }
            }
        }
    }
}
