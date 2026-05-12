using System.Text.Json;
using System.Text.Json.Serialization;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Core.Serialization;

public static class RuleSetSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(RuleSet ruleSet) =>
        JsonSerializer.Serialize(ruleSet, Options);

    public static RuleSet Deserialize(string json) =>
        JsonSerializer.Deserialize<RuleSet>(json, Options)
        ?? throw new InvalidOperationException("Não foi possível desserializar RuleSet (JSON vazio).");
}
