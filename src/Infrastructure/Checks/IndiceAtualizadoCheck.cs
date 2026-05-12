using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.5.1 Cliente — Índice atualizado. Heurística: detectar a presença de
/// um sumário/TOC no documento e verificar se não há marcas de erro de atualização.
/// (A atualização efetiva exige rebuild do TOC; aqui se faz apenas verificação estrutural.)
/// </summary>
public sealed class IndiceAtualizadoCheck : IRuleCheck
{
    public IndiceAtualizadoCheck(ChecklistPadrao padrao = ChecklistPadrao.Cliente)
    {
        Ref = new ChecklistRef("PS-002", "4.3.5.1", padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var violations = new List<Violation>();
        // Detecta TOC tanto via heurística antiga quanto via campo OOXML "TOC" no body.
        var temToc = ctx.Structure.HasUpdatedToc
                  || ctx.Structure.BodyFieldCodes.Any(c =>
                        c.Equals("TOC", StringComparison.OrdinalIgnoreCase));
        if (!temToc)
        {
            violations.Add(new Violation(
                RuleId: Ref.ToString(),
                Severity: Severity.Warning,
                Message: "Sumário/Índice não localizado no documento.",
                Location: new ViolationLocation(null, null, null, "Índice")));
        }

        // Detecta texto de erro típico do Word quando o TOC não foi atualizado.
        var marcasRaw = ctx.Profile.Get("indice.marcasErro")
            ?? "Erro! Indicador não definido|Erro! Nenhuma entrada|Error! No table of contents entries found";
        var marcas = marcasRaw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var erroAtualizacao = ctx.Structure.Paragraphs.Any(p =>
            marcas.Any(m => p.Text.Contains(m, StringComparison.OrdinalIgnoreCase)));
        if (erroAtualizacao)
        {
            violations.Add(new Violation(
                RuleId: Ref.ToString(),
                Severity: Severity.Error,
                Message: "Sumário/Índice contém marcas de erro do Word — atualização pendente.",
                Location: new ViolationLocation(null, null, null, "Índice")));
        }

        var status = violations.Count == 0 ? CheckStatus.Passed
                   : violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : CheckStatus.Skipped;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }
}
