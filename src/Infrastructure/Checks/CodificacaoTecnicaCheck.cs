using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-018 4.3 Cliente — Codificação Técnica: estrutura no padrão PdA e Cliente.
/// Espera profile parameters:
///   codificacao.pdaRegex     (regex obrigatório para padrão PdA, default 15 chars)
///   codificacao.clienteRegex (opcional)
///   codificacao.aliasesRotulo (rótulos do quadro Características que contêm a codificação)
/// </summary>
public sealed class CodificacaoTecnicaCheck : IRuleCheck
{
    private const RegexOptions MatchOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public ChecklistRef Ref { get; } = new("PS-018", "4.3", ChecklistPadrao.Cliente);

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var pdaRx = ctx.Profile.Get("codificacao.pdaRegex");
        var clienteRx = ctx.Profile.Get("codificacao.clienteRegex");
        if (string.IsNullOrWhiteSpace(pdaRx))
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Profile sem 'codificacao.pdaRegex'."));
        }

        var rotulos = (ctx.Profile.Get("codificacao.aliasesRotulo")
            ?? "Codificação PdA|Codificação|Documento|Código do Documento|Código")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var titulosAlias = (ctx.Profile.Get("quadroCaracteristicas.aliases")
            ?? "Características do Documento|Quadro de Características|Características")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var table = QuadroCaracteristicas.Find(ctx.Structure, titulosAlias);
        var tableValues = table?.Cells
            .Select(c => c.Text.Trim())
            .Where(s => s.Length > 0)
            .ToArray() ?? Array.Empty<string>();

        var violations = new List<Violation>();

        // 1) File name should either follow a configured code pattern or match
        // a document code present in the characteristics table without separators.
        var fileName = ctx.Structure.FileName;
        if (!string.IsNullOrEmpty(fileName))
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            if (!MatchesDocumentPattern(stem, pdaRx)
                && !MatchesDocumentPattern(stem, clienteRx)
                && !MatchesAnyDocumentCode(stem, tableValues))
            {
                violations.Add(new Violation(
                    RuleId: Ref.ToString(),
                    Severity: Severity.Warning,
                    Message: $"Nome do arquivo '{stem}' não corresponde ao padrão PdA ({pdaRx}) nem a um código do quadro Características.",
                    Location: new ViolationLocation(null, null, null, "Arquivo")));
            }
        }

        // 2) Quadro "Características" should contain a value matching the PdA regex.
        if (table is null)
        {
            violations.Add(new Violation(
                RuleId: Ref.ToString(),
                Severity: Severity.Warning,
                Message: "Quadro 'Características do Documento' não localizado para validar codificação.",
                Location: new ViolationLocation(null, null, null, "Quadro Características")));
        }
        else
        {
            var codigo = FindCode(table, rotulos, pdaRx, clienteRx);

            if (string.IsNullOrWhiteSpace(codigo))
            {
                violations.Add(new Violation(
                    RuleId: Ref.ToString(),
                    Severity: Severity.Warning,
                    Message: "Não foi possível localizar campo de codificação no quadro 'Características'.",
                    Location: new ViolationLocation(null, null, null, "Quadro Características")));
            }
            else if (!MatchesDocumentPattern(codigo, pdaRx))
            {
                violations.Add(new Violation(
                    RuleId: Ref.ToString(),
                    Severity: Severity.Error,
                    Message: $"Codificação no quadro Características ('{codigo}') não corresponde ao padrão PdA ({pdaRx}).",
                    Location: new ViolationLocation(null, null, null, "Quadro Características")));
            }
            else if (!string.IsNullOrWhiteSpace(clienteRx) && !MatchesDocumentPattern(codigo, clienteRx))
            {
                violations.Add(new Violation(
                    RuleId: Ref.ToString(),
                    Severity: Severity.Warning,
                    Message: $"Codificação '{codigo}' não corresponde ao padrão Cliente ({clienteRx}).",
                    Location: new ViolationLocation(null, null, null, "Quadro Características")));
            }
        }

        var status = violations.Count == 0 ? CheckStatus.Passed
                   : violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : CheckStatus.Skipped;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }

    private static string? FindCode(
        ExtractedTable table,
        IReadOnlyList<string> rotulos,
        string pdaRx,
        string? clienteRx)
    {
        var fields = QuadroCaracteristicas.FieldsByLabel(table);
        var labeledValues = rotulos
            .Select(rot => fields.FirstOrDefault(kv => kv.Key.Contains(rot, StringComparison.OrdinalIgnoreCase)).Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        foreach (var value in labeledValues)
        {
            var code = FindCodeInText(value, pdaRx, null);
            if (!string.IsNullOrWhiteSpace(code))
            {
                return code;
            }
        }

        foreach (var cell in table.Cells)
        {
            var code = FindCodeInText(cell.Text, pdaRx, null);
            if (!string.IsNullOrWhiteSpace(code))
            {
                return code;
            }
        }

        if (string.IsNullOrWhiteSpace(clienteRx))
        {
            return null;
        }

        foreach (var value in labeledValues)
        {
            var code = FindCodeInText(value, clienteRx, null);
            if (!string.IsNullOrWhiteSpace(code))
            {
                return code;
            }
        }

        foreach (var cell in table.Cells)
        {
            var code = FindCodeInText(cell.Text, clienteRx, null);
            if (!string.IsNullOrWhiteSpace(code))
            {
                return code;
            }
        }

        return null;
    }

    private static string? FindCodeInText(string? text, string pdaRx, string? clienteRx)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var pdaMatch = Regex.Match(text, SearchPattern(pdaRx), MatchOptions);
        if (pdaMatch.Success)
        {
            return pdaMatch.Value;
        }

        if (!string.IsNullOrWhiteSpace(clienteRx))
        {
            var clienteMatch = Regex.Match(text, SearchPattern(clienteRx), MatchOptions);
            if (clienteMatch.Success)
            {
                return clienteMatch.Value;
            }
        }

        return null;
    }

    private static string SearchPattern(string regex)
    {
        var pattern = regex.Trim();
        if (pattern.StartsWith('^'))
        {
            pattern = pattern[1..];
        }

        if (pattern.EndsWith('$'))
        {
            pattern = pattern[..^1];
        }

        return pattern + @"(?:-\d+)?";
    }

    private static bool MatchesDocumentPattern(string? value, string? regex)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(regex))
        {
            return false;
        }

        var trimmed = value.Trim();
        return Regex.IsMatch(trimmed, regex, MatchOptions)
            || Regex.IsMatch(WithoutRevisionSuffix(trimmed), regex, MatchOptions);
    }

    private static string WithoutRevisionSuffix(string value) =>
        Regex.Replace(value.Trim(), @"-\d+$", string.Empty, MatchOptions);

    private static bool MatchesAnyDocumentCode(string stem, IEnumerable<string> documentValues)
    {
        var normalizedStem = NormalizeCode(stem);
        return normalizedStem.Length > 0
            && documentValues.Any(value =>
                NormalizeCode(value) == normalizedStem
                || NormalizeCode(WithoutRevisionSuffix(value)) == normalizedStem);
    }

    private static string NormalizeCode(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
