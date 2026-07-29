using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.6.3 (PdA) / 4.3.6.4 (Cliente) — Figuras, fotos, gráficos e tabelas:
/// centralizados, com títulos posicionados corretamente (tabelas acima, demais abaixo)
/// e formatação consistente.
/// <para>
/// A posição da legenda em relação à tabela é deduzida da sequência de parágrafos: um bloco
/// contíguo com <c>IsInTable</c> é uma tabela, então a legenda "Tabela N" deve vir logo
/// antes desse bloco e a legenda "Figura N" logo depois de um parágrafo com imagem.
/// </para>
/// </summary>
public sealed class ElementosGraficosCheck : IRuleCheck
{
    private static readonly Regex LegendaTabela = new(
        @"^\s*(tabela|quadro)\s*\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LegendaFigura = new(
        @"^\s*(figura|foto|gr[áa]fico|desenho)\s*\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const int ToleranciaDefault = 20;

    public ElementosGraficosCheck(ChecklistPadrao padrao = ChecklistPadrao.Cliente, string item = "4.3.6.4")
    {
        Ref = new ChecklistRef("PS-002", item, padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var paragrafos = ctx.Structure.Paragraphs;
        // Imagens dentro de tabela seguem o alinhamento da célula, não o da página.
        var comImagem = paragrafos.Where(p => p.ImageCount > 0 && !p.IsInTable).ToList();
        // Entradas do índice (lista de figuras/tabelas) casam com o regex de legenda mas não
        // são legendas — elas carregam PAGEREF e precisam ser excluídas.
        var legendas = paragrafos
            .Select((p, idx) => (Paragrafo: p, Index: idx))
            .Where(x => !string.IsNullOrWhiteSpace(x.Paragrafo.Text)
                     && !DocumentoTexto.EhEntradaIndice(x.Paragrafo)
                     && (LegendaTabela.IsMatch(x.Paragrafo.Text) || LegendaFigura.IsMatch(x.Paragrafo.Text)))
            .ToList();

        if (comImagem.Count == 0 && legendas.Count == 0)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Documento sem figuras, tabelas legendadas ou imagens no corpo."));
        }

        var tolerancia = ctx.Profile.GetInt("elementosGraficos.toleranciaPercentual") ?? ToleranciaDefault;
        var violations = new List<Violation>();

        AvaliarCentralizacao(violations, comImagem, tolerancia);
        AvaliarPosicaoLegendas(violations, paragrafos, legendas, tolerancia);

        var status = violations.Count == 0 ? CheckStatus.Passed
                   : violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : CheckStatus.Skipped;

        return Task.FromResult(new RuleCheckResult(Ref, status, violations,
            Note: $"{comImagem.Count} parágrafos com imagem e {legendas.Count} legendas avaliados."));
    }

    private void AvaliarCentralizacao(
        List<Violation> violations, IReadOnlyList<ExtractedParagraph> comImagem, int tolerancia)
    {
        var comAlinhamento = comImagem.Where(p => !string.IsNullOrWhiteSpace(p.Alignment)).ToList();
        if (comAlinhamento.Count == 0) return;

        var foraDoCentro = comAlinhamento
            .Where(p => !string.Equals(p.Alignment, "center", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (foraDoCentro.Count == 0) return;

        var percentual = foraDoCentro.Count * 100.0 / comAlinhamento.Count;
        if (percentual <= tolerancia) return;

        violations.Add(new Violation(Ref.ToString(), Severity.Warning,
            $"{foraDoCentro.Count} de {comAlinhamento.Count} elementos gráficos ({percentual:0.#}%) " +
            $"não estão centralizados (alinhamentos: " +
            $"{string.Join(", ", foraDoCentro.Select(p => p.Alignment).Distinct())}).",
            new ViolationLocation(foraDoCentro[0].ParagraphId, null, foraDoCentro[0].SectionIndex,
                                  "Elementos gráficos")));
    }

    /// <summary>
    /// Tabelas e figuras costumam ser separadas da legenda por parágrafos vazios de
    /// espaçamento, então a busca percorre uma janela em vez do vizinho imediato.
    /// </summary>
    private const int JanelaBusca = 3;

    private void AvaliarPosicaoLegendas(
        List<Violation> violations,
        IReadOnlyList<ExtractedParagraph> paragrafos,
        IReadOnlyList<(ExtractedParagraph Paragrafo, int Index)> legendas,
        int tolerancia)
    {
        var forasDePosicao = new List<ExtractedParagraph>();

        foreach (var (legenda, idx) in legendas)
        {
            var ehTabela = LegendaTabela.IsMatch(legenda.Text);
            var correta = ehTabela
                // Legenda de tabela vem ACIMA: procurar a tabela logo abaixo.
                ? legenda.IsInTable || ExisteNaJanela(paragrafos, idx, +1, p => p.IsInTable)
                // Legenda de figura vem ABAIXO: procurar a imagem logo acima.
                : legenda.ImageCount > 0 || ExisteNaJanela(paragrafos, idx, -1, p => p.ImageCount > 0);

            if (!correta) forasDePosicao.Add(legenda);
        }

        if (forasDePosicao.Count == 0 || legendas.Count == 0) return;

        var percentual = forasDePosicao.Count * 100.0 / legendas.Count;
        if (percentual <= tolerancia) return;

        // Um achado agregado, não um por legenda: 100+ comentários no .docx seriam inúteis.
        var amostra = string.Join("; ", forasDePosicao.Take(3).Select(l => $"'{Resumo(l.Text)}'"));
        violations.Add(new Violation(Ref.ToString(), Severity.Warning,
            $"{forasDePosicao.Count} de {legendas.Count} legendas ({percentual:0.#}%) não estão " +
            $"adjacentes ao respectivo elemento (tabela acima, figura abaixo). Exemplos: {amostra}",
            new ViolationLocation(forasDePosicao[0].ParagraphId, null, forasDePosicao[0].SectionIndex,
                                  "Legendas de elementos gráficos")));
    }

    private static bool ExisteNaJanela(
        IReadOnlyList<ExtractedParagraph> paragrafos, int origem, int passo,
        Func<ExtractedParagraph, bool> criterio)
    {
        var examinados = 0;
        for (var i = origem + passo; i >= 0 && i < paragrafos.Count && examinados < JanelaBusca; i += passo)
        {
            var p = paragrafos[i];
            if (criterio(p)) return true;
            // Parágrafos vazios de espaçamento não consomem a janela.
            if (!string.IsNullOrWhiteSpace(p.Text)) examinados++;
        }
        return false;
    }

    private static string Resumo(string texto)
    {
        var t = texto.Trim().Replace("\n", " ");
        return t.Length <= 60 ? t : t[..60] + "…";
    }
}
