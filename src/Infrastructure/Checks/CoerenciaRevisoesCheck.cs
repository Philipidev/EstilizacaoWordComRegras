using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.3 (letra i) Cliente — Coerência entre revisões PdA e Cliente.
/// Verifica se a(s) revisão(ões) registrada(s) na folha de rosto / capa batem
/// com o valor do quadro Características.
/// </summary>
public sealed class CoerenciaRevisoesCheck : IRuleCheck
{
    private static readonly Regex RevisionToken = new(@"\b(0[A-Za-z]|\d{2})\b", RegexOptions.Compiled);
    // Captura revisão precedida de contexto explícito ("Rev", "Revisão", "Rev.").
    private static readonly Regex RevisionInContext = new(
        @"(?:rev(?:is[ãa]o)?\.?\s*[:\-]?\s*)(0[A-Za-z]|\d{2})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const int FolhaRostoParagraphCount = 30;

    public ChecklistRef Ref { get; } = new("PS-002", "4.3.3 (letra i)", ChecklistPadrao.Cliente);

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var titulosAlias = (ctx.Profile.Get("quadroCaracteristicas.aliases")
            ?? "Características do Documento|Quadro de Características|Características")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var revAliases = (ctx.Profile.Get("revisao.aliasesRotulo") ?? "Revisão|Rev.|Rev")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var table = QuadroCaracteristicas.Find(ctx.Structure, titulosAlias);
        if (table is null)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Quadro 'Características do Documento' não localizado."));
        }

        var byLabel = QuadroCaracteristicas.FieldsByLabel(table);
        var revQuadro = ExtractRevision(LookupAny(byLabel, revAliases))
                         ?? ExtractRevision(QuadroCaracteristicas.FieldsByColumn(table,
                                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["rev"] = revAliases })
                            ?.GetValueOrDefault("rev"));

        if (string.IsNullOrEmpty(revQuadro))
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Não foi possível extrair revisão do quadro Características."));
        }

        // Folha de rosto = parágrafos ANTES do primeiro heading, limitados a N.
        var folhaParagrafos = TakeFolhaRosto(ctx.Structure.Paragraphs, FolhaRostoParagraphCount);
        var folhaTexto = string.Join(" ", folhaParagrafos.Select(p => p.Text));

        // Só consideramos revisões em contexto explícito ("Rev. NN" / "Revisão NN") para
        // evitar falsos positivos com números soltos de tabelas/datas.
        var folhaRevs = RevisionInContext.Matches(folhaTexto)
            .Select(m => m.Groups[1].Value.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var violations = new List<Violation>();
        if (folhaRevs.Count == 0)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Nenhuma revisão localizada na folha de rosto — não foi possível comparar."));
        }

        if (!folhaRevs.Contains(revQuadro))
        {
            violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                $"Revisão do quadro Características ('{revQuadro}') não consta na folha de rosto (revisões vistas: {string.Join(",", folhaRevs)}).",
                new ViolationLocation(null, null, null, "Folha de Rosto × Quadro")));
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

    private static string? ExtractRevision(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var m = RevisionToken.Match(raw);
        return m.Success ? m.Value.ToUpperInvariant() : null;
    }

    /// <summary>
    /// Folha de rosto = parágrafos do início do documento até o primeiro heading
    /// (Heading1/Título 1) ou até o limite máximo, o que vier primeiro.
    /// </summary>
    internal static IEnumerable<ExtractedParagraph> TakeFolhaRosto(
        IReadOnlyList<ExtractedParagraph> paragraphs, int max)
    {
        var taken = 0;
        foreach (var p in paragraphs)
        {
            if (taken >= max) yield break;
            var style = p.StyleId ?? string.Empty;
            if (taken > 0
                && (style.StartsWith("Heading1", StringComparison.OrdinalIgnoreCase)
                    || style.Equals("Heading 1", StringComparison.OrdinalIgnoreCase)
                    || style.StartsWith("Titulo1", StringComparison.OrdinalIgnoreCase)
                    || style.Equals("Título 1", StringComparison.OrdinalIgnoreCase)))
                yield break;
            taken++;
            yield return p;
        }
    }
}
