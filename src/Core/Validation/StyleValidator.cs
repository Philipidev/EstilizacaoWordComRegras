using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Rules;

namespace WordComplianceValidator.Core.Validation;

public sealed class StyleValidator : IValidator
{
    public IEnumerable<Violation> Validate(RuleSet ruleSet, DocumentStructure document)
    {
        var styleRules = ruleSet.Rules.Where(r => r.Type == RuleType.Style).ToList();

        foreach (var (styleKey, expected) in ruleSet.Styles)
        {
            var actual = FindStyle(document, styleKey);
            if (actual is null)
            {
                var rule = styleRules.FirstOrDefault();
                yield return new Violation(
                    RuleId: rule?.Id ?? $"STYLE-MISSING-{styleKey}",
                    Severity: rule?.Severity ?? Severity.Warning,
                    Message: $"Estilo '{styleKey}' não encontrado no documento.",
                    Location: null);
                continue;
            }

            foreach (var mismatch in Compare(styleKey, expected, actual))
            {
                var rule = styleRules.FirstOrDefault();
                yield return new Violation(
                    RuleId: rule?.Id ?? $"STYLE-{styleKey}",
                    Severity: rule?.Severity ?? Severity.Warning,
                    Message: mismatch,
                    Location: null);
            }
        }
    }

    private static ExtractedStyle? FindStyle(DocumentStructure document, string key)
    {
        if (document.Styles.TryGetValue(key, out var byId)) return byId;
        return document.Styles.Values.FirstOrDefault(s =>
            string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.StyleId, key, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> Compare(string key, StyleRule expected, ExtractedStyle actual)
    {
        if (expected.Font is not null && !string.Equals(expected.Font, actual.Font, StringComparison.OrdinalIgnoreCase))
            yield return $"Estilo '{key}': fonte esperada '{expected.Font}', encontrada '{actual.Font ?? "(nenhuma)"}'.";
        if (expected.Size is not null && actual.Size is not null && Math.Abs(expected.Size.Value - actual.Size.Value) > 0.01)
            yield return $"Estilo '{key}': tamanho esperado {expected.Size}, encontrado {actual.Size}.";
        if (expected.Bold is not null && expected.Bold != actual.Bold)
            yield return $"Estilo '{key}': negrito esperado {expected.Bold}, encontrado {actual.Bold?.ToString() ?? "(indefinido)"}.";
        if (expected.Italic is not null && expected.Italic != actual.Italic)
            yield return $"Estilo '{key}': itálico esperado {expected.Italic}, encontrado {actual.Italic?.ToString() ?? "(indefinido)"}.";
        if (expected.Alignment is not null && !string.Equals(expected.Alignment, actual.Alignment, StringComparison.OrdinalIgnoreCase))
            yield return $"Estilo '{key}': alinhamento esperado '{expected.Alignment}', encontrado '{actual.Alignment ?? "(nenhum)"}'.";
    }
}
