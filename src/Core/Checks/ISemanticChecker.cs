namespace WordComplianceValidator.Core.Checks;

public sealed record SemanticVerdict(bool Conforme, string Justificativa);

public interface ISemanticChecker
{
    Task<SemanticVerdict> EvaluateAsync(
        string instrucao,
        string conteudo,
        CancellationToken cancellationToken = default);
}
