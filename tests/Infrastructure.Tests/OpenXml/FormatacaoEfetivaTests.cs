using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using AwesomeAssertions;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Infrastructure.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;

namespace WordComplianceValidator.Infrastructure.Tests.OpenXml;

/// <summary>
/// Fonte e tamanho efetivos do corpo, que alimentam as regras de formatação (PS-002 4.3.6.x).
/// </summary>
public class FormatacaoEfetivaTests
{
    /// <summary>
    /// Tamanho 12 no estilo Normal e nada nos docDefaults (como no RN-816): parágrafo sem
    /// pStyle saía com tamanho nulo, porque só os docDefaults eram consultados. A fonte vinha
    /// do tema (asciiTheme), e saía nula também — a regra de fonte nunca era avaliada.
    /// </summary>
    [Fact]
    public void Paragrafo_sem_estilo_usa_o_Normal_e_resolve_fonte_do_tema()
    {
        var s = Extrair(main =>
        {
            var estilos = main.AddNewPart<StyleDefinitionsPart>();
            estilos.Styles = new Styles(
                new DocDefaults(new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { AsciiTheme = ThemeFontValues.MinorHighAnsi }))),
                new Style(new StyleName { Val = "Normal" },
                          new StyleRunProperties(new FontSize { Val = "24" }))
                {
                    Type = StyleValues.Paragraph, StyleId = "Normal", Default = true
                });
            estilos.Styles.Save();

            var tema = main.AddNewPart<ThemePart>();
            tema.Theme = new A.Theme(new A.ThemeElements(
                new A.ColorScheme { Name = "x" },
                new A.FontScheme(
                    new A.MajorFont(new A.LatinFont { Typeface = "Cambria" }, new A.EastAsianFont { Typeface = "" }, new A.ComplexScriptFont { Typeface = "" }),
                    new A.MinorFont(new A.LatinFont { Typeface = "Calibri" }, new A.EastAsianFont { Typeface = "" }, new A.ComplexScriptFont { Typeface = "" }))
                { Name = "x" },
                new A.FormatScheme { Name = "x" }))
            { Name = "x" };
            tema.Theme.Save();

            main.Document!.Body!.AppendChild(new Paragraph(new Run(new Text("Corpo do relatório."))));
        });

        var corpo = s.Paragraphs.Single();
        corpo.EffectiveFontSize.Should().Be(12);
        corpo.EffectiveFont.Should().Be("Calibri");
    }

    /// <summary>Texto dentro de hyperlink era ignorado na ponderação da fonte.</summary>
    [Fact]
    public void Runs_dentro_de_hyperlink_contam_na_fonte_do_paragrafo()
    {
        var s = Extrair(main =>
        {
            main.Document!.Body!.AppendChild(new Paragraph(
                new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }), new Text("Ver ")),
                new Hyperlink(new Run(new RunProperties(new RunFonts { Ascii = "Arial" }),
                    new Text("o documento de referência completo citado")))
                { Anchor = "x" }));
        });

        s.Paragraphs.Single().EffectiveFont.Should().Be("Arial");
    }

    private static DocumentStructure Extrair(Action<MainDocumentPart> montar)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            montar(main);
            main.Document!.Body!.AppendChild(new SectionProperties());
            main.Document.Save();
        }
        stream.Position = 0;
        return new DocxStructureExtractor().Extract(stream, "formatacao.docx");
    }
}
