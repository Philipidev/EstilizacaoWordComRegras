using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.3 (letra h) — Correção de referências cruzadas. Dois sinais:
/// <list type="bullet">
/// <item>marcas de erro que o Word grava no texto do campo ("Erro! Indicador não definido",
/// "Erro! A origem da referência não foi encontrada"…), no corpo, cabeçalhos e rodapés;</item>
/// <item>campos REF/PAGEREF/NOTEREF que apontam para um indicador inexistente — o defeito
/// que ainda não virou marca de erro porque os campos não foram atualizados.</item>
/// </list>
/// As marcas são comparadas normalizadas (sem acento, sem pontuação, sem caixa): a versão
/// anterior comparava literalmente e não tinha a mensagem do Word em português para
/// referência a legenda ("A origem da referência não foi encontrada").
/// </summary>
public sealed class ReferenciasCruzadasCheck : IRuleCheck
{
    private const string DefaultErrorMarks =
        "Erro! Indicador não definido|Erro! O indicador não está definido|" +
        "Erro! A origem da referência não foi encontrada|Erro! Fonte de referência não encontrada|" +
        "Erro! Fonte de referência|Erro! Nenhum texto com o estilo especificado|" +
        "Error! Reference source not found|Error! Bookmark not defined|" +
        "Error! No text of specified style";

    public ReferenciasCruzadasCheck(ChecklistPadrao padrao = ChecklistPadrao.Cliente)
    {
        Ref = new ChecklistRef("PS-002", "4.3.3 (letra h)", padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var marks = DocumentoTexto.Lista(ctx.Profile.Get("referenciasCruzadas.marcasErro"), DefaultErrorMarks)
            .Select(m => (Original: m, Norm: DocumentoTexto.Normalizar(m)))
            .Where(m => m.Norm.Length > 0)
            .ToList();

        var violations = new List<Violation>();
        foreach (var p in ctx.Structure.Paragraphs)
        {
            if (Marca(p.Text) is { } mark)
                violations.Add(new Violation(Ref.ToString(), Severity.Error,
                    $"Referência cruzada com erro: '{mark}'.",
                    new ViolationLocation(p.ParagraphId, null, p.SectionIndex, "Referência cruzada")));
        }

        foreach (var hf in ctx.Structure.Headers.Concat(ctx.Structure.Footers))
        {
            if (Marca(hf.Text) is { } mark)
                violations.Add(new Violation(Ref.ToString(), Severity.Error,
                    $"Referência cruzada com erro no {DocumentoTexto.NomeDaParte("cabeçalho/rodapé", hf.Kind, hf.SectionIndex)}: '{mark}'.",
                    new ViolationLocation(null, hf.Kind, hf.SectionIndex, "Referência cruzada")));
        }

        // Um parágrafo já acusado pela marca de erro não precisa de um segundo comentário.
        var jaAcusados = violations.Select(v => v.Location?.ParagraphId).OfType<string>().ToHashSet();
        // Quem lê precisa saber ONDE está a referência quebrada, não o nome interno do indicador
        // ("_Ref223474267"), que só aparece com os códigos de campo à mostra.
        foreach (var r in ctx.Structure.BrokenReferences.Where(r => !jaAcusados.Contains(r.ParagraphId)))
        {
            var onde = Final(r.TextBefore);
            var mensagem = r.DisplayedText is { } exibido
                ? $"A referência cruzada «{exibido}»{(onde is null ? "" : $" (em «{onde}»)")} aponta para uma " +
                  "legenda ou título que não existe mais. O texto ainda aparece, mas ao atualizar os campos o " +
                  "Word troca por \"Erro! Indicador não definido\". Refaça em Inserir › Referência cruzada."
                // Campo sem resultado: não aparece no texto hoje, e por isso ninguém o vê na revisão
                // visual — mas a próxima atualização de campos escreve o erro no meio da frase.
                : $"Há uma referência cruzada vazia{(onde is null ? "" : $" logo depois de «{onde}»")} que aponta " +
                  "para uma legenda ou título que não existe mais. Hoje ela não aparece, mas ao atualizar os campos " +
                  "o Word escreve \"Erro! Indicador não definido\" nesse ponto. Remova o campo " +
                  "(Alt+F9 mostra os códigos de campo).";
            violations.Add(new Violation(Ref.ToString(), Severity.Error, mensagem,
                new ViolationLocation(r.ParagraphId, null, null, "Referência cruzada")));
        }

        var status = violations.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));

        // As últimas palavras antes do campo: o bastante para achar o ponto no texto.
        static string? Final(string texto)
        {
            var t = string.Join(' ', texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (t.Length == 0) return null;
            return t.Length <= 40 ? t : "…" + t[^40..].TrimStart();
        }

        string? Marca(string? texto)
        {
            var norm = DocumentoTexto.Normalizar(texto);
            if (norm.Length == 0) return null;
            return marks.FirstOrDefault(m => norm.Contains(m.Norm, StringComparison.Ordinal)).Original;
        }
    }
}
