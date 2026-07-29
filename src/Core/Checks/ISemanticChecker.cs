using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Core.Checks;

public sealed record SemanticVerdict(bool Conforme, string Justificativa);

/// <summary>
/// Resultado de uma avaliação semântica. <see cref="NaoAplicavel"/> é um estado de primeira
/// classe porque dezenas de itens do CL-001 são condicionais ("quando aplicável", "no caso
/// de DCE…") — tratá-los como reprovação produziria falso positivo em massa.
/// </summary>
public enum SemanticStatus
{
    Conforme,
    NaoAplicavel,
    NaoConforme
}

/// <summary>
/// Um achado individual da avaliação. <paramref name="TrechoAncora"/> é um trecho literal do
/// documento que permite ancorar o comentário no parágrafo correto do .docx.
/// </summary>
public sealed record SemanticFinding(
    Severity Severidade,
    string Mensagem,
    string? TrechoAncora);

public sealed record SemanticEvaluation(
    SemanticStatus Status,
    IReadOnlyList<SemanticFinding> Achados,
    string? Justificativa = null);

public interface ISemanticChecker
{
    /// <summary>Avaliação booleana simples, usada pelos checks dedicados mais antigos.</summary>
    Task<SemanticVerdict> EvaluateAsync(
        string instrucao,
        string conteudo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Avaliação estruturada usada pelo motor genérico: distingue não-aplicável de
    /// não-conforme e devolve achados individuais com severidade e âncora de localização.
    /// </summary>
    Task<SemanticEvaluation> AvaliarAsync(
        string instrucao,
        string conteudo,
        string? modelo = null,
        CancellationToken cancellationToken = default);
}
