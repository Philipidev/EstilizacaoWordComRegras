using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.6.1 (PdA) / 4.3.6.2 (Cliente) — Formatação do corpo do documento:
/// fonte, tamanho, alinhamento e margens.
/// <para>
/// A avaliação é proporcional, não absoluta: documentos técnicos legitimamente têm
/// parágrafos fora do padrão (legendas, notas de rodapé, textos em figuras). Só é
/// reportada violação quando a divergência ultrapassa a tolerância do profile.
/// Parágrafos cuja fonte/tamanho não pôde ser resolvido são ignorados em vez de
/// contados como divergentes.
/// </para>
/// </summary>
public sealed class FormatacaoCorpoCheck : IRuleCheck
{
    private const string FonteDefault = "Times New Roman";
    private const double TamanhoDefault = 12;
    private const int ToleranciaDefault = 10;

    public FormatacaoCorpoCheck(ChecklistPadrao padrao = ChecklistPadrao.Cliente, string item = "4.3.6.2")
    {
        Ref = new ChecklistRef("PS-002", item, padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var corpo = DocumentoTexto.Corpo(ctx.Structure);
        if (corpo.Count == 0)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Documento sem parágrafos de corpo identificáveis."));
        }

        var fonteEsperada = ctx.Profile.Get("corpo.fonte") ?? FonteDefault;
        var tamanhoEsperado = ParseDouble(ctx.Profile.Get("corpo.tamanho")) ?? TamanhoDefault;
        var tolerancia = ctx.Profile.GetInt("corpo.toleranciaPercentual") ?? ToleranciaDefault;

        var violations = new List<Violation>();

        // --- Fonte ---
        var comFonte = corpo.Where(p => !string.IsNullOrWhiteSpace(p.EffectiveFont)).ToList();
        var fonteDivergente = comFonte
            .Where(p => !string.Equals(p.EffectiveFont, fonteEsperada, StringComparison.OrdinalIgnoreCase))
            .ToList();
        AvaliarProporcao(violations, fonteDivergente.Count, comFonte.Count, tolerancia,
            descricao: $"fonte diferente de '{fonteEsperada}'",
            exemplos: fonteDivergente.Select(p => $"'{Resumo(p.Text)}' → {p.EffectiveFont}"),
            ruleId: Ref.ToString());

        // --- Tamanho ---
        var comTamanho = corpo.Where(p => p.EffectiveFontSize is not null).ToList();
        var tamanhoDivergente = comTamanho
            .Where(p => Math.Abs(p.EffectiveFontSize!.Value - tamanhoEsperado) > 0.01)
            .ToList();
        AvaliarProporcao(violations, tamanhoDivergente.Count, comTamanho.Count, tolerancia,
            descricao: $"tamanho diferente de {tamanhoEsperado:0.#} pt",
            exemplos: tamanhoDivergente.Select(p => $"'{Resumo(p.Text)}' → {p.EffectiveFontSize:0.#} pt"),
            ruleId: Ref.ToString());

        // --- Alinhamento (só quando o profile declara o esperado) ---
        var alinhamentoEsperado = ctx.Profile.Get("corpo.alinhamento");
        if (!string.IsNullOrWhiteSpace(alinhamentoEsperado))
        {
            var comAlinhamento = corpo.Where(p => !string.IsNullOrWhiteSpace(p.Alignment)).ToList();
            var alinhamentoDivergente = comAlinhamento
                .Where(p => !string.Equals(p.Alignment, alinhamentoEsperado, StringComparison.OrdinalIgnoreCase))
                .ToList();
            AvaliarProporcao(violations, alinhamentoDivergente.Count, comAlinhamento.Count, tolerancia,
                descricao: $"alinhamento diferente de '{alinhamentoEsperado}'",
                exemplos: alinhamentoDivergente.Select(p => $"'{Resumo(p.Text)}' → {p.Alignment}"),
                ruleId: Ref.ToString());
        }

        // --- Margens (só quando o profile declara o esperado, em twips) ---
        AvaliarMargens(violations, ctx, Ref.ToString());

        var status = violations.Count == 0 ? CheckStatus.Passed
                   : violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : CheckStatus.Skipped;

        return Task.FromResult(new RuleCheckResult(Ref, status, violations,
            Note: $"{corpo.Count} parágrafos de corpo avaliados (esperado: {fonteEsperada} {tamanhoEsperado:0.#} pt)."));
    }

    private static void AvaliarProporcao(
        List<Violation> violations, int divergentes, int total, int toleranciaPercentual,
        string descricao, IEnumerable<string> exemplos, string ruleId)
    {
        if (total == 0 || divergentes == 0) return;

        var percentual = divergentes * 100.0 / total;
        if (percentual <= toleranciaPercentual) return;

        var amostra = string.Join("; ", exemplos.Take(3));
        violations.Add(new Violation(ruleId, Severity.Error,
            $"{divergentes} de {total} parágrafos de corpo ({percentual:0.#}%) com {descricao}. " +
            $"Exemplos: {amostra}",
            new ViolationLocation(null, null, null, "Corpo do documento")));
    }

    private static void AvaliarMargens(List<Violation> violations, DocumentContext ctx, string ruleId)
    {
        var margens = new (string Chave, string Nome, Func<ExtractedSection, double?> Seletor)[]
        {
            ("corpo.margemSuperior", "superior", s => s.MarginTop),
            ("corpo.margemInferior", "inferior", s => s.MarginBottom),
            ("corpo.margemEsquerda", "esquerda", s => s.MarginLeft),
            ("corpo.margemDireita",  "direita",  s => s.MarginRight)
        };

        foreach (var (chave, nome, seletor) in margens)
        {
            var esperada = ParseDouble(ctx.Profile.Get(chave));
            if (esperada is null) continue;

            // Tolerância de 1 pt (20 twips) para arredondamentos do Word.
            var fora = ctx.Structure.Sections
                .Where(s => seletor(s) is { } v && Math.Abs(v - esperada.Value) > 20)
                .ToList();

            if (fora.Count > 0)
            {
                violations.Add(new Violation(ruleId, Severity.Error,
                    $"Margem {nome} fora do padrão em {fora.Count} seção(ões): " +
                    $"esperado {esperada.Value:0} twips, encontrado " +
                    string.Join(", ", fora.Select(s => $"seção {s.Index}={seletor(s):0}")),
                    new ViolationLocation(null, null, fora[0].Index, $"Margem {nome}")));
            }
        }
    }

    private static double? ParseDouble(string? raw) =>
        double.TryParse(raw, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string Resumo(string texto)
    {
        var t = texto.Trim().Replace("\n", " ");
        return t.Length <= 40 ? t : t[..40] + "…";
    }
}
