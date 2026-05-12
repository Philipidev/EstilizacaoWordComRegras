using System.Text.Json;
using WordComplianceValidator.Core.Profile;

namespace WordComplianceValidator.Infrastructure.Profile;

public sealed class JsonClientProfileRepository : IClientProfileRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<ClientProfile> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var dto = JsonSerializer.Deserialize<Dto>(json, Options)
                  ?? throw new InvalidOperationException("Profile JSON inválido.");
        return new ClientProfile(
            Cliente: dto.Cliente,
            Versao: dto.Versao,
            Parameters: dto.Parameters ?? new Dictionary<string, string>());
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
