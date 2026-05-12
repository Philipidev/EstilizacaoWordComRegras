using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Rules;
using WordComplianceValidator.Core.Serialization;

namespace WordComplianceValidator.Infrastructure.Persistence;

public sealed class FileRuleSetRepository : IRuleSetRepository
{
    private readonly string _root;
    private static readonly Regex VersionRx = new(@"^v(\d+)\.(\d+)\.json$", RegexOptions.Compiled);

    public FileRuleSetRepository(string root)
    {
        _root = root;
    }

    public async Task<string> SaveAsync(RuleSet ruleSet, CancellationToken cancellationToken = default)
    {
        var clientDir = Path.Combine(_root, SanitizeName(ruleSet.Cliente));
        Directory.CreateDirectory(clientDir);
        var fileName = $"v{ruleSet.VersaoPadrao}.json";
        var path = Path.Combine(clientDir, fileName);
        var json = RuleSetSerializer.Serialize(ruleSet);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return path;
    }

    public async Task<RuleSet> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return RuleSetSerializer.Deserialize(json);
    }

    public Task<string> NextVersionAsync(string clientName, bool major, CancellationToken cancellationToken = default)
    {
        var clientDir = Path.Combine(_root, SanitizeName(clientName));
        if (!Directory.Exists(clientDir)) return Task.FromResult("1.0");

        var versions = Directory.EnumerateFiles(clientDir, "v*.json")
            .Select(p => VersionRx.Match(Path.GetFileName(p)))
            .Where(m => m.Success)
            .Select(m => (Major: int.Parse(m.Groups[1].Value), Minor: int.Parse(m.Groups[2].Value)))
            .OrderByDescending(v => v.Major).ThenByDescending(v => v.Minor)
            .ToList();

        if (versions.Count == 0) return Task.FromResult("1.0");
        var (curMajor, curMinor) = versions[0];
        var next = major ? ($"{curMajor + 1}.0") : ($"{curMajor}.{curMinor + 1}");
        return Task.FromResult(next);
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name) sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString().Trim();
    }
}
