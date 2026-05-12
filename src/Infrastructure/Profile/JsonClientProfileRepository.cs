using System.Text.Json;
using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Profile;

namespace WordComplianceValidator.Infrastructure.Profile;

public sealed class JsonClientProfileRepository : IClientProfileRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Parâmetros que precisam ser regex válidos. Falhar early no load se houver erro.
    /// </summary>
    private static readonly string[] RegexParameterKeys =
    {
        "codificacao.pdaRegex",
        "codificacao.clienteRegex"
    };

    /// <summary>
    /// Parâmetros numéricos (inteiros positivos).
    /// </summary>
    private static readonly string[] IntParameterKeys =
    {
        "logomarcas.minimo",
        "folhaRosto.paragrafosIniciais"
    };

    public async Task<ClientProfile> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var dto = JsonSerializer.Deserialize<Dto>(json, Options)
                  ?? throw new InvalidOperationException("Profile JSON inválido.");
        var parameters = dto.Parameters ?? new Dictionary<string, string>();

        ValidateParameters(dto.Cliente, parameters);

        return new ClientProfile(
            Cliente: dto.Cliente,
            Versao: dto.Versao,
            Parameters: parameters);
    }

    private static void ValidateParameters(string cliente, IReadOnlyDictionary<string, string> parameters)
    {
        var errors = new List<string>();

        foreach (var key in RegexParameterKeys)
        {
            if (!parameters.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) continue;
            try { _ = new Regex(value); }
            catch (Exception ex) { errors.Add($"  - '{key}': regex inválido — {ex.Message}"); }
        }

        foreach (var key in IntParameterKeys)
        {
            if (!parameters.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) continue;
            if (!int.TryParse(value, out var n) || n < 0)
                errors.Add($"  - '{key}': esperado inteiro >= 0, recebido '{value}'.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Profile '{cliente}' contém parâmetros inválidos:\n{string.Join("\n", errors)}");
        }
    }

    public async Task<string> SaveAsync(ClientProfile profile, string rootDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(rootDirectory);
        var path = Path.Combine(rootDirectory, $"{Sanitize(profile.Cliente)}.json");
        var dto = new Dto(profile.Cliente, profile.Versao, profile.Parameters.ToDictionary(x => x.Key, x => x.Value));
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(dto, Options), cancellationToken);
        return path;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name) sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString().Trim();
    }

    private sealed record Dto(string Cliente, string Versao, Dictionary<string, string>? Parameters);
}
