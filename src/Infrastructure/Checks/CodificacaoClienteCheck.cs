using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-018 4.7 Cliente — Codificação do Cliente: valida se a codificação do
/// cliente (regex em <c>codificacao.clienteRegex</c>) está presente em algum dos
/// campos definidos do quadro Características.
/// </summary>
public sealed class CodificacaoClienteCheck : IRuleCheck
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public ChecklistRef Ref { get; } = new("PS-018", "4.7", ChecklistPadrao.Cliente);

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var clienteRx = ctx.Profile.Get("codificacao.clienteRegex");
        if (string.IsNullOrWhiteSpace(clienteRx))
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Profile sem 'codificacao.clienteRegex'."));
        }

        var titulosAlias = (ctx.Profile.Get("quadroCaracteristicas.aliases")
            ?? "Características do Documento|Quadro de Características|Características")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var table = QuadroCaracteristicas.Find(ctx.Structure, titulosAlias);
        if (table is null)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Failed,
                new[]
                {
                    new Violation(Ref.ToString(), Severity.Warning,
                        "Quadro 'Características do Documento' não localizado para validar codificação do cliente.",
                        new ViolationLocation(null, null, null, "Quadro Características"))
                }));
        }

        var pattern = SearchPattern(clienteRx);
        var hit = table.Cells.Any(c =>
            !string.IsNullOrWhiteSpace(c.Text) && Regex.IsMatch(c.Text, pattern, Opt));

        var violations = new List<Violation>();
        if (!hit)
        {
            violations.Add(new Violation(Ref.ToString(), Severity.Error,
                $"Codificação do Cliente ({clienteRx}) não foi localizada no quadro Características.",
                new ViolationLocation(null, null, null, "Quadro Características")));
        }

        var status = violations.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }

    private static string SearchPattern(string regex)
    {
        var p = regex.Trim();
        if (p.StartsWith('^')) p = p[1..];
        if (p.EndsWith('$')) p = p[..^1];
        return p;
    }
}
