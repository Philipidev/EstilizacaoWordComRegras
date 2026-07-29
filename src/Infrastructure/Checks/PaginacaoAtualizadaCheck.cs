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
    private static readonly Regex PageWithTotal = new(@"\b\d+\s*(?:/|de)\s*\d+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PageWord = new(@"P[áa]gina\s*\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BareNumber = new(@"\b\d{1,4}\b", RegexOptions.Compiled);

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
                // 2) Fallback: texto literal "Página X de Y" ou número avulso.
                var temTexto = headers.Concat(footers).Any(h =>
                    !string.IsNullOrWhiteSpace(h.Text) &&
                    (PageWithTotal.IsMatch(h.Text)
                     || PageWord.IsMatch(h.Text)
                     || BareNumber.IsMatch(h.Text)));

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

        violations.AddRange(TotalDePaginasLiteral(ctx));

        var status = violations.Count == 0 ? CheckStatus.Passed
                   : violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : CheckStatus.Skipped;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }

    /// <summary>
    /// Total de páginas gravado como texto fixo em vez de campo <c>NUMPAGES</c>.
    /// <para>
    /// Um cabeçalho "FL.: {PAGE}/99" tem a página atualizada pelo Word e o total congelado: ao
    /// repaginar, o "99" não acompanha. Quando o quadro "Características" tem folha <b>marcada</b>
    /// acima desse total, a divergência deixa de ser risco e vira fato — e é aí que a severidade
    /// sobe de aviso para erro. A comparação é com a folha marcada, não com a maior impressa:
    /// a grade vem pré-impressa até um número redondo e as linhas finais costumam estar vazias.
    /// </para>
    /// Decidir isto por OOXML tira do avaliador semântico uma leitura que ele vinha errando:
    /// o "7" de "FL.: 7/99" é o valor cacheado do campo PAGE, igual em todas as partes de
    /// cabeçalho, e era reportado como "paginação repetida em várias seções".
    /// </summary>
    private IEnumerable<Violation> TotalDePaginasLiteral(DocumentContext ctx)
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
            .ToList();

        if (totais.Count == 0) yield break;

        var aliases = DocumentoTexto.Lista(ctx.Profile.Get("quadroCaracteristicas.aliases"),
            "Características do Documento|Quadro de Características|Características");
        var quadro = QuadroCaracteristicas.Find(ctx.Structure, aliases);
        var maiorFolha = quadro is null ? null : QuadroCaracteristicas.MaiorFolhaMarcadaNaGrade(quadro);

        var menorTotal = totais.Min();
        var listados = string.Join(", ", totais.OrderBy(t => t));

        if (maiorFolha is { } folha && folha > menorTotal)
        {
            yield return new Violation(
                RuleId: Ref.ToString(),
                Severity: Severity.Error,
                Message: $"O total de páginas no cabeçalho é o texto fixo \"{listados}\", não um campo " +
                         $"NUMPAGES, e o quadro 'Características do Documento' tem a folha {folha} marcada. " +
                         "A paginação não acompanhou a evolução do documento.",
                Location: new ViolationLocation(null, "header", null, "Numeração de páginas"));
            yield break;
        }

        yield return new Violation(
            RuleId: Ref.ToString(),
            Severity: Severity.Warning,
            Message: $"O total de páginas no cabeçalho está gravado como texto fixo (\"{listados}\") " +
                     "em vez de campo NUMPAGES: ele não é atualizado ao repaginar o documento.",
            Location: new ViolationLocation(null, "header", null, "Numeração de páginas"));
    }

    private static readonly Regex Total = new(@"(?:/|de)\s*(\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
}
