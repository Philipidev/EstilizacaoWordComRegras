using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// Check genérico movido pela própria planilha CL-001: usa a coluna "Descrição" da entrada
/// como instrução de verificação e um pacote de evidências como conteúdo.
/// <para>
/// É o que permite cobrir dezenas de itens do checklist sem escrever uma classe por regra —
/// as descrições do CL-001 já são instruções de verificação autocontidas em PT-BR. Regras
/// com sinal determinístico continuam merecendo um <see cref="IRuleCheck"/> dedicado; este
/// motor é para as que exigem julgamento.
/// </para>
/// </summary>
public sealed class SemanticChecklistCheck : IRuleCheck
{
    private readonly ChecklistEntry _entry;
    private readonly ISemanticChecker _semantic;
    private readonly IEvidenceSelector _evidence;
    private readonly string? _modelo;

    public SemanticChecklistCheck(
        ChecklistEntry entry,
        ISemanticChecker semantic,
        IEvidenceSelector evidence,
        string? modelo = null)
    {
        _entry = entry;
        _semantic = semantic;
        _evidence = evidence;
        _modelo = modelo;
        Ref = entry.Ref;
    }

    public ChecklistRef Ref { get; }

    public async Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var instrucao = MontarInstrucao(_entry);
        var conteudo = _evidence.Build(ctx, _entry);

        var avaliacao = await _semantic.AvaliarAsync(instrucao, conteudo, _modelo, cancellationToken);

        var notaModelo = _modelo is null ? "" : $" [modelo: {_modelo}]";

        switch (avaliacao.Status)
        {
            case SemanticStatus.Conforme:
                return new RuleCheckResult(Ref, CheckStatus.Passed, Array.Empty<Violation>(),
                    Note: (avaliacao.Justificativa ?? "Conforme (avaliação semântica).") + notaModelo);

            case SemanticStatus.NaoAplicavel:
                return new RuleCheckResult(Ref, CheckStatus.Skipped, Array.Empty<Violation>(),
                    Note: (avaliacao.Justificativa ?? "Não aplicável a este documento.") + notaModelo);

            default:
                var violations = avaliacao.Achados
                    .Select(a => new Violation(
                        RuleId: Ref.ToString(),
                        Severity: a.Severidade,
                        Message: a.Mensagem,
                        Location: Localizar(ctx, a.TrechoAncora)))
                    .ToList();

                var status = violations.Any(v => v.Severity == Severity.Error)
                    ? CheckStatus.Failed
                    : CheckStatus.Skipped;

                return new RuleCheckResult(Ref, status, violations,
                    Note: (avaliacao.Justificativa ?? "Não conformidade detectada.") + notaModelo);
        }
    }

    private static string MontarInstrucao(ChecklistEntry entry)
    {
        var partes = new List<string>
        {
            $"Procedimento {entry.Ref.Ps} — {entry.ProcedimentoTitulo}",
            $"Item {entry.Ref.Item} ({entry.Ref.Padrao}) — {entry.Titulo}",
            entry.Descricao
        };
        if (!string.IsNullOrWhiteSpace(entry.Observacao))
            partes.Add($"Observação do checklist: {entry.Observacao}");

        return string.Join("\n", partes.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    // Comprimento normalizado mínimo, em caracteres, para que um texto sirva de âncora.
    // Vale tanto para a citação do modelo quanto para o parágrafo candidato.
    private const int MinimoAncora = 8;

    /// <summary>
    /// Resolve o trecho devolvido pelo LLM para um parágrafo real do documento, para que o
    /// comentário seja inserido no lugar certo em vez de ficar solto no fim do arquivo.
    /// </summary>
    private static ViolationLocation Localizar(DocumentContext ctx, string? trecho)
    {
        if (string.IsNullOrWhiteSpace(trecho))
            return new ViolationLocation(null, null, null, "Avaliação semântica");

        var alvo = DocumentoTexto.Normalizar(trecho);
        if (alvo.Length < MinimoAncora)
            return new ViolationLocation(null, null, null, Resumo(trecho));

        // O piso vale para os dois lados. A contenção reversa (parágrafo contido na citação)
        // existe para ancorar quando o modelo cita uma linha inteira da evidência, mas sem
        // piso ela casa qualquer célula de uma letra.
        var candidatos = ctx.Structure.Paragraphs
            .Select(p => (Par: p, Texto: DocumentoTexto.Normalizar(p.Text)))
            .Where(x => x.Texto.Length >= MinimoAncora
                     && (x.Texto.Contains(alvo, StringComparison.Ordinal)
                      || alvo.Contains(x.Texto, StringComparison.Ordinal)))
            .ToList();

        // Ordem de preferência, e não "o primeiro que casar": a ordem do documento não diz
        // nada sobre qualidade do casamento. Uma citação "assinatura do aprovador" casava a
        // primeira célula "Aprovado" de uma tabela de ensaio, porque ela é substring da
        // citação e vem antes no documento.
        var paragrafo = candidatos
            .Select(x => new
            {
                x.Par,
                x.Texto,
                Rank = x.Texto == alvo ? 0
                     : x.Texto.Contains(alvo, StringComparison.Ordinal) ? 1
                     : 2
            })
            .OrderBy(x => x.Rank)
            // Rank 1 (parágrafo contém a citação): o menor é o mais justo. Rank 2 (parágrafo
            // contido na citação): o maior é o que cobre mais do trecho citado.
            .ThenBy(x => x.Rank == 1 ? x.Texto.Length : -x.Texto.Length)
            .Select(x => x.Par)
            .FirstOrDefault();

        return paragrafo is null
            ? new ViolationLocation(null, null, null, Resumo(trecho))
            : new ViolationLocation(paragrafo.ParagraphId, null, paragrafo.SectionIndex, Resumo(trecho));
    }

    private static string Resumo(string texto)
    {
        var t = texto.Trim().Replace("\n", " ");
        return t.Length <= 80 ? t : t[..80] + "…";
    }
}
