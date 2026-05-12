using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.3 (letra c) Cliente — Imparcialidade na verificação:
/// iniciais do elaborador e do verificador técnico devem ser distintas.
/// </summary>
public sealed class IniciaisDistintasCheck : IRuleCheck
{
    public ChecklistRef Ref { get; } = new("PS-002", "4.3.3 (letra c)", ChecklistPadrao.Cliente);

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var titulosAlias = (ctx.Profile.Get("quadroCaracteristicas.aliases")
            ?? "Características do Documento|Quadro de Características|Características")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var elabAliases = SplitAliases(ctx.Profile.Get("iniciais.rotuloElaborador") ?? "Elaborado por|Emissor|Elaborador");
        var verifAliases = SplitAliases(ctx.Profile.Get("iniciais.rotuloVerificador") ?? "Verificado por|Verificador|Verificador Técnico");

        var table = QuadroCaracteristicas.Find(ctx.Structure, titulosAlias);
        if (table is null)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Quadro 'Características do Documento' não localizado."));
        }

        var fields = QuadroCaracteristicas.FieldsByLabel(table);
        var elab = ExtractInitials(LookupAny(fields, elabAliases));
        var verif = ExtractInitials(LookupAny(fields, verifAliases));

        // Fallback: revision-history layout where Emissor/Verificador are column headers.
        if (string.IsNullOrEmpty(elab) || string.IsNullOrEmpty(verif))
        {
            var columnSpec = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["elaborador"] = elabAliases,
                ["verificador"] = verifAliases
            };
            var byCol = QuadroCaracteristicas.FieldsByColumn(table, columnSpec);
            if (byCol is not null)
            {
                elab ??= ExtractInitials(byCol.GetValueOrDefault("elaborador"));
                verif ??= ExtractInitials(byCol.GetValueOrDefault("verificador"));
            }
        }

        var violations = new List<Violation>();
        if (string.IsNullOrEmpty(elab) || string.IsNullOrEmpty(verif))
        {
            violations.Add(new Violation(
                RuleId: Ref.ToString(),
                Severity: Severity.Warning,
                Message: $"Não foi possível extrair iniciais de '{string.Join("/", elabAliases)}' ou '{string.Join("/", verifAliases)}' no quadro 'Características'.",
                Location: new ViolationLocation(null, null, null, "Quadro Características")));
        }
        else if (string.Equals(elab, verif, StringComparison.OrdinalIgnoreCase))
        {
            violations.Add(new Violation(
                RuleId: Ref.ToString(),
                Severity: Severity.Error,
                Message: $"Iniciais de elaborador e verificador são idênticas ('{elab}'). Devem ser distintas (imparcialidade).",
                Location: new ViolationLocation(null, null, null, "Quadro Características")));
        }

        var status = violations.Count == 0 ? CheckStatus.Passed
                   : violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : CheckStatus.Skipped;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }

    private static IReadOnlyList<string> SplitAliases(string raw) =>
        raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? LookupAny(IReadOnlyDictionary<string, string> fields, IEnumerable<string> aliases)
    {
        foreach (var label in aliases)
        {
            if (fields.TryGetValue(label, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
            var hit = fields.FirstOrDefault(kv => kv.Key.Contains(label, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(hit.Value)) return hit.Value;
        }
        return null;
    }

    private static string? ExtractInitials(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var tokens = raw.Split(new[] { ' ', '\t', '/', '-', ',', '(', ')', '.', ';', ':' },
                               StringSplitOptions.RemoveEmptyEntries);
        // 1) Prefer all-uppercase short tokens (typical initials like "JAS").
        var initials = tokens.FirstOrDefault(s =>
            s.Length is >= 2 and <= 5 && s.All(c => char.IsLetter(c) && char.IsUpper(c)));
        return initials;
    }
}
