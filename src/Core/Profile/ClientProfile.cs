namespace WordComplianceValidator.Core.Profile;

public sealed record ClientProfile(
    string Cliente,
    string Versao,
    IReadOnlyDictionary<string, string> Parameters)
{
    public string? Get(string key) => Parameters.TryGetValue(key, out var v) ? v : null;

    public string Require(string key) =>
        Get(key) ?? throw new InvalidOperationException(
            $"Profile do cliente '{Cliente}' não contém o parâmetro obrigatório '{key}'.");

    public int? GetInt(string key) =>
        Get(key) is { } s && int.TryParse(s, out var i) ? i : null;
}
