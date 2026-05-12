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

        var status = violations.Count == 0 ? CheckStatus.Passed
                   : violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : CheckStatus.Skipped;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }
}
