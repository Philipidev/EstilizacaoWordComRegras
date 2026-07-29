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
    // Padrões cobertos:
    //  - "left218440" / "right1234" / nome alinhamento + offset numérico
    //  - "centercenter" / "leftleft" / 2+ tokens de alinhamento consecutivos
    //    (sem exigir \b após — quando o token aparece grudado ao texto seguinte)
    //  - números avulsos com 4+ dígitos (EMU/offsets de posicionamento)
    private const string AlignWord = "left|right|top|bottom|center|inline|margin|page|column|para|line|character";
    private static readonly Regex NoiseToken = new(
        $@"\b(?:{AlignWord})(?:{AlignWord})+(?=[A-ZÀ-Ý\s]|$)|" +   // 2+ tokens consecutivos (centercenter, leftright)
        $@"\b(?:{AlignWord})\d+\b|" +                              // alinhamento + número (right218440)
        @"\b\d{4,}\b",                                              // número avulso longo (EMU/offset)
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ISemanticChecker? _semantic;

    public CabecalhosPadronizadosCheck(ISemanticChecker? semantic = null)
    {
        _semantic = semantic;
    }

    public ChecklistRef Ref { get; } = new("PS-002", "4.3.3 (letra g)", ChecklistPadrao.Cliente);

    public async Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        // Tarjas oficiais do PdA (PS-024) — não compõem o conteúdo "padronizado" do cabeçalho.
        var tarjasRaw = ctx.Profile.Get("cabecalho.tarjasIgnoradas")
            ?? "EMISSÃO PARA COMENTÁRIOS DO CLIENTE|" +
               "NÃO É VÁLIDO PARA EXECUÇÃO|" +
               "DOCUMENTO CANCELADO|" +
               "EMISSÃO PARA COMENTÁRIOS";
        var tarjas = tarjasRaw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Cabeçalhos de primeira página (capa) seguem padrão próprio; ignora.
        // Stripa tokens de posicionamento residual + tarjas oficiais.
        var headers = ctx.Structure.Headers
            .Where(h => !string.Equals(h.Kind, "first", StringComparison.OrdinalIgnoreCase))
            .Select(h => h with { Text = StripTarjas(StripNoise(h.Text), tarjas) })
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
        // entre cabeçalhos que têm conteúdo textual significativo (>=10 caracteres alfabéticos).
        // Resíduos curtos costumam ser sobras de stripping de tarjas grandes.
        var headersComTexto = headers
            .Where(h => (h.Text?.Count(char.IsLetter) ?? 0) >= 10)
            .ToList();
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

    /// <summary>
    /// Remove ocorrências de tarjas oficiais (case-insensitive) do texto.
    /// Repete a remoção 2x para cobrir tarjas duplicadas (texto sobreposto).
    /// </summary>
    private static string StripTarjas(string text, IReadOnlyList<string> tarjas)
    {
        if (string.IsNullOrEmpty(text) || tarjas.Count == 0) return text;
        var result = text;
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var t in tarjas)
            {
                if (string.IsNullOrWhiteSpace(t)) continue;
                result = Regex.Replace(result, Regex.Escape(t), " ",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
        }
        return Regex.Replace(result, @"\s+", " ").Trim();
    }
}
