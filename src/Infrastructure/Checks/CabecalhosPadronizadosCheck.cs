using System.Text;
using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.3 (letra g) Cliente — Padronização de cabeçalhos. Heurística:
/// todos os cabeçalhos extraídos do documento devem ter conteúdo equivalente
/// (texto e contagem de imagens consistentes) — exceto cabeçalhos de primeira página
/// que podem ser vazios.
/// </summary>
public sealed class CabecalhosPadronizadosCheck : IRuleCheck
{
    // Tokens de posicionamento/anchor de imagem flutuante que aparecem no InnerText do header.
    private static readonly Regex NoiseToken = new(
        @"\b(?:left|right|top|bottom|center|margin|page|column)\d+\b|\b\d{4,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ISemanticChecker? _semantic;

    public CabecalhosPadronizadosCheck(ISemanticChecker? semantic = null)
    {
        _semantic = semantic;
    }

    public ChecklistRef Ref { get; } = new("PS-002", "4.3.3 (letra g)", ChecklistPadrao.Cliente);

    public async Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        // Cabeçalhos de primeira página (capa) seguem padrão próprio; ignora.
        // Stripa tokens de posicionamento residual e ignora cabeçalhos vazios sem imagem.
        var headers = ctx.Structure.Headers
            .Where(h => !string.Equals(h.Kind, "first", StringComparison.OrdinalIgnoreCase))
            .Select(h => h with { Text = StripNoise(h.Text) })
            .Where(h => !string.IsNullOrWhiteSpace(h.Text) || h.ImageCount > 0)
            .ToList();

        if (headers.Count <= 1)
        {
            return new RuleCheckResult(Ref, CheckStatus.Passed,
                Array.Empty<Violation>(),
                Note: headers.Count == 0 ? "Documento sem cabeçalhos com conteúdo." : null);
        }

        // Cabeçalhos só com imagem (sem texto) podem ser cabeçalhos de "primeira página"
        // ou de seções com layout reduzido — não devem causar divergência. Só comparar texto
        // entre cabeçalhos que de fato têm texto.
        var headersComTexto = headers.Where(h => !string.IsNullOrWhiteSpace(h.Text)).ToList();
        var distinctTexts = headersComTexto.Select(h => Normalize(h.Text)).Distinct().Count();

        // Imagens: aceitar variação se a MAIORIA dos cabeçalhos com conteúdo tiver a mesma contagem.
        var imageCounts = headers.Select(h => h.ImageCount).ToList();
        var imageMode = imageCounts.GroupBy(c => c).OrderByDescending(g => g.Count()).First();
        var imagensDiscrepantes = imageCounts.Count(c => c != imageMode.Key);

        var violations = new List<Violation>();
        if (distinctTexts > 1)
        {
            var amostras = string.Join(" | ",
                headersComTexto.Select(h => Truncate(h.Text, 40)).Distinct().Take(3));
            violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                $"Cabeçalhos divergem entre seções: {amostras}",
                new ViolationLocation(null, "header", null, "Cabeçalhos")));
        }
        // Só reclama de imagens se MAIS DA METADE dos cabeçalhos divergir do padrão.
        if (imagensDiscrepantes * 2 > imageCounts.Count)
        {
            violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                $"Quantidade de imagens nos cabeçalhos não é uniforme ({string.Join(",", imageCounts)}).",
                new ViolationLocation(null, "header", null, "Cabeçalhos")));
        }

        // Antes de marcar como Failed, dar ao LLM a chance de confirmar que as divergências
        // são reais. Isso evita falsos positivos quando os cabeçalhos têm pequenas variações
        // estilísticas mas representam o mesmo conteúdo conceitual.
        if (violations.Count > 0 && _semantic is not null)
        {
            try
            {
                var dump = new StringBuilder();
                for (int i = 0; i < headersComTexto.Count; i++)
                    dump.AppendLine($"#{i + 1} [{headersComTexto[i].Kind}]: {headersComTexto[i].Text}");

                var verdict = await _semantic.EvaluateAsync(
                    "Avalie se os cabeçalhos abaixo (extraídos de seções distintas do mesmo documento) " +
                    "representam o MESMO cabeçalho conceitual padronizado. 'conforme'=true quando as " +
                    "diferenças são apenas variações de formatação (espaços, quebras de linha) e o " +
                    "conteúdo é equivalente; 'conforme'=false se há divergência real de conteúdo.",
                    dump.ToString(),
                    cancellationToken);

                if (verdict.Conforme)
                {
                    return new RuleCheckResult(Ref, CheckStatus.Passed,
                        Array.Empty<Violation>(),
                        Note: $"LLM confirma equivalência dos cabeçalhos: {verdict.Justificativa}");
                }
            }
            catch
            {
                // Falha do LLM não derruba a regra; mantemos o veredito heurístico.
            }
        }

        var status = violations.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed;
        return new RuleCheckResult(Ref, status, violations);
    }

    private static string Normalize(string text) =>
        new string((text ?? string.Empty)
            .Replace("\u00A0", " ")
            .Trim()
            .ToLowerInvariant()
            .Where(c => !char.IsWhiteSpace(c) || c == ' ')
            .ToArray())
        .Replace("  ", " ");

    private static string Truncate(string s, int n) =>
        s.Length <= n ? s : s[..n] + "…";

    private static string StripNoise(string text) =>
        NoiseToken.Replace(text ?? string.Empty, " ").Trim();
}
