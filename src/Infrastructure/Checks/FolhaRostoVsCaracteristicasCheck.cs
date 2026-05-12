using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.2 Cliente — Folha de Rosto: compatibilidade entre informações
/// da folha de rosto e o quadro "Características do Documento" (avaliação semântica via LLM).
/// </summary>
public sealed class FolhaRostoVsCaracteristicasCheck : IRuleCheck
{
    private readonly ISemanticChecker _semantic;

    public ChecklistRef Ref { get; } = new("PS-002", "4.3.2", ChecklistPadrao.Cliente);

    public FolhaRostoVsCaracteristicasCheck(ISemanticChecker semantic)
    {
        _semantic = semantic;
    }

    public async Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var titulosAlias = (ctx.Profile.Get("quadroCaracteristicas.aliases")
            ?? "Características do Documento|Quadro de Características|Características")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var quadro = QuadroCaracteristicas.Find(ctx.Structure, titulosAlias);
        if (quadro is null)
        {
            return new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Quadro 'Características do Documento' não localizado.");
        }
        var quadroDump = string.Join("\n",
            QuadroCaracteristicas.FieldsByLabel(quadro).Select(kv => $"- {kv.Key}: {kv.Value}"));

        // Folha de rosto = primeiros parágrafos antes do primeiro Heading 1.
        var folhaRosto = string.Join("\n",
            ctx.Structure.Paragraphs.Take(40).Select(p => p.Text).Where(s => !string.IsNullOrWhiteSpace(s)));

        var instrucao =
            "Verifique se as informações da folha de rosto são coerentes e compatíveis com o " +
            "quadro 'Características do Documento' (código, título, revisão, cliente, datas). " +
            "Aponte divergências relevantes.";
        var conteudo = $"### Folha de rosto\n{folhaRosto}\n\n### Quadro Características\n{quadroDump}";

        var verdict = await _semantic.EvaluateAsync(instrucao, conteudo, cancellationToken);

        if (verdict.Conforme)
            return new RuleCheckResult(Ref, CheckStatus.Passed, Array.Empty<Violation>(), Note: verdict.Justificativa);

        return new RuleCheckResult(Ref, CheckStatus.Failed,
            new[] {
                new Violation(
                    RuleId: Ref.ToString(),
                    Severity: Severity.Warning,
                    Message: $"Incompatibilidade folha de rosto × quadro 'Características': {verdict.Justificativa}",
                    Location: new ViolationLocation(null, null, null, "Folha de Rosto / Quadro Características"))
            },
            Note: verdict.Justificativa);
    }
}
