using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.6.5 (PdA) — Numeração das páginas: no canto superior direito, ausente na
/// folha de rosto e na página do quadro "Características do Documento".
/// <para>
/// "Superior" é verificável: o campo <c>PAGE</c> tem de estar num cabeçalho, não num rodapé.
/// "Direito" depende do layout do cliente — nos documentos de referência o número fica numa
/// célula da tarja, cujo alinhamento não corresponde ao da página. Por isso só é cobrado
/// quando o profile declara <c>numeracao.exigirDireita=true</c>.
/// </para>
/// </summary>
public sealed class NumeracaoPaginasCheck : IRuleCheck
{
    private static readonly string[] CamposPagina = ["PAGE", "NUMPAGES"];

    public NumeracaoPaginasCheck(ChecklistPadrao padrao = ChecklistPadrao.Pda, string item = "4.3.6.5")
    {
        Ref = new ChecklistRef("PS-002", item, padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var headers = ctx.Structure.Headers;
        var footers = ctx.Structure.Footers;

        var headersComPagina = headers.Where(TemCampoPagina).ToList();
        var footersComPagina = footers.Where(TemCampoPagina).ToList();

        if (headersComPagina.Count == 0 && footersComPagina.Count == 0)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Nenhum campo PAGE/NUMPAGES localizado em cabeçalhos ou rodapés."));
        }

        var violations = new List<Violation>();

        // --- Posição vertical: deve ser no cabeçalho ---
        if (headersComPagina.Count == 0)
        {
            violations.Add(new Violation(Ref.ToString(), Severity.Error,
                "Numeração de páginas está no rodapé; o padrão exige o canto superior da página.",
                new ViolationLocation(null, "footer", null, "Numeração de páginas")));
        }

        // --- Posição horizontal: só cobrada quando o profile exige ---
        if (string.Equals(ctx.Profile.Get("numeracao.exigirDireita"), "true", StringComparison.OrdinalIgnoreCase)
            && headersComPagina.Count > 0)
        {
            var semDireita = headersComPagina
                .Where(h => !h.ParagraphAlignments.Any(a =>
                    string.Equals(a, "right", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a, "end", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (semDireita.Count > 0)
            {
                violations.Add(new Violation(Ref.ToString(), Severity.Error,
                    $"{semDireita.Count} cabeçalho(s) com numeração fora do canto direito.",
                    new ViolationLocation(null, semDireita[0].Kind, semDireita[0].SectionIndex,
                                          "Numeração de páginas")));
            }
        }

        // --- Folha de rosto (seção 0) não deve ter numeração ---
        // Só faz sentido quando a capa é uma seção própria: num documento de seção única a
        // seção 0 é o documento inteiro, e exigir ausência de numeração ali contradiz a
        // própria regra de que as páginas devem ser numeradas.
        if (ctx.Structure.Sections.Count > 1)
        {
            var capaNumerada = headersComPagina.Concat(footersComPagina)
                .Where(h => h.SectionIndex == 0)
                .ToList();
            if (capaNumerada.Count > 0)
            {
                violations.Add(new Violation(Ref.ToString(), Severity.Error,
                    "A folha de rosto (primeira seção) contém numeração de páginas.",
                    new ViolationLocation(null, capaNumerada[0].Kind, 0, "Folha de rosto")));
            }
        }

        var status = violations.Count == 0 ? CheckStatus.Passed
                   : violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : CheckStatus.Skipped;

        return Task.FromResult(new RuleCheckResult(Ref, status, violations,
            Note: $"Campo de página em {headersComPagina.Count} cabeçalho(s) e " +
                  $"{footersComPagina.Count} rodapé(s)."));
    }

    private static bool TemCampoPagina(ExtractedHeaderFooter hf) =>
        hf.FieldCodes.Any(c => CamposPagina.Contains(c, StringComparer.OrdinalIgnoreCase));
}
