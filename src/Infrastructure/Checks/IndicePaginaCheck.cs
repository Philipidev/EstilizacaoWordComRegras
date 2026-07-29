using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.4.1 (PdA) — A página do índice deve conter o nome da empresa contratante e a
/// descrição/área do projeto, conforme o padrão de formatação estabelecido.
/// <para>
/// Depende do profile: sem <c>folhaRosto.contratante</c> declarado não há o que confrontar,
/// e o resultado é Skipped em vez de um falso positivo.
/// </para>
/// </summary>
public sealed class IndicePaginaCheck : IRuleCheck
{
    public IndicePaginaCheck(ChecklistPadrao padrao = ChecklistPadrao.Pda, string item = "4.3.4.1")
    {
        Ref = new ChecklistRef("PS-002", item, padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var contratante = ctx.Profile.Get("folhaRosto.contratante");
        if (string.IsNullOrWhiteSpace(contratante))
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Parâmetro 'folhaRosto.contratante' não definido no profile."));
        }

        var entradas = DocumentoTexto.EntradasIndice(ctx.Structure);
        if (entradas.Count == 0)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Índice não localizado (nenhum parágrafo com estilo TOC)."));
        }

        // A "página do índice" é aproximada pela seção onde o índice está: a identificação
        // pode vir no cabeçalho da seção ou nos parágrafos ao redor das entradas do TOC.
        var secaoDoIndice = entradas[0].SectionIndex;
        var indiceInicio = ctx.Structure.Paragraphs.ToList().FindIndex(p => p.ParagraphId == entradas[0].ParagraphId);
        var contexto = ctx.Structure.Paragraphs
            .Skip(Math.Max(0, indiceInicio - 15))
            .Take(20)
            .Select(p => p.Text)
            .Concat(ctx.Structure.Headers.Where(h => h.SectionIndex == secaoDoIndice).Select(h => h.Text));

        var contextoNorm = DocumentoTexto.Normalizar(string.Join(" ", contexto));
        var alvos = DocumentoTexto.Lista(contratante, contratante);

        var encontrado = alvos.Any(a =>
            contextoNorm.Contains(DocumentoTexto.Normalizar(a), StringComparison.Ordinal));

        var violations = new List<Violation>();
        if (!encontrado)
        {
            violations.Add(new Violation(Ref.ToString(), Severity.Error,
                $"A página do índice não identifica a empresa contratante ({string.Join(" / ", alvos)}).",
                new ViolationLocation(entradas[0].ParagraphId, null, secaoDoIndice, "Página do índice")));
        }

        var status = violations.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations,
            Note: $"Índice na seção {secaoDoIndice} com {entradas.Count} entradas."));
    }
}
