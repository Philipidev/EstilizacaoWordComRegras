using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.4.3 — o índice deve contemplar todos os títulos e subtítulos até o nível 2
/// (níveis inferiores são facultativos).
/// PS-002 4.3.4.4 — os elementos gráficos (figuras, fotos, gráficos e tabelas) devem estar
/// relacionados no índice, abaixo dos títulos.
/// <para>
/// As entradas do índice são os parágrafos com estilo TOC1/TOC2/…; a comparação é feita
/// por texto normalizado, porque no índice o título vem acompanhado de pontilhado e número
/// de página.
/// </para>
/// </summary>
public sealed class IndiceConteudoCheck : IRuleCheck
{
    private static readonly Regex LegendaElementoGrafico = new(
        @"^\s*(figura|foto|gr[áa]fico|tabela|quadro|desenho)\s*[\d]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _item;

    public IndiceConteudoCheck(ChecklistPadrao padrao = ChecklistPadrao.Pda, string item = "4.3.4.3")
    {
        _item = item;
        Ref = new ChecklistRef("PS-002", item, padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var entradas = DocumentoTexto.EntradasIndice(ctx.Structure);
        if (entradas.Count == 0)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Índice não localizado (nenhum parágrafo com estilo TOC)."));
        }

        var indiceNorm = entradas.Select(e => DocumentoTexto.NormalizarTitulo(e.Text)).ToList();

        return Task.FromResult(_item.StartsWith("4.3.4.4", StringComparison.Ordinal)
            ? AvaliarElementosGraficos(ctx, indiceNorm, entradas.Count)
            : AvaliarTitulos(ctx, indiceNorm, entradas.Count));
    }

    private RuleCheckResult AvaliarTitulos(DocumentContext ctx, List<string> indiceNorm, int totalEntradas)
    {
        // Nível 0 e 1 = "títulos e subtítulos até o nível 2" na terminologia do PS-002.
        var obrigatorios = DocumentoTexto.Titulos(ctx.Structure)
            .Where(t => t.OutlineLevel <= 1)
            .ToList();

        if (obrigatorios.Count == 0)
        {
            return new RuleCheckResult(Ref, CheckStatus.Skipped, Array.Empty<Violation>(),
                Note: "Documento sem títulos de nível 1 ou 2.");
        }

        var ausentes = obrigatorios
            .Where(t => !ApareceNoIndice(DocumentoTexto.NormalizarTitulo(t.Text), indiceNorm))
            .ToList();

        var violations = ausentes.Select(t => new Violation(
            Ref.ToString(), Severity.Error,
            $"Título de nível {t.OutlineLevel + 1} ausente do índice: '{Resumo(t.Text)}'.",
            new ViolationLocation(t.ParagraphId, null, t.SectionIndex, "Índice"))).ToList();

        var status = violations.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed;
        return new RuleCheckResult(Ref, status, violations,
            Note: $"{obrigatorios.Count} títulos até nível 2 confrontados com {totalEntradas} entradas de índice.");
    }

    private RuleCheckResult AvaliarElementosGraficos(DocumentContext ctx, List<string> indiceNorm, int totalEntradas)
    {
        // As próprias entradas da lista de figuras casam com o regex de legenda — excluí-las
        // evita comparar o índice consigo mesmo.
        var legendas = ctx.Structure.Paragraphs
            .Where(p => !string.IsNullOrWhiteSpace(p.Text)
                     && !DocumentoTexto.EhEntradaIndice(p)
                     && LegendaElementoGrafico.IsMatch(p.Text))
            .ToList();

        if (legendas.Count == 0)
        {
            return new RuleCheckResult(Ref, CheckStatus.Skipped, Array.Empty<Violation>(),
                Note: "Nenhuma legenda de figura/tabela/gráfico localizada.");
        }

        var ausentes = legendas
            .Where(l => !ApareceNoIndice(DocumentoTexto.NormalizarTitulo(l.Text), indiceNorm))
            .ToList();

        var violations = new List<Violation>();
        if (ausentes.Count > 0)
        {
            var amostra = string.Join("; ", ausentes.Take(3).Select(l => $"'{Resumo(l.Text)}'"));
            violations.Add(new Violation(Ref.ToString(), Severity.Error,
                $"{ausentes.Count} de {legendas.Count} elementos gráficos não constam do índice. " +
                $"Exemplos: {amostra}",
                new ViolationLocation(ausentes[0].ParagraphId, null, ausentes[0].SectionIndex,
                                      "Relação de elementos gráficos")));
        }

        var status = violations.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed;
        return new RuleCheckResult(Ref, status, violations,
            Note: $"{legendas.Count} elementos gráficos confrontados com {totalEntradas} entradas de índice.");
    }

    /// <summary>
    /// Uma entrada do índice contém o título mais pontilhado e número de página, então a
    /// comparação é por continência do texto normalizado, em qualquer direção.
    /// </summary>
    private static bool ApareceNoIndice(string alvoNorm, IEnumerable<string> indiceNorm)
    {
        if (alvoNorm.Length == 0) return true;
        return indiceNorm.Any(e => e.Contains(alvoNorm, StringComparison.Ordinal)
                                || (alvoNorm.Length > 12 && alvoNorm.Contains(e, StringComparison.Ordinal) && e.Length > 12));
    }

    private static string Resumo(string texto)
    {
        var t = texto.Trim().Replace("\n", " ");
        return t.Length <= 60 ? t : t[..60] + "…";
    }
}
