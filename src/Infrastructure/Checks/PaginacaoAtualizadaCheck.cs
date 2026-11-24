using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.3 (letra f) Cliente — Paginação atualizada. Verifica se o rodapé
/// contém numeração de páginas (texto compatível com "Página X de Y" ou ao menos
/// um número isolado). Como a extração trabalha com texto, o teste é por presença
/// de padrão numérico nos rodapés extraídos.
/// </summary>
public sealed class PaginacaoAtualizadaCheck : IRuleCheck
{
    // "X/Y" ou "X de Y" que não é pedaço de data: sem barra ou dígito colado dos dois lados.
    // Sem isso, "15/03/2024" num cabeçalho virava total fixo "3".
    private static readonly Regex PageWithTotal = new(@"(?<![\d/])\d+\s*(?:/|de)\s*\d+(?![\d/])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Numeração rotulada ("FL.: 7/99", "Folha 3 de 10", "Página 2"). O fallback anterior aceitava
    // qualquer número de até 4 dígitos — o "26" de "UP-26" bastava para dar a paginação por presente.
    private static readonly Regex PageWord = new(@"\b(?:P[áa]g(?:ina)?|FL|Folha)\.?\s*:?\s*\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public PaginacaoAtualizadaCheck(
        ChecklistPadrao padrao = ChecklistPadrao.Cliente,
        string item = "4.3.3 (letra f)")
    {
        Ref = new ChecklistRef("PS-002", item, padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var footers = ctx.Structure.Footers;
        var headers = ctx.Structure.Headers;
        var violations = new List<Violation>();

        // 1) Forma mais confiável: campo OOXML PAGE em algum header ou footer.
        var temCampoPage = headers.Concat(footers).Any(h =>
            h.FieldCodes.Any(c =>
                c.Equals("PAGE", StringComparison.OrdinalIgnoreCase)
                || c.Equals("NUMPAGES", StringComparison.OrdinalIgnoreCase)));

        if (!temCampoPage)
        {
            if (footers.Count == 0 && headers.Count == 0)
            {
                violations.Add(new Violation(
                    RuleId: Ref.ToString(),
                    Severity: Severity.Warning,
                    Message: "Documento não possui cabeçalhos/rodapés — não foi possível verificar paginação.",
                    Location: new ViolationLocation(null, "footer", null, "Rodapé")));
            }
            else
            {
                // 2) Fallback: numeração rotulada no texto ("Página X de Y", "FL. X/Y").
                var temTexto = headers.Concat(footers).Any(h =>
                    !string.IsNullOrWhiteSpace(h.Text) && PageWord.IsMatch(h.Text));

                if (!temTexto)
                {
                    violations.Add(new Violation(
                        RuleId: Ref.ToString(),
                        Severity: Severity.Warning,
                        Message: "Não foi localizado campo PAGE/NUMPAGES nem numeração textual em cabeçalho/rodapé.",
                        Location: new ViolationLocation(null, "footer", null, "Rodapé")));
                }
            }
        }

        var (doTotal, nota) = TotalDePaginasLiteral(ctx);
        violations.AddRange(doTotal);

        var status = violations.Count == 0 ? CheckStatus.Passed
                   : violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : CheckStatus.Skipped;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations, Note: nota));
    }

    /// <summary>
    /// Total de páginas gravado como texto fixo ("FL.: {PAGE}/144") em vez de campo
    /// <c>NUMPAGES</c>.
    /// <para>
    /// A contagem automática não é obrigatória: o total pode ser digitado, desde que esteja
    /// certo. E "certo" exclui o quadro "Características do Documento" — por isso um
    /// <c>NUMPAGES</c>, que conta todas as páginas, nem sempre serve. O total digitado é
    /// conferido contra duas fontes:
    /// </para>
    /// <list type="bullet">
    /// <item>a contagem do Word: o total de páginas que ele gravou em <c>docProps/app.xml</c> no
    /// último salvamento, menos as páginas ocupadas pelo quadro (medidas pelas quebras de
    /// página renderizadas dentro dele);</item>
    /// <item>a grade de controle de páginas do quadro: a maior página marcada com "x".</item>
    /// </list>
    /// Folha marcada acima do total é erro (fato). Total diferente da contagem do Word é erro
    /// quando a grade concorda com a contagem e aviso quando só a contagem diverge — o
    /// <c>app.xml</c> pode estar defasado se o documento foi salvo por outra ferramenta.
    /// <para>
    /// Decidir isto por OOXML tira do avaliador semântico uma leitura que ele vinha errando:
    /// o "7" de "FL.: 7/99" é o valor cacheado do campo PAGE, igual em todas as partes de
    /// cabeçalho, e era reportado como "paginação repetida em várias seções".
    /// </para>
    /// </summary>
    private (IReadOnlyList<Violation> Violacoes, string? Nota) TotalDePaginasLiteral(DocumentContext ctx)
    {
        var comPageSemNumPages = ctx.Structure.Headers.Concat(ctx.Structure.Footers)
            .Where(h => h.FieldCodes.Any(c => c.Equals("PAGE", StringComparison.OrdinalIgnoreCase))
                     && !h.FieldCodes.Any(c => c.Equals("NUMPAGES", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var totais = comPageSemNumPages
            .Select(h => PageWithTotal.Match(h.Text ?? string.Empty))
            .Where(m => m.Success)
            .Select(m => Total.Match(m.Value))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        if (totais.Count == 0) return (Array.Empty<Violation>(), null);

        var aliases = DocumentoTexto.Lista(ctx.Profile.Get("quadroCaracteristicas.aliases"),
            "Características do Documento|Quadro de Características|Características");
        var quadro = QuadroCaracteristicas.Find(ctx.Structure, aliases);
        var maiorFolha = quadro is null ? null : QuadroCaracteristicas.MaiorFolhaMarcadaNaGrade(quadro);

        var registradas = ctx.Structure.Properties?.Pages;
        var doQuadro = quadro is null ? 0 : QuadroCaracteristicas.PaginasDoQuadro(ctx.Structure, quadro) ?? 1;
        int? contagem = registradas is { } r && r > doQuadro ? r - doQuadro : null;
        var origemContagem = registradas is { } reg
            ? $"o Word registrou {reg} página(s) no último salvamento" +
              (quadro is null ? "" : $", das quais {doQuadro} do quadro 'Características do Documento'") +
              $", o que dá {contagem} página(s) numerada(s)"
            : null;

        var violacoes = new List<Violation>();
        var local = new ViolationLocation(null, "header", null, "Numeração de páginas");
        var listados = string.Join(", ", totais);

        if (totais.Count > 1)
            violacoes.Add(new Violation(Ref.ToString(), Severity.Warning,
                $"Os cabeçalhos indicam totais de páginas diferentes ({listados}).", local));

        if (maiorFolha is { } folha && folha > totais[0])
        {
            violacoes.Add(new Violation(Ref.ToString(), Severity.Error,
                $"O total de páginas no cabeçalho é o texto fixo \"{listados}\" (sem campo NUMPAGES), e o quadro " +
                $"'Características do Documento' tem a folha {folha} marcada. " +
                "A paginação não acompanhou a evolução do documento.", local));
            return (violacoes, null);
        }

        var notas = new List<string>();
        foreach (var total in totais)
        {
            if (contagem == total)
            {
                notas.Add($"Total fixo {total} confere: {origemContagem}.");
                continue;
            }

            if (contagem is null)
            {
                if (maiorFolha == total)
                {
                    notas.Add($"Total fixo {total} confere com a última página marcada na grade do quadro (sem contagem de páginas do Word para comparar).");
                    continue;
                }
                var semContagem = registradas is null
                    ? "o documento não traz a contagem de páginas do Word"
                    : $"a contagem do Word ({registradas} página(s)) não é maior que as páginas do quadro ({doQuadro})";
                violacoes.Add(new Violation(Ref.ToString(), Severity.Warning,
                    $"O total de páginas indicado no cabeçalho ({total}) é texto fixo e não pôde ser conferido: {semContagem}" +
                    (maiorFolha is { } m ? $" e a grade do quadro marca até a página {m}." : " e a grade do quadro não foi lida.") +
                    " Confira o total manualmente, sem contar o quadro 'Características do Documento'.", local));
                continue;
            }

            var gradeConcordaComContagem = maiorFolha == contagem;
            var gradeConcordaComTotal = maiorFolha == total;
            var mensagem = $"O total de páginas indicado no cabeçalho ({total}) não confere: {origemContagem}" +
                           (maiorFolha is { } g ? $"; a grade do quadro marca até a página {g}." : ".") +
                           " O quadro 'Características do Documento' não entra na contagem.";
            if (gradeConcordaComTotal)
                mensagem += " Se o documento foi repaginado depois de salvo por outra ferramenta, salve-o no Word e revise de novo.";

            violacoes.Add(new Violation(Ref.ToString(),
                gradeConcordaComContagem ? Severity.Error : Severity.Warning, mensagem, local));
        }

        return (violacoes, notas.Count == 0 ? null : string.Join(" ", notas));
    }

    private static readonly Regex Total = new(@"(?:/|de)\s*(\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
}
