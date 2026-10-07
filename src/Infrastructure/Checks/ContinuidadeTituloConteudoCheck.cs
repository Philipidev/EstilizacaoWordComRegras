using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.3 (letra a) — Continuidade de título e conteúdo: o título tem de estar na mesma
/// página do conteúdo.
/// <para>
/// O OOXML não guarda paginação, mas guarda dois sinais:
/// </para>
/// <list type="bullet">
/// <item>quebra de página explícita depois do texto do título (ou em parágrafos vazios logo
/// abaixo dele) — o conteúdo vai para a página seguinte em qualquer impressão: <b>erro</b>;</item>
/// <item><c>w:lastRenderedPageBreak</c> no início do primeiro parágrafo de conteúdo — na última
/// vez que o Word paginou, o título ficou no pé de uma página e o conteúdo começou na outra.
/// Depende de fonte e impressora, então é <b>aviso</b>.</item>
/// </list>
/// <para>
/// Títulos são reconhecidos pelo nível de estrutura (<c>outlineLvl</c>), não pelo nome do
/// estilo: os documentos reais usam estilos próprios ("PDA-T1", "Ttulo1MRN", "Estilo1"), e a
/// versão anterior, que exigia nomes começando com "Heading"/"Título", não via título nenhum e
/// aprovava sempre.
/// </para>
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
        var indiceDoPrimeiroTitulo = -1;
        for (var k = 0; k < paragraphs.Count; k++)
            if (EhTitulo(paragraphs[k])) { indiceDoPrimeiroTitulo = k; break; }

        if (indiceDoPrimeiroTitulo < 0)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(), Note: "Nenhum título (nível de estrutura) localizado no documento."));
        }

        var violations = new List<Violation>();
        var titulos = 0;
        for (var i = indiceDoPrimeiroTitulo; i < paragraphs.Count - 1; i++)
        {
            var titulo = paragraphs[i];
            if (!EhTitulo(titulo)) continue;
            titulos++;

            if (titulo.PageBreakAfterText)
            {
                violations.Add(Erro(titulo, "termina com quebra de página — o conteúdo vai para a página seguinte."));
                continue;
            }

            // Parágrafos vazios logo abaixo do título até o primeiro conteúdo.
            var j = i + 1;
            var quebraExplicita = false;
            while (j < paragraphs.Count && string.IsNullOrWhiteSpace(paragraphs[j].Text) && paragraphs[j].ImageCount == 0)
            {
                if (paragraphs[j].EndsWithPageBreak) quebraExplicita = true;
                j++;
            }
            if (j >= paragraphs.Count) break;

            var conteudo = paragraphs[j];
            if (quebraExplicita)
            {
                violations.Add(Erro(titulo, "é seguido de parágrafos vazios com quebra de página — o conteúdo fica na página seguinte."));
            }
            else if (conteudo.RenderedPageBreakAtStart || paragraphs.Skip(i + 1).Take(j - i).Any(p => p.RenderedPageBreakAtStart))
            {
                violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                    $"Título '{Trunc(titulo.Text, 60)}' ficou no pé da página, separado do conteúdo, na última " +
                    "paginação do Word. Use \"Manter com o próximo\" no estilo de título ou ajuste a quebra.",
                    new ViolationLocation(titulo.ParagraphId, null, titulo.SectionIndex, "Continuidade título/conteúdo")));
            }
        }

        var status = violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : violations.Count > 0 ? CheckStatus.Skipped
                   : CheckStatus.Passed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations, Note: $"{titulos} título(s) verificados."));

        Violation Erro(ExtractedParagraph titulo, string problema) =>
            new(Ref.ToString(), Severity.Error, $"Título '{Trunc(titulo.Text, 60)}' {problema}",
                new ViolationLocation(titulo.ParagraphId, null, titulo.SectionIndex, "Continuidade título/conteúdo"));
    }

    /// <summary>Título = parágrafo com texto, fora de tabela e de índice, com nível de estrutura 1–9.</summary>
    private static bool EhTitulo(ExtractedParagraph p) =>
        p.OutlineLevel is >= 0 and <= 8
        && !p.IsInTable
        && !string.IsNullOrWhiteSpace(p.Text)
        && !DocumentoTexto.EhEntradaIndice(p);

    private static string Trunc(string s, int n)
    {
        var t = s.Replace('\n', ' ').Replace('\t', ' ').Trim();
        return t.Length <= n ? t : t[..n] + "…";
    }
}
