using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.1 PdA — Folha de Rosto. Verifica se a folha de rosto contém:
/// nome da empresa contratante / descrição do projeto, título, data (mês/ano)
/// e codificação. Pesquisa nos primeiros parágrafos do documento.
/// Profile params (opcional):
///   folhaRosto.paragrafosIniciais (default: 60)
///   folhaRosto.contratante        (string a procurar)
///   folhaRosto.titulo             (string a procurar)
///   codificacao.pdaRegex          (regex)
/// </summary>
public sealed class FolhaRostoCheck : IRuleCheck
{
    private static readonly Regex MesAno = new(
        @"\b(jan|fev|mar|abr|mai|jun|jul|ago|set|out|nov|dez|janeiro|fevereiro|março|abril|maio|junho|julho|agosto|setembro|outubro|novembro|dezembro|\d{1,2})[\s/.-]+(?:de\s+)?\d{2,4}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ISemanticChecker? _semantic;

    /// <summary>
    /// Quando <paramref name="semantic"/> é informado, usa LLM como validação semântica
    /// complementar (apenas confirma que o conjunto extraído realmente parece uma folha
    /// de rosto). Sem ele, faz apenas checagem regex.
    /// </summary>
    public FolhaRostoCheck(ISemanticChecker? semantic = null)
    {
        _semantic = semantic;
    }

    public ChecklistRef Ref { get; } = new("PS-002", "4.3.1", ChecklistPadrao.Pda);

    public async Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        int paragrafos = ctx.Profile.GetInt("folhaRosto.paragrafosIniciais") ?? 60;
        // Folha de rosto = parágrafos iniciais + primeira tabela (geralmente o quadro
        // Características que aparece na capa em padrão PdA) + cabeçalhos de seção.
        var textoParagrafos = CoerenciaRevisoesCheck.TakeFolhaRosto(ctx.Structure.Paragraphs, paragrafos)
            .Select(p => p.Text);
        var textoTabelas = ctx.Structure.Tables.Take(2)
            .SelectMany(t => t.Cells.Select(c => c.Text));
        var textoHeaders = ctx.Structure.Headers.Select(h => h.Text);
        var folha = string.Join("\n", textoParagrafos.Concat(textoTabelas).Concat(textoHeaders));

        var contratante = ctx.Profile.Get("folhaRosto.contratante");
        var tituloEsperado = ctx.Profile.Get("folhaRosto.titulo");
        var pdaRx = ctx.Profile.Get("codificacao.pdaRegex");

        var violations = new List<Violation>();
        if (string.IsNullOrWhiteSpace(folha))
        {
            violations.Add(new Violation(Ref.ToString(), Severity.Error,
                "Folha de rosto vazia ou não extraída.",
                new ViolationLocation(null, null, null, "Folha de Rosto")));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(contratante)
                && !folha.Contains(contratante, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                    $"Nome da empresa contratante ('{contratante}') não localizado na folha de rosto.",
                    new ViolationLocation(null, null, null, "Folha de Rosto")));
            }

            if (!string.IsNullOrWhiteSpace(tituloEsperado)
                && !folha.Contains(tituloEsperado, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                    $"Título esperado ('{tituloEsperado}') não localizado na folha de rosto.",
                    new ViolationLocation(null, null, null, "Folha de Rosto")));
            }

            if (!MesAno.IsMatch(folha))
            {
                violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                    "Data de emissão (mês/ano) não localizada na folha de rosto.",
                    new ViolationLocation(null, null, null, "Folha de Rosto")));
            }

            if (!string.IsNullOrWhiteSpace(pdaRx))
            {
                var pattern = SearchPattern(pdaRx);
                if (!Regex.IsMatch(folha, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    violations.Add(new Violation(Ref.ToString(), Severity.Error,
                        $"Codificação (padrão {pdaRx}) não localizada na folha de rosto.",
                        new ViolationLocation(null, null, null, "Folha de Rosto")));
                }
            }
        }

        // Validação semântica via LLM (opcional): só ativa se nenhum erro de regex já foi
        // detectado E semantic está disponível. Serve para evitar falsos negativos quando o
        // texto extraído não cobre tudo que deveria mas o LLM, lendo o agregado, encontra.
        string? llmNote = null;
        if (_semantic is not null && !violations.Any(v => v.Severity == Severity.Error))
        {
            try
            {
                var instrucao =
                    "Avalie se o conteúdo a seguir representa uma folha de rosto técnica do padrão PdA " +
                    "contendo: nome da empresa contratante / descrição do projeto, título do documento, " +
                    "data de emissão (mês/ano) e codificação. 'conforme' = true se TODOS os 4 elementos " +
                    "estão claramente presentes no texto.";
                var verdict = await _semantic.EvaluateAsync(instrucao, folha, cancellationToken);
                llmNote = $"LLM: {verdict.Justificativa}";
                if (!verdict.Conforme)
                {
                    violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                        $"Verificação semântica (LLM) reprovou folha de rosto: {verdict.Justificativa}",
                        new ViolationLocation(null, null, null, "Folha de Rosto")));
                }
            }
            catch (Exception ex)
            {
                llmNote = $"LLM indisponível: {ex.Message}";
            }
        }

        var status = violations.Count == 0 ? CheckStatus.Passed
                   : violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : CheckStatus.Skipped;
        return new RuleCheckResult(Ref, status, violations, Note: llmNote);
    }

    private static string SearchPattern(string regex)
    {
        var p = regex.Trim();
        if (p.StartsWith('^')) p = p[1..];
        if (p.EndsWith('$')) p = p[..^1];
        return p + @"(?:-\d+)?";
    }
}
