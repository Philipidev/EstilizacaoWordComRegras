using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using AwesomeAssertions;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Infrastructure.Checks;
using WordComplianceValidator.Infrastructure.OpenXml;
using WordComplianceValidator.Infrastructure.Tests.Fixtures;

namespace WordComplianceValidator.Infrastructure.Tests.OpenXml;

/// <summary>
/// O que o extrator entrega como cabeçalho precisa ser o que o Word exibe. Partes 'first' e
/// 'even' existem no pacote mesmo quando a seção não as usa, e eram tratadas como visíveis —
/// origem de uma reprovação por "tarja remanescente" que nenhum leitor via.
/// </summary>
public class CabecalhosVisiveisTests
{
    [Fact]
    public void Cabecalho_first_sem_primeira_pagina_diferente_fica_oculto()
    {
        var s = DocxFixtureBuilder.Novo()
            .Paragrafo("Corpo.")
            .Cabecalho("CABEÇALHO PADRÃO")
            .Cabecalho("SÓ NA PRIMEIRA", kind: "first")
            .PrimeiraPaginaDiferente(false)
            .Extrair();

        s.Headers.Should().ContainSingle().Which.Text.Should().Be("CABEÇALHO PADRÃO");
        s.HiddenHeaderFooters.Should().ContainSingle(h => h.Kind == "first" && h.Text == "SÓ NA PRIMEIRA");
    }

    [Fact]
    public void Cabecalho_first_com_primeira_pagina_diferente_e_visivel()
    {
        var s = DocxFixtureBuilder.Novo()
            .Paragrafo("Corpo.")
            .Cabecalho("CABEÇALHO PADRÃO")
            .Cabecalho("SÓ NA PRIMEIRA", kind: "first")
            .Extrair();

        s.Headers.Should().HaveCount(2);
        s.HiddenHeaderFooters.Should().BeEmpty();
        s.Sections.Should().ContainSingle().Which.TitlePage.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Cabecalho_even_depende_de_pares_e_impares_diferentes(bool ativo, bool visivel)
    {
        var s = DocxFixtureBuilder.Novo()
            .Paragrafo("Corpo.")
            .Cabecalho("ÍMPAR")
            .Cabecalho("PAR", kind: "even")
            .ParesImparesDiferentes(ativo)
            .Extrair();

        s.Headers.Any(h => h.Kind == "even").Should().Be(visivel);
        s.HiddenHeaderFooters.Any(h => h.Kind == "even").Should().Be(!visivel);
    }

    /// <summary>
    /// Uma seção que não declara o cabeçalho 'first' herda o da anterior. Se a anterior o
    /// declarou sem usá-lo e a seguinte ativa "primeira página diferente", ele aparece.
    /// </summary>
    [Fact]
    public void Cabecalho_first_herdado_aparece_na_secao_que_ativa_a_opcao()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;

            var padrao = main.AddNewPart<HeaderPart>();
            padrao.Header = new Header(new Paragraph(new Run(new Text("PADRÃO"))));
            var primeira = main.AddNewPart<HeaderPart>();
            primeira.Header = new Header(new Paragraph(new Run(new Text("PRIMEIRA"))));

            // Seção 1: declara os dois, sem titlePg.
            body.AppendChild(new Paragraph(
                new ParagraphProperties(new SectionProperties(
                    new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(padrao) },
                    new HeaderReference { Type = HeaderFooterValues.First, Id = main.GetIdOfPart(primeira) })),
                new Run(new Text("Seção 1"))));

            // Seção 2: não declara nada, mas ativa titlePg — herda o 'first' da seção 1.
            body.AppendChild(new Paragraph(new Run(new Text("Seção 2"))));
            body.AppendChild(new SectionProperties(new TitlePage()));
            main.Document.Save();
        }

        stream.Position = 0;
        var s = new DocxStructureExtractor().Extract(stream, "heranca.docx");

        s.Headers.Select(h => h.Text).Should().BeEquivalentTo("PADRÃO", "PRIMEIRA");
        s.HiddenHeaderFooters.Should().BeEmpty();
    }

    [Fact]
    public void Texto_de_marca_dagua_VML_e_extraido()
    {
        var s = DocxFixtureBuilder.Novo()
            .Paragrafo("Corpo.")
            .MarcaDagua("EMISSÃO PARA COMENTÁRIOS DO CLIENTE")
            .Extrair();

        s.Headers.Should().ContainSingle()
            .Which.Text.Should().Contain("EMISSÃO PARA COMENTÁRIOS DO CLIENTE");
    }

    /// <summary>
    /// Caixa de texto moderna vem em <c>mc:AlternateContent</c>: a versão DrawingML no Choice e
    /// uma cópia VML no Fallback. Ler as duas duplicava o texto.
    /// </summary>
    [Fact]
    public void Caixa_de_texto_em_AlternateContent_nao_duplica_o_texto()
    {
        const string caixa =
            "<w:p><w:r><mc:AlternateContent>" +
            "<mc:Choice Requires=\"wps\"><w:drawing><wp:inline><wp:extent cx=\"100\" cy=\"100\"/>" +
            "<wp:docPr id=\"1\" name=\"Caixa\"/><a:graphic><a:graphicData " +
            "uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\"><wps:wsp>" +
            "<wps:txbx><w:txbxContent><w:p><w:r><w:t>TARJA ÚNICA</w:t></w:r></w:p></w:txbxContent></wps:txbx>" +
            "<wps:bodyPr/></wps:wsp></a:graphicData></a:graphic></wp:inline></w:drawing></mc:Choice>" +
            "<mc:Fallback><w:pict><v:shape><v:textbox><w:txbxContent><w:p><w:r><w:t>TARJA ÚNICA</w:t></w:r></w:p>" +
            "</w:txbxContent></v:textbox></v:shape></w:pict></mc:Fallback>" +
            "</mc:AlternateContent></w:r></w:p>";

        var s = DocxFixtureBuilder.Novo()
            .Paragrafo("Corpo.")
            .CabecalhoXml(caixa)
            .Extrair();

        var texto = DocumentoTexto.Normalizar(s.Headers.Single().Text);
        texto.Should().Be("tarja unica");
    }
}
