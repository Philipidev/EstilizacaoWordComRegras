using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Infrastructure.OpenXml;
using Xunit;

namespace WordComplianceValidator.Infrastructure.Tests.OpenXml;

public class DocxExtractorAndCommentInserterTests
{
    [Fact]
    public void Extract_reads_styles_paragraphs_and_header()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wcv-extract-{Guid.NewGuid():N}.docx");
        try
        {
            CreateSampleDocx(path, headerText: "Cliente: ACME — Código Documento: D-001");

            var extractor = new DocxStructureExtractor();
            var structure = extractor.ExtractFromFile(path);

            structure.Styles.Should().ContainKey("Heading1");
            structure.Styles["Heading1"].Font.Should().Be("Calibri");
            structure.Headers.Should().Contain(h => h.Text.Contains("ACME"));
            structure.Paragraphs.Should().NotBeEmpty();
            structure.HasPendingTrackChanges.Should().BeFalse();
            structure.HasOpenComments.Should().BeFalse();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void CommentInserter_creates_word_comments_in_output()
    {
        var src = Path.Combine(Path.GetTempPath(), $"wcv-src-{Guid.NewGuid():N}.docx");
        var dst = Path.Combine(Path.GetTempPath(), $"wcv-dst-{Guid.NewGuid():N}.docx");
        try
        {
            CreateSampleDocx(src, headerText: "Cliente: ACME");
            var inserter = new CommentInserter();
            var violations = new List<Violation>
            {
                new("R001", Severity.Error, "Header fora do padrão.", new ViolationLocation(null, "header", null)),
                new("R002", Severity.Warning, "Estilo titulo1 fora do padrão.", null)
            };

            inserter.InsertComments(src, dst, violations);

            using var doc = WordprocessingDocument.Open(dst, isEditable: false);
            var comments = doc.MainDocumentPart!.WordprocessingCommentsPart!.Comments.Elements<Comment>().ToList();
            comments.Should().HaveCount(2);
            comments[0].InnerText.Should().Contain("[R001]");
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(dst)) File.Delete(dst);
        }
    }

    private static void CreateSampleDocx(string path, string headerText)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;

        var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            new Style(
                new StyleName { Val = "heading 1" },
                new StyleRunProperties(
                    new RunFonts { Ascii = "Calibri" },
                    new FontSize { Val = "28" },
                    new Bold()))
            {
                Type = StyleValues.Paragraph,
                StyleId = "Heading1"
            });
        stylesPart.Styles.Save();

        var headerPart = main.AddNewPart<HeaderPart>("rIdH1");
        headerPart.Header = new Header(new Paragraph(new Run(new Text(headerText))));
        headerPart.Header.Save();

        body.AppendChild(new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
            new Run(new Text("Título do Documento"))) { ParagraphId = "1" });

        body.AppendChild(new Paragraph(
            new Run(new Text("Conteúdo de teste."))) { ParagraphId = "2" });

        var sectionProps = new SectionProperties(
            new HeaderReference { Id = "rIdH1", Type = HeaderFooterValues.Default });
        body.AppendChild(sectionProps);

        main.Document.Save();
    }
}
