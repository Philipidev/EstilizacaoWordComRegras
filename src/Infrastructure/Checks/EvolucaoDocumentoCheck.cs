using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-018 4.8 Cliente — Evolução do Documento. Verifica se o campo de revisão no
/// quadro Características está preenchido e segue o padrão esperado:
/// "0A".."0Z" (etapa de comentários), "00" (emissão final) ou "01".."99" (revisões).
/// </summary>
public sealed class EvolucaoDocumentoCheck : IRuleCheck
{
    private static readonly Regex Revisao =
        new(@"^(0[A-Z]|[0-9]{2})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ChecklistRef Ref { get; } = new("PS-018", "4.8", ChecklistPadrao.Cliente);

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var titulosAlias = (ctx.Profile.Get("quadroCaracteristicas.aliases")
            ?? "Características do Documento|Quadro de Características|Características")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var rotulos = (ctx.Profile.Get("revisao.aliasesRotulo") ?? "Revisão|Rev.|Rev")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var table = QuadroCaracteristicas.Find(ctx.Structure, titulosAlias);
        if (table is null)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Quadro 'Características do Documento' não localizado."));
        }

        var byLabel = QuadroCaracteristicas.FieldsByLabel(table);
        var revisao = LookupAny(byLabel, rotulos);

        if (string.IsNullOrWhiteSpace(revisao))
        {
            // Fallback: layout coluna (cabeçalho "Revisão").
            var col = QuadroCaracteristicas.FieldsByColumn(table,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["revisao"] = rotulos
                });
            revisao = col?.GetValueOrDefault("revisao");
        }

        var violations = new List<Violation>();
        if (string.IsNullOrWhiteSpace(revisao))
        {
            violations.Add(new Violation(Ref.ToString(), Severity.Error,
                "Campo 'Revisão' não encontrado no quadro Características.",
                new ViolationLocation(null, null, null, "Quadro Características")));
        }
        else
        {
            var token = ExtractRevisionToken(revisao);
            if (token is null || !Revisao.IsMatch(token))
            {
                violations.Add(new Violation(Ref.ToString(), Severity.Error,
                    $"Valor de revisão '{revisao}' não segue padrão (0A–0Z, 00, 01–99).",
                    new ViolationLocation(null, null, null, "Quadro Características")));
            }
        }

        var status = violations.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }

    private static string? LookupAny(IReadOnlyDictionary<string, string> fields, IEnumerable<string> aliases)
    {
        foreach (var label in aliases)
        {
            var hit = fields.FirstOrDefault(kv => kv.Key.Contains(label, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(hit.Value)) return hit.Value;
        }
        return null;
    }

    private static string? ExtractRevisionToken(string raw)
    {
        var trimmed = raw.Trim();
        var m = Regex.Match(trimmed, @"\b(0[A-Za-z]|\d{2})\b");
        return m.Success ? m.Value.ToUpperInvariant() : null;
    }
}
