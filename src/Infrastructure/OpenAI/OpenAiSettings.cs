namespace WordComplianceValidator.Infrastructure.OpenAI;

public sealed class OpenAiSettings
{
    public const string ModeloPadrao = "gpt-6.1-sol";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = ModeloPadrao;

    /// <summary>
    /// Esforço de raciocínio enviado como <c>reasoning_effort</c> (<c>low</c>, <c>medium</c>,
    /// <c>high</c>). Vazio deixa o default do modelo.
    /// </summary>
    public string? ReasoningEffort { get; set; } = "medium";

    /// <summary>Tarifas por modelo, para estimar o custo de cada revisão.</summary>
    public Dictionary<string, PrecoDeModelo> Precos { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
