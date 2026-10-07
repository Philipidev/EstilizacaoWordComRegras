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
    // Mês/ano de emissão: mês por extenso ou abreviado seguido do ano ("março de 2026",
    // "mar/26"), mês/ano numérico com ano de 4 dígitos ("03/2026") ou data completa
    // ("26/03/26"). A versão anterior aceitava qualquer "número separador número" e casava
    // "26-04" dentro da codificação e o "7/99" da paginação — a regra nunca falhava.
    private static readonly Regex MesAno = new(
        @"\b(?:jan(?:eiro)?|fev(?:ereiro)?|mar(?:ço|co)?|abr(?:il)?|mai(?:o)?|jun(?:ho)?|jul(?:ho)?|ago(?:sto)?|set(?:embro)?|out(?:ubro)?|nov(?:embro)?|dez(?:embro)?)\.?[\s/.-]+(?:de\s+)?(?:\d{4}|\d{2})\b" +
        @"|\b(?:0?[1-9]|1[0-2])[/.-](?:19|20)\d{2}\b" +
        @"|\b\d{1,2}/\d{1,2}/(?:\d{4}|\d{2})\b",
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
        // Folha de rosto = capa até o primeiro título (células de tabela incluídas).
        // Cabeçalhos ficam de fora: repetem código e título em toda página, e era por eles que
        // a regra passava mesmo com a capa incompleta. O parâmetro do profile só limita
        // documentos sem título nem índice, onde não há fronteira natural.
        var limite = ctx.Profile.GetInt("folhaRosto.paragrafosIniciais") ?? 600;
        var folha = string.Join("\n", DocumentoTexto.FolhaDeRosto(ctx.Structure, limite).Select(p => p.Text));

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
                if (Codificacao.Encontrar(folha, pdaRx) is null)
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
}
