using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordComplianceValidator.Core.Abstractions;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.OpenXml;

public sealed class CommentInserter : ICommentInserter
{
    public int InsertComments(
        string sourcePath,
        string destinationPath,
        IReadOnlyList<Violation> violations,
        string author = "Compliance Validator",
        string initials = "CV")
    {
        File.Copy(sourcePath, destinationPath, overwrite: true);

        using var doc = WordprocessingDocument.Open(destinationPath, isEditable: true);
        var main = doc.MainDocumentPart
            ?? throw new InvalidOperationException("Documento .docx sem MainDocumentPart.");

        var commentsPart = main.WordprocessingCommentsPart ?? main.AddNewPart<WordprocessingCommentsPart>();
        commentsPart.Comments ??= new Comments();

        int commentId = commentsPart.Comments.Elements<Comment>()
            .Select(c => int.TryParse(c.Id?.Value, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        // Mesma enumeração e mesmos ids do extrator: é o que garante que o ParagraphId de uma
        // violação aponte para o parágrafo em que ela foi encontrada.
        var comId = main.Document?.Body is { } body
            ? DocxStructureExtractor.ParagrafosComId(body).ToList()
            : new List<(Paragraph Paragrafo, string Id)>();
        var paragraphs = comId.Select(x => x.Paragrafo).ToList();
        var paragraphsById = comId.ToDictionary(x => x.Id, x => x.Paragrafo, StringComparer.OrdinalIgnoreCase);

        var firstParagraph = paragraphs.FirstOrDefault();
        var primeiroPorSecao = MapearPrimeiroParagrafoPorSecao(paragraphs);

        // Um mesmo fato costuma violar vários itens do checklist — o total de páginas fixo cai
        // em três. O relatório precisa do veredito item a item, mas quem lê no Word não precisa
        // do mesmo texto três vezes: aqui os idênticos viram um comentário só, citando todas as
        // regras. A ordem original é preservada.
        var agrupadas = violations
            .GroupBy(v => (Mensagem: v.Message.Trim(), Alvo: ResolveTarget(v, paragraphsById, primeiroPorSecao, firstParagraph)))
            .Select(g => (g.Key.Alvo, g.Key.Mensagem, Regras: g.Select(v => v.RuleId).Distinct().ToList()))
            .ToList();

        var inseridos = 0;
        foreach (var (target, mensagem, regras) in agrupadas)
        {
            if (target is null) continue;

            var comment = new Comment
            {
                Id = commentId.ToString(),
                Author = author,
                Initials = initials,
                Date = DateTime.UtcNow
            };
            comment.AppendChild(new Paragraph(new Run(
                new Text($"[{string.Join("; ", regras)}] {mensagem}"))));
            commentsPart.Comments.AppendChild(comment);

            var rangeStart = new CommentRangeStart { Id = commentId.ToString() };
            var rangeEnd = new CommentRangeEnd { Id = commentId.ToString() };
            var reference = new Run(new CommentReference { Id = commentId.ToString() });

            // O w:pPr tem de ser o primeiro filho do parágrafo; inserir o início do comentário
            // antes dele gera XML fora do schema, que o Word pode recusar ou pedir para reparar.
            if (target.ParagraphProperties is { } pPr) target.InsertAfter(rangeStart, pPr);
            else target.InsertAt(rangeStart, 0);
            target.AppendChild(rangeEnd);
            target.AppendChild(reference);

            commentId++;
            inseridos++;
        }

        commentsPart.Comments.Save();
        // As configurações do documento ficam como estavam. A cópia gravava w:updateFields, e o
        // Word abria perguntando "Este documento contém campos que podem fazer referência a
        // outros arquivos. Deseja atualizar os campos?" — um alerta que assusta quem só quer ler
        // os comentários. Comentários ficam na margem e não mudam a paginação, então não há o
        // que recalcular.
        main.Document?.Save();
        return inseridos;
    }

    private static Paragraph? ResolveTarget(
        Violation v,
        IReadOnlyDictionary<string, Paragraph> byId,
        IReadOnlyDictionary<int, Paragraph> primeiroPorSecao,
        Paragraph? fallback)
    {
        if (v.Location?.ParagraphId is { } pid && byId.TryGetValue(pid, out var byPid))
            return byPid;

        // Achado de cabeçalho/rodapé não tem parágrafo de corpo correspondente — comentário do
        // Word precisa de âncora no corpo. Ancorar na primeira folha da seção afetada não é
        // exato, mas leva o leitor à região certa; o padrão anterior, o primeiro parágrafo do
        // documento, empilhava todos esses achados na folha índice.
        if (v.Location?.SectionIndex is { } secao && primeiroPorSecao.TryGetValue(secao, out var porSecao))
            return porSecao;

        return fallback;
    }

    /// <summary>
    /// Primeiro parágrafo de cada seção. A contagem espelha a do extrator: um <c>sectPr</c>
    /// dentro do <c>pPr</c> encerra a seção, então o parágrafo seguinte já é da próxima.
    /// </summary>
    private static Dictionary<int, Paragraph> MapearPrimeiroParagrafoPorSecao(
        IReadOnlyList<Paragraph> paragraphs)
    {
        var mapa = new Dictionary<int, Paragraph>();
        var secao = 0;

        foreach (var p in paragraphs)
        {
            if (!mapa.ContainsKey(secao) && !string.IsNullOrWhiteSpace(p.InnerText))
                mapa[secao] = p;

            if (p.ParagraphProperties?.SectionProperties is not null) secao++;
        }
        return mapa;
    }
}
