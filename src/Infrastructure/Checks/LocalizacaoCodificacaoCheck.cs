using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-018 4.3.2 (letra a) Cliente — Localização da Codificação Técnica.
/// Verifica se a codificação (padrão PdA) está localizada e consistente nos três
/// pontos: nome do arquivo eletrônico, folha de rosto/capa e quadro Características.
/// </summary>
public sealed class LocalizacaoCodificacaoCheck : IRuleCheck
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const int FolhaRostoParagraphCount = 60;

    public LocalizacaoCodificacaoCheck(ChecklistPadrao padrao = ChecklistPadrao.Cliente)
    {
        Ref = new ChecklistRef("PS-018", "4.3.2 (letra a)", padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var pdaRx = ctx.Profile.Get("codificacao.pdaRegex");
        if (string.IsNullOrWhiteSpace(pdaRx))
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Profile sem 'codificacao.pdaRegex'."));
        }

        var titulosAlias = (ctx.Profile.Get("quadroCaracteristicas.aliases")
            ?? "Características do Documento|Quadro de Características|Características")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var table = QuadroCaracteristicas.Find(ctx.Structure, titulosAlias);
        var quadroCodigo = ExtractCode(
            table?.Cells.Select(c => c.Text) ?? Array.Empty<string>(), pdaRx);

        // Folha de rosto = parágrafos iniciais antes do primeiro heading + cabeçalho da primeira seção.
        var folhaParagrafos = CoerenciaRevisoesCheck.TakeFolhaRosto(
                ctx.Structure.Paragraphs, FolhaRostoParagraphCount)
            .Select(p => p.Text);
        var headerTextos = ctx.Structure.Headers.Select(h => h.Text);
        var folhaCodigo = ExtractCode(folhaParagrafos.Concat(headerTextos), pdaRx);

        var stem = string.IsNullOrEmpty(ctx.Structure.FileName)
            ? null : Path.GetFileNameWithoutExtension(ctx.Structure.FileName);

        var violations = new List<Violation>();

        if (string.IsNullOrEmpty(quadroCodigo))
        {
            violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                "Codificação não encontrada no quadro 'Características do Documento'.",
                new ViolationLocation(null, null, null, "Quadro Características")));
        }

        if (string.IsNullOrEmpty(folhaCodigo))
        {
            violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                "Codificação não encontrada na folha de rosto / capa.",
                new ViolationLocation(null, null, null, "Folha de Rosto")));
        }

        if (!string.IsNullOrEmpty(quadroCodigo)
            && !string.IsNullOrEmpty(folhaCodigo)
            && !NormalizeCode(WithoutRevisionSuffix(quadroCodigo))
                .Equals(NormalizeCode(WithoutRevisionSuffix(folhaCodigo)), StringComparison.Ordinal))
        {
            violations.Add(new Violation(Ref.ToString(), Severity.Error,
                $"Codificação diverge entre folha de rosto ('{folhaCodigo}') e quadro Características ('{quadroCodigo}').",
                new ViolationLocation(null, null, null, "Folha de Rosto × Quadro")));
        }

        if (!string.IsNullOrEmpty(stem) && !string.IsNullOrEmpty(quadroCodigo))
        {
            var stemNorm = NormalizeCode(stem);
            var codeNorm = NormalizeCode(WithoutRevisionSuffix(quadroCodigo));

            // Direct match (com/sem revisão) ou contenção mútua.
            var direct = stemNorm.Contains(codeNorm, StringComparison.Ordinal)
                         || codeNorm.Contains(stemNorm, StringComparison.Ordinal);

            // Codificação alternativa: o stem do arquivo aparece literalmente em alguma
            // outra célula do quadro Características (típico em projetos com código
            // magnético/Meridian distinto do código PdA).
            var alternative = table is not null && table.Cells.Any(c =>
                !string.IsNullOrWhiteSpace(c.Text)
                && NormalizeCode(c.Text).Contains(stemNorm, StringComparison.Ordinal));

            if (!direct && !alternative)
            {
                violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                    $"Nome do arquivo ('{stem}') não reflete a codificação do quadro ('{quadroCodigo}').",
                    new ViolationLocation(null, null, null, "Arquivo")));
            }
        }

        var status = violations.Count == 0 ? CheckStatus.Passed
                   : violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : CheckStatus.Skipped;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }

    private static string? ExtractCode(IEnumerable<string> texts, string regex)
    {
        var pattern = SearchPattern(regex);
        foreach (var t in texts)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            var m = Regex.Match(t, pattern, Opt);
            if (m.Success) return m.Value;
        }
        return null;
    }

    private static string SearchPattern(string regex)
    {
        var p = regex.Trim();
        if (p.StartsWith('^')) p = p[1..];
        if (p.EndsWith('$')) p = p[..^1];
        return p + @"(?:-\d+)?";
    }

    private static string WithoutRevisionSuffix(string value) =>
        Regex.Replace(value.Trim(), @"-\d+$", string.Empty, Opt);

    private static string NormalizeCode(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
