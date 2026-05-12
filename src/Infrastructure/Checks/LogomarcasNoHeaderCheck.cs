using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>PS-002 4.1 Cliente — Logomarcas PdA + Cliente no cabeçalho.</summary>
public sealed class LogomarcasNoHeaderCheck : IRuleCheck
{
    public ChecklistRef Ref { get; } = new("PS-002", "4.1", ChecklistPadrao.Cliente);

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var minimo = ctx.Profile.GetInt("logomarcas.minimo") ?? 2;
        var totalImagens = ctx.Structure.Headers.Sum(h => h.ImageCount);
        var violations = new List<Violation>();

        if (ctx.Structure.Headers.Count == 0)
        {
            violations.Add(new Violation(
                RuleId: Ref.ToString(),
                Severity: Severity.Error,
                Message: "Documento não possui cabeçalho.",
                Location: new ViolationLocation(null, "header", null)));
        }
        else if (totalImagens < minimo)
        {
            violations.Add(new Violation(
                RuleId: Ref.ToString(),
                Severity: Severity.Error,
                Message: $"Esperado(s) pelo menos {minimo} logomarca(s) no cabeçalho (PdA + Cliente); encontrada(s) {totalImagens}.",
                Location: new ViolationLocation(null, "header", null)));
        }

        var status = violations.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }
}
