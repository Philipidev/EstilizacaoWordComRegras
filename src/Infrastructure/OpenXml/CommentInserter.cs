using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordComplianceValidator.Core.Abstractions;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.OpenXml;

public sealed class CommentInserter : ICommentInserter
{
    public void InsertComments(
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

        var paragraphs = main.Document.Body?.Descendants<Paragraph>().ToList() ?? new List<Paragraph>();
        var paragraphsById = paragraphs.ToDictionary(
            p => p.ParagraphId?.Value ?? string.Empty,
            p => p,
            StringComparer.OrdinalIgnoreCase);

        var firstParagraph = paragraphs.FirstOrDefault();

        foreach (var v in violations)
        {
            var target = ResolveTarget(v, paragraphsById, firstParagraph);
            if (target is null) continue;

            var comment = new Comment
            {
                Id = commentId.ToString(),
                Author = author,
                Initials = initials,
                Date = DateTime.UtcNow
            };
            comment.AppendChild(new Paragraph(new Run(new Text($"[{v.RuleId}] {v.Message}"))));
            commentsPart.Comments.AppendChild(comment);

            var rangeStart = new CommentRangeStart { Id = commentId.ToString() };
            var rangeEnd = new CommentRangeEnd { Id = commentId.ToString() };
            var reference = new Run(new CommentReference { Id = commentId.ToString() });

            target.InsertBefore(rangeStart, target.FirstChild);
            target.AppendChild(rangeEnd);
            target.AppendChild(reference);

            commentId++;
        }

        commentsPart.Comments.Save();
        main.Document.Save();
    }

    private static Paragraph? ResolveTarget(
        Violation v,
        IReadOnlyDictionary<string, Paragraph> byId,
        Paragraph? fallback)
    {
        if (v.Location?.ParagraphId is { } pid && byId.TryGetValue(pid, out var byPid))
            return byPid;
        return fallback;
    }
}
