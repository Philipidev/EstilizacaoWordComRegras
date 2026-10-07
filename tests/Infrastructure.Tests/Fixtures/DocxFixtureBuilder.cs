using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Infrastructure.OpenXml;

namespace WordComplianceValidator.Infrastructure.Tests.Fixtures;

/// <summary>
/// Constrói .docx mínimos em memória para o corpus negativo.
/// <para>
/// Os documentos de referência do repositório são conformes — eles provam ausência de falso
/// positivo, mas não provam que uma regra detecta o defeito que deveria detectar. Estas
/// fixtures introduzem defeitos deliberados para medir isso.
/// </para>
/// </summary>
public sealed class DocxFixtureBuilder
{
    private readonly List<Action<Body>> _blocos = [];
    private readonly List<(string Kind, Action<Header> Build)> _headers = [];
    private readonly List<(string Kind, Action<Footer> Build)> _footers = [];

    public static DocxFixtureBuilder Novo() => new();

    public DocxFixtureBuilder Paragrafo(
        string texto, string? estilo = null, string? fonte = "Times New Roman",
        double? tamanho = 12, string? alinhamento = null)
    {
        _blocos.Add(body => body.AppendChild(MontarParagrafo(texto, estilo, fonte, tamanho, alinhamento, null)));
        return this;
    }

    /// <summary>Título com nível de outline explícito (0 = Título 1).</summary>
    public DocxFixtureBuilder Titulo(string texto, int nivelOutline)
    {
        _blocos.Add(body => body.AppendChild(
            MontarParagrafo(texto, $"Heading{nivelOutline + 1}", "Times New Roman", 14, null, nivelOutline)));
        return this;
    }

    /// <summary>Entrada de índice: parágrafo com campo PAGEREF, como o Word gera num TOC.</summary>
    public DocxFixtureBuilder EntradaIndice(string texto)
    {
        _blocos.Add(body =>
        {
            var p = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "TOC1" }));
            p.AppendChild(new Run(new Text(texto) { Space = SpaceProcessingModeValues.Preserve }));
            p.AppendChild(new Run(new FieldCode(" PAGEREF _Toc1 \\h ")
            { Space = SpaceProcessingModeValues.Preserve }));
            body.AppendChild(p);
        });
        return this;
    }

    /// <summary>
    /// Entrada de índice como o Word realmente a monta: numeração, <c>w:tab</c>, título,
    /// <c>w:tab</c> e o resultado do campo PAGEREF. Os tabs são o que separa "10.4" do
    /// título e o título do número da página.
    /// </summary>
    public DocxFixtureBuilder EntradaIndiceComTabs(string numero, string titulo, string pagina)
    {
        _blocos.Add(body =>
        {
            var p = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "TOC1" }));
            p.AppendChild(new Run(new Text(numero) { Space = SpaceProcessingModeValues.Preserve }));
            p.AppendChild(new Run(new TabChar()));
            p.AppendChild(new Run(new Text(titulo) { Space = SpaceProcessingModeValues.Preserve }));
            p.AppendChild(new Run(new TabChar()));
            p.AppendChild(new Run(new FieldCode(" PAGEREF _Toc1 \\h ")
            { Space = SpaceProcessingModeValues.Preserve }));
            p.AppendChild(new Run(new Text(pagina) { Space = SpaceProcessingModeValues.Preserve }));
            body.AppendChild(p);
        });
        return this;
    }

    /// <summary>Parágrafo do corpo a partir de XML cru de <c>w:p</c> (prefixos de <see cref="Namespaces"/>).</summary>
    public DocxFixtureBuilder ParagrafoXml(string paragrafoXml)
    {
        _blocos.Add(body =>
        {
            var bruto = new Body($"<w:body {Namespaces}>{paragrafoXml}</w:body>");
            foreach (var filho in bruto.ChildElements.ToList())
                body.AppendChild(filho.CloneNode(true));
        });
        return this;
    }

    /// <summary>Tabela com número arbitrário de colunas por linha, como a folha índice do cliente.</summary>
    public DocxFixtureBuilder TabelaGrade(params string[][] linhas)
    {
        _blocos.Add(body =>
        {
            var t = new Table();
            foreach (var linha in linhas)
                t.AppendChild(new TableRow(linha.Select(Celula).ToArray<OpenXmlElement>()));
            body.AppendChild(t);
        });
        return this;
    }

    /// <summary>Tabela de uma celula contendo varios paragrafos, como a celula de titulo da capa.</summary>
    public DocxFixtureBuilder CelulaComVariosParagrafos(params string[] paragrafos)
    {
        _blocos.Add(body =>
        {
            var celula = new TableCell();
            foreach (var texto in paragrafos)
                celula.AppendChild(new Paragraph(new Run(
                    new Text(texto) { Space = SpaceProcessingModeValues.Preserve })));
            body.AppendChild(new Table(new TableRow(celula)));
        });
        return this;
    }

    /// <summary>Tabela simples rótulo→valor, usada para o quadro "Características do Documento".</summary>
    public DocxFixtureBuilder Tabela(string primeiraLinha, params (string Rotulo, string Valor)[] linhas)
    {
        _blocos.Add(body =>
        {
            var t = new Table();
            t.AppendChild(new TableRow(Celula(primeiraLinha), Celula("")));
            foreach (var (rotulo, valor) in linhas)
                t.AppendChild(new TableRow(Celula(rotulo), Celula(valor)));
            body.AppendChild(t);
        });
        return this;
    }

    public DocxFixtureBuilder Cabecalho(string texto, bool comCampoPagina = false, string kind = "default")
    {
        _headers.Add((kind, header =>
        {
            var p = new Paragraph(new Run(new Text(texto) { Space = SpaceProcessingModeValues.Preserve }));
            if (comCampoPagina)
                p.AppendChild(new Run(new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }));
            header.AppendChild(p);
        }
        ));
        return this;
    }

    /// <summary>
    /// Cabeçalho a partir de XML cru (conteúdo de <c>w:hdr</c>), para estruturas que o SDK
    /// tipado torna verbosas: marca d'água VML, caixa de texto em <c>mc:AlternateContent</c>.
    /// Os prefixos w, v, o, w10, mc, wps, a e wp já estão declarados.
    /// </summary>
    public DocxFixtureBuilder CabecalhoXml(string conteudoXml, string kind = "default")
    {
        _headers.Add((kind, header =>
        {
            var xml = $"<w:hdr {Namespaces}>{conteudoXml}</w:hdr>";
            var bruto = new Header(xml);
            foreach (var filho in bruto.ChildElements.ToList())
                header.AppendChild(filho.CloneNode(true));
            foreach (var ns in bruto.NamespaceDeclarations)
                header.AddNamespaceDeclaration(ns.Key, ns.Value);
        }
        ));
        return this;
    }

    /// <summary>Marca d'água do Word: texto no atributo <c>string</c> de um v:textpath.</summary>
    public DocxFixtureBuilder MarcaDagua(string texto, string kind = "default") =>
        CabecalhoXml(
            "<w:p><w:r><w:pict><v:shape id=\"PowerPlusWaterMarkObject1\" type=\"#_x0000_t136\" " +
            "style=\"position:absolute;width:400pt;height:40pt;rotation:315\" fillcolor=\"red\" stroked=\"f\">" +
            $"<v:textpath style=\"font-family:&quot;Times New Roman&quot;\" string=\"{texto}\"/>" +
            "</v:shape></w:pict></w:r></w:p>", kind);

    public const string Namespaces =
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
        "xmlns:v=\"urn:schemas-microsoft-com:vml\" " +
        "xmlns:o=\"urn:schemas-microsoft-com:office:office\" " +
        "xmlns:w10=\"urn:schemas-microsoft-com:office:word\" " +
        "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" " +
        "xmlns:wp14=\"http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing\"";

    private bool? _primeiraPaginaDiferente;
    private bool? _paresImparesDiferentes;

    /// <summary>
    /// Controla <c>w:titlePg</c>. Sem chamada, segue o Word: ligado quando há cabeçalho ou
    /// rodapé 'first' — é o que o Word faz ao criar um.
    /// </summary>
    public DocxFixtureBuilder PrimeiraPaginaDiferente(bool ativo)
    {
        _primeiraPaginaDiferente = ativo;
        return this;
    }

    /// <summary>Controla <c>w:evenAndOddHeaders</c>; sem chamada, ligado quando há 'even'.</summary>
    public DocxFixtureBuilder ParesImparesDiferentes(bool ativo)
    {
        _paresImparesDiferentes = ativo;
        return this;
    }

    public DocxFixtureBuilder Rodape(string texto, bool comCampoPagina = false, string kind = "default")
    {
        _footers.Add((kind, footer =>
        {
            var p = new Paragraph(new Run(new Text(texto) { Space = SpaceProcessingModeValues.Preserve }));
            if (comCampoPagina)
                p.AppendChild(new Run(new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }));
            footer.AppendChild(p);
        }
        ));
        return this;
    }

    public DocumentStructure Extrair()
    {
        using var stream = new MemoryStream(Gerar());
        return new DocxStructureExtractor().Extract(stream, "fixture.docx");
    }

    /// <summary>Grava o .docx em disco — para testes que precisam do arquivo, não só da estrutura.</summary>
    public string Salvar(string caminho)
    {
        File.WriteAllBytes(caminho, Gerar());
        return caminho;
    }

    private byte[] Gerar()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document();
            var body = main.Document.AppendChild(new Body());

            AdicionarEstilos(main);

            foreach (var bloco in _blocos) bloco(body);

            var sectPr = new SectionProperties(
                new PageSize { Width = 11906, Height = 16838 },
                new PageMargin { Top = 1417, Bottom = 1417, Left = 1701, Right = 1134 });

            foreach (var (kind, build) in _headers)
            {
                var part = main.AddNewPart<HeaderPart>();
                part.Header = new Header();
                build(part.Header);
                sectPr.PrependChild(new HeaderReference
                {
                    Id = main.GetIdOfPart(part),
                    Type = Tipo(kind)
                });
            }
            foreach (var (kind, build) in _footers)
            {
                var part = main.AddNewPart<FooterPart>();
                part.Footer = new Footer();
                build(part.Footer);
                sectPr.PrependChild(new FooterReference
                {
                    Id = main.GetIdOfPart(part),
                    Type = Tipo(kind)
                });
            }

            var tipos = _headers.Select(h => h.Kind).Concat(_footers.Select(f => f.Kind)).ToList();
            if (_primeiraPaginaDiferente ?? tipos.Contains("first"))
                sectPr.AppendChild(new TitlePage());
            if (_paresImparesDiferentes ?? tipos.Contains("even"))
            {
                var settings = main.AddNewPart<DocumentSettingsPart>();
                settings.Settings = new Settings(new EvenAndOddHeaders());
                settings.Settings.Save();
            }

            body.AppendChild(sectPr);
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static HeaderFooterValues Tipo(string kind) => kind switch
    {
        "first" => HeaderFooterValues.First,
        "even" => HeaderFooterValues.Even,
        _ => HeaderFooterValues.Default
    };

    private static TableCell Celula(string texto) =>
        new(new Paragraph(new Run(new Text(texto) { Space = SpaceProcessingModeValues.Preserve })));

    private static Paragraph MontarParagrafo(
        string texto, string? estilo, string? fonte, double? tamanho,
        string? alinhamento, int? nivelOutline)
    {
        var pPr = new ParagraphProperties();
        if (estilo is not null) pPr.AppendChild(new ParagraphStyleId { Val = estilo });
        if (nivelOutline is not null) pPr.AppendChild(new OutlineLevel { Val = nivelOutline.Value });
        if (alinhamento is not null)
            pPr.AppendChild(new Justification { Val = Alinhamento(alinhamento) });

        var rPr = new RunProperties();
        if (fonte is not null) rPr.AppendChild(new RunFonts { Ascii = fonte, HighAnsi = fonte });
        if (tamanho is not null)
            rPr.AppendChild(new FontSize { Val = ((int)(tamanho.Value * 2)).ToString() });

        var run = new Run(rPr, new Text(texto) { Space = SpaceProcessingModeValues.Preserve });
        return new Paragraph(pPr, run);
    }

    private static JustificationValues Alinhamento(string valor) => valor.ToLowerInvariant() switch
    {
        "center" => JustificationValues.Center,
        "right" => JustificationValues.Right,
        "both" => JustificationValues.Both,
        _ => JustificationValues.Left
    };

    /// <summary>Estilos Heading1..Heading4 com outlineLvl, como o Word os define.</summary>
    private static void AdicionarEstilos(MainDocumentPart main)
    {
        var part = main.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles();

        styles.AppendChild(new DocDefaults(
            new RunPropertiesDefault(new RunPropertiesBaseStyle(
                new RunFonts { Ascii = "Times New Roman" },
                new FontSize { Val = "24" }))));

        for (var i = 1; i <= 4; i++)
        {
            styles.AppendChild(new Style(
                new StyleName { Val = $"heading {i}" },
                new StyleParagraphProperties(new OutlineLevel { Val = i - 1 }))
            {
                StyleId = $"Heading{i}",
                Type = StyleValues.Paragraph
            });
        }

        styles.AppendChild(new Style(new StyleName { Val = "toc 1" })
        {
            StyleId = "TOC1",
            Type = StyleValues.Paragraph
        });

        part.Styles = styles;
        part.Styles.Save();
    }
}
