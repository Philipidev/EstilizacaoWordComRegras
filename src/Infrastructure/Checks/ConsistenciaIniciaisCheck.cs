using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.3 (letra d) — Consistência de iniciais entre folha de rosto e quadro
/// "Características do Documento". Verifica se as iniciais (elaborador, verificador,
/// aprovador) que aparecem na folha de rosto (primeiros parágrafos) batem com as
/// iniciais do quadro Características. Meridian não é verificável a partir do .docx.
/// </summary>
public sealed class ConsistenciaIniciaisCheck : IRuleCheck
{
    private static readonly Regex InitialsToken = new(@"\b[A-Z]{2,5}\b", RegexOptions.Compiled);
    private const int FolhaRostoParagraphCount = 60;

    public ConsistenciaIniciaisCheck(ChecklistPadrao padrao = ChecklistPadrao.Cliente)
    {
        Ref = new ChecklistRef("PS-002", "4.3.3 (letra d)", padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var titulosAlias = (ctx.Profile.Get("quadroCaracteristicas.aliases")
            ?? "Características do Documento|Quadro de Características|Características")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var table = QuadroCaracteristicas.Find(ctx.Structure, titulosAlias);
        if (table is null)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Quadro 'Características do Documento' não localizado."));
        }

        var elabAliases = SplitAliases(ctx.Profile.Get("iniciais.rotuloElaborador")
            ?? "Elaborado por|Emissor|Elaborador");
        var verifAliases = SplitAliases(ctx.Profile.Get("iniciais.rotuloVerificador")
            ?? "Verificado por|Verificador|Verificador Técnico");
        var aprovAliases = SplitAliases(ctx.Profile.Get("iniciais.rotuloAprovador")
            ?? "Aprovado por|Aprovador");

        var byLabel = QuadroCaracteristicas.FieldsByLabel(table);
        var quadroElab = ExtractInitials(LookupAny(byLabel, elabAliases));
        var quadroVerif = ExtractInitials(LookupAny(byLabel, verifAliases));
        var quadroAprov = ExtractInitials(LookupAny(byLabel, aprovAliases));

        if (quadroElab is null && quadroVerif is null && quadroAprov is null)
        {
            var columnSpec = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["elaborador"] = elabAliases,
                ["verificador"] = verifAliases,
                ["aprovador"] = aprovAliases
            };
            var byCol = QuadroCaracteristicas.FieldsByColumn(table, columnSpec);
            if (byCol is not null)
            {
                quadroElab ??= ExtractInitials(byCol.GetValueOrDefault("elaborador"));
                quadroVerif ??= ExtractInitials(byCol.GetValueOrDefault("verificador"));
                quadroAprov ??= ExtractInitials(byCol.GetValueOrDefault("aprovador"));
            }
        }

        // Iniciais da "folha de rosto": coletadas dos primeiros N parágrafos.
        var folhaTexto = string.Join(" ",
            ctx.Structure.Paragraphs.Take(FolhaRostoParagraphCount).Select(p => p.Text));
        var folhaInitials = InitialsToken.Matches(folhaTexto)
            .Select(m => m.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var violations = new List<Violation>();
        Compare("elaborador", quadroElab);
        Compare("verificador", quadroVerif);
        Compare("aprovador", quadroAprov);

        void Compare(string papel, string? esperado)
        {
            if (string.IsNullOrEmpty(esperado)) return;
            if (folhaInitials.Count == 0) return; // sem folha de rosto extraída
            if (!folhaInitials.Contains(esperado))
            {
                violations.Add(new Violation(
                    RuleId: Ref.ToString(),
                    Severity: Severity.Warning,
                    Message: $"Iniciais do {papel} no quadro Características ('{esperado}') não constam na folha de rosto.",
                    Location: new ViolationLocation(null, null, null, "Folha de Rosto × Quadro Características")));
            }
        }

        var status = violations.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }

    private static IReadOnlyList<string> SplitAliases(string raw) =>
        raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? LookupAny(IReadOnlyDictionary<string, string> fields, IEnumerable<string> aliases)
    {
        foreach (var label in aliases)
        {
            var hit = fields.FirstOrDefault(kv => kv.Key.Contains(label, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(hit.Value)) return hit.Value;
        }
        return null;
    }

    private static string? ExtractInitials(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var match = InitialsToken.Match(raw);
        return match.Success ? match.Value : null;
    }
}
