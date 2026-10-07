using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-018 4.8 Cliente — Evolução do Documento. Verifica se o campo de revisão no
/// quadro Características está preenchido e segue o padrão esperado:
/// "0A".."0Z" (etapa de comentários), "00" (emissão final) ou "01".."99" (revisões).
/// </summary>
public sealed class EvolucaoDocumentoCheck : IRuleCheck
{
    private static readonly Regex Revisao =
        new(@"^(0[A-Z]|[0-9]{2})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ChecklistRef Ref { get; } = new("PS-018", "4.8", ChecklistPadrao.Cliente);

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var titulosAlias = (ctx.Profile.Get("quadroCaracteristicas.aliases")
            ?? "Características do Documento|Quadro de Características|Características")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var rotulos = (ctx.Profile.Get("revisao.aliasesRotulo") ?? "Revisão|Rev.|Rev")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var table = QuadroCaracteristicas.Find(ctx.Structure, titulosAlias);
        if (table is null)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Quadro 'Características do Documento' não localizado."));
        }

        var historico = QuadroCaracteristicas.HistoricoDeRevisoes(table);
        if (historico.Count > 0)
            return Task.FromResult(AvaliarHistorico(historico));

        var byLabel = QuadroCaracteristicas.FieldsByLabel(table);
        var revisao = LookupAny(byLabel, rotulos);

        if (string.IsNullOrWhiteSpace(revisao))
        {
            // Fallback: layout coluna (cabeçalho "Revisão").
            var col = QuadroCaracteristicas.FieldsByColumn(table,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["revisao"] = rotulos
                });
            revisao = col?.GetValueOrDefault("revisao");
        }

        var violations = new List<Violation>();
        if (string.IsNullOrWhiteSpace(revisao))
        {
            violations.Add(new Violation(Ref.ToString(), Severity.Error,
                "Campo 'Revisão' não encontrado no quadro Características.",
                new ViolationLocation(null, null, null, "Quadro Características")));
        }
        else
        {
            var token = ExtractRevisionToken(revisao);
            if (token is null || !Revisao.IsMatch(token))
            {
                violations.Add(new Violation(Ref.ToString(), Severity.Error,
                    $"Valor de revisão '{revisao}' não segue padrão (0A–0Z, 00, 01–99).",
                    new ViolationLocation(null, null, null, "Quadro Características")));
            }
        }

        var status = violations.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }

    /// <summary>
    /// Evolução pelo histórico do quadro: cada revisão no padrão, em ordem estritamente
    /// crescente (0A → 0B → … → 00 → 01 …) e com datas que não andam para trás.
    /// <para>
    /// Antes a revisão era lida por rótulo — e o primeiro rótulo "Rev." do quadro real é o
    /// cabeçalho da grade de folhas. Num quadro conforme cujo histórico vinha logo abaixo do
    /// cabeçalho, o valor lido era "Data Emissor Verificador…" e o check reprovava.
    /// </para>
    /// </summary>
    private RuleCheckResult AvaliarHistorico(IReadOnlyList<QuadroCaracteristicas.EntradaDeRevisao> historico)
    {
        var violations = new List<Violation>();
        QuadroCaracteristicas.EntradaDeRevisao? anterior = null;

        foreach (var entrada in historico)
        {
            var ordem = QuadroCaracteristicas.OrdemDaRevisaoPda(entrada.Revisao);
            if (ordem is null)
            {
                violations.Add(Erro($"Revisão '{entrada.Revisao}' do histórico não segue o padrão (0A–0Z, 00, 01–99)."));
            }
            else if (anterior is not null
                     && QuadroCaracteristicas.OrdemDaRevisaoPda(anterior.Revisao) is { } ordemAnterior
                     && ordem <= ordemAnterior)
            {
                violations.Add(Erro($"Revisão '{entrada.Revisao}' vem depois de '{anterior.Revisao}' no histórico: " +
                                    "a sequência deve ser crescente (0A → 0B → … → 00 → 01 …)."));
            }

            if (anterior?.DataLida is { } dataAnterior && entrada.DataLida is { } data && data < dataAnterior)
            {
                violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                    $"A revisão '{entrada.Revisao}' ({entrada.Data}) tem data anterior à da revisão " +
                    $"'{anterior.Revisao}' ({anterior.Data}).",
                    new ViolationLocation(null, null, null, "Quadro Características")));
            }

            if (string.IsNullOrWhiteSpace(entrada.Data))
            {
                violations.Add(Erro($"A revisão '{entrada.Revisao}' está sem data no histórico do quadro."));
            }

            anterior = entrada;
        }

        var status = violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : violations.Count > 0 ? CheckStatus.Skipped
                   : CheckStatus.Passed;
        return new RuleCheckResult(Ref, status, violations,
            Note: $"Histórico: {string.Join(" → ", historico.Select(e => e.Revisao))}.");

        Violation Erro(string m) => new(Ref.ToString(), Severity.Error, m,
            new ViolationLocation(null, null, null, "Quadro Características"));
    }

    private static string? LookupAny(IReadOnlyDictionary<string, string> fields, IEnumerable<string> aliases)
    {
        foreach (var label in aliases)
        {
            var hit = fields.FirstOrDefault(kv => kv.Key.Contains(label, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(hit.Value)) return hit.Value;
        }
        return null;
    }

    private static string? ExtractRevisionToken(string raw)
    {
        var trimmed = raw.Trim();
        var m = Regex.Match(trimmed, @"\b(0[A-Za-z]|\d{2})\b");
        return m.Success ? m.Value.ToUpperInvariant() : null;
    }
}
