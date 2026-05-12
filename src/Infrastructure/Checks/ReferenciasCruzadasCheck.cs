using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.3 (letra h) Cliente — Correção de referências cruzadas. Detecta
/// marcas de erro do Word em referências cruzadas: "Erro! Indicador não definido",
/// "Erro! Fonte de referência", "Error! Reference source", etc.
/// </summary>
public sealed class ReferenciasCruzadasCheck : IRuleCheck
{
    private const string DefaultErrorMarks =
        "Erro! Indicador não definido|Erro! Fonte de referência|" +
        "Error! Reference source not found|Error! Bookmark not defined";

    public ChecklistRef Ref { get; } = new("PS-002", "4.3.3 (letra h)", ChecklistPadrao.Cliente);

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var marksRaw = ctx.Profile.Get("referenciasCruzadas.marcasErro") ?? DefaultErrorMarks;
        var marks = marksRaw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var violations = new List<Violation>();
        foreach (var p in ctx.Structure.Paragraphs)
        {
            foreach (var mark in marks)
            {
                if (p.Text.Contains(mark, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(new Violation(Ref.ToString(), Severity.Error,
                        $"Referência cruzada com erro: '{mark}' em parágrafo {p.ParagraphId}.",
                        new ViolationLocation(p.ParagraphId, null, null, "Referência cruzada")));
                    break;
                }
            }
        }

        var status = violations.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }
}
