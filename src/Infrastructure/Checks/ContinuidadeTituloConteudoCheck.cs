using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.3 (letra a) — Continuidade de título e conteúdo (mesma página).
/// A extração via Open XML não fornece informação de paginação (page layout é
/// calculado pelo Word em runtime). Verificação heurística: detectar quebras de
/// página explícitas <c>w:br w:type="page"</c> imediatamente após parágrafos com
/// estilo de heading. Caso a heurística não seja aplicável, retorna Skipped com nota.
/// </summary>
public sealed class ContinuidadeTituloConteudoCheck : IRuleCheck
{
    public ContinuidadeTituloConteudoCheck(ChecklistPadrao padrao = ChecklistPadrao.Cliente)
    {
        Ref = new ChecklistRef("PS-002", "4.3.3 (letra a)", padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var paragraphs = ctx.Structure.Paragraphs;
        if (paragraphs.Count == 0)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(), Note: "Documento sem parágrafos extraídos."));
        }

        var violations = new List<Violation>();
        for (int i = 0; i < paragraphs.Count - 1; i++)
        {
            var p = paragraphs[i];
            if (!IsTitulo(p.StyleId)) continue;

            // Caso 1: o próprio parágrafo título termina com page break → conteúdo fica na próxima página.
            if (p.EndsWithPageBreak)
            {
                violations.Add(new Violation(Ref.ToString(), Severity.Error,
                    $"Título '{Trunc(p.Text, 60)}' termina com quebra de página — conteúdo na página seguinte.",
                    new ViolationLocation(p.ParagraphId, null, null, "Continuidade título/conteúdo")));
                continue;
            }

            // Caso 2: parágrafos em branco logo após o título, e o último deles inicia
            // antes de uma quebra → mesma situação (espaços empurrando o conteúdo).
            int j = i + 1;
            while (j < paragraphs.Count && string.IsNullOrWhiteSpace(paragraphs[j].Text)
                   && !IsTitulo(paragraphs[j].StyleId))
            {
                if (paragraphs[j].EndsWithPageBreak)
                {
                    violations.Add(new Violation(Ref.ToString(), Severity.Error,
                        $"Título '{Trunc(p.Text, 60)}' seguido apenas de parágrafos vazios antes de quebra de página.",
                        new ViolationLocation(p.ParagraphId, null, null, "Continuidade título/conteúdo")));
                    break;
                }
                j++;
            }
        }

        var status = violations.Count == 0 ? CheckStatus.Passed
                   : violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : CheckStatus.Passed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }

    private static bool IsTitulo(string? styleId)
    {
        var s = styleId ?? string.Empty;
        return s.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("Titulo", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("Título", StringComparison.OrdinalIgnoreCase);
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
