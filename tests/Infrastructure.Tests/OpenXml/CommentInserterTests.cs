using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using AwesomeAssertions;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Infrastructure.OpenXml;
using WordComplianceValidator.Infrastructure.Tests.Fixtures;

namespace WordComplianceValidator.Infrastructure.Tests.OpenXml;

public sealed class CommentInserterTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("comment-inserter-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_pasta, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// Documentos sem <c>w14:paraId</c> (python-docx, LibreOffice, Google Docs, Word 2007)
    /// derrubavam a revisão inteira: o mapa de parágrafos usava o paraId como chave e todos
    /// colidiam na string vazia.
    /// </summary>
    [Fact]
    public void Documento_sem_paraId_recebe_o_comentario_no_paragrafo_certo()
    {
        var origem = DocxFixtureBuilder.Novo()
            .Paragrafo("Primeiro parágrafo.")
            .Paragrafo("Parágrafo com o defeito.")
            .Paragrafo("Terceiro parágrafo.")
            .Salvar(Path.Combine(_pasta, "sem-paraid.docx"));

        var estrutura = new DocxStructureExtractor().ExtractFromFile(origem);
        var alvo = estrutura.Paragraphs.Single(p => p.Text == "Parágrafo com o defeito.");
        var saida = Path.Combine(_pasta, "saida.docx");

        new CommentInserter().InsertComments(origem, saida,
        [
            new Violation("PS-002:4.2:Cliente", Severity.Error, "Erro de teste.",
                new ViolationLocation(alvo.ParagraphId, null, null))
        ]);

        using var doc = WordprocessingDocument.Open(saida, false);
        var comentado = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Single(p => p.Descendants<CommentReference>().Any());
        comentado.InnerText.Should().Be("Parágrafo com o defeito.");
    }

    /// <summary>
    /// Visto ao abrir a cópia no Word de verdade: com w:updateFields gravado, o Word perguntava
    /// "Deseja atualizar os campos deste documento?" antes de mostrar qualquer coisa. E o painel
    /// do CLI dizia "14 comentários" quando o Word tinha 10 — achados iguais viram um só.
    /// </summary>
    [Fact]
    public void Copia_nao_pede_atualizacao_de_campos_e_a_contagem_e_a_dos_comentarios_gravados()
    {
        var origem = DocxFixtureBuilder.Novo()
            .Paragrafo("Parágrafo com o defeito.")
            .Salvar(Path.Combine(_pasta, "contagem.docx"));
        var alvo = new DocxStructureExtractor().ExtractFromFile(origem).Paragraphs[0].ParagraphId;
        var saida = Path.Combine(_pasta, "saida.docx");

        var inseridos = new CommentInserter().InsertComments(origem, saida,
        [
            new Violation("PS-002:4.3.3 (letra f):Pda", Severity.Warning, "Mesmo achado.", new ViolationLocation(alvo, null, null)),
            new Violation("PS-002:4.3.3 (letra f):Cliente", Severity.Warning, "Mesmo achado.", new ViolationLocation(alvo, null, null)),
            new Violation("PS-002:4.2:Cliente", Severity.Error, "Outro achado.", new ViolationLocation(alvo, null, null))
        ]);

        using var doc = WordprocessingDocument.Open(saida, false);
        inseridos.Should().Be(2);
        doc.MainDocumentPart!.WordprocessingCommentsPart!.Comments!.Elements<Comment>().Should().HaveCount(2);
        doc.MainDocumentPart.DocumentSettingsPart?.Settings?.GetFirstChild<UpdateFieldsOnOpen>().Should().BeNull();
    }

    [Fact]
    public void ParaId_repetido_nao_derruba_a_insercao()
    {
        var origem = DocxFixtureBuilder.Novo()
            .Paragrafo("Um.")
            .Paragrafo("Dois.")
            .Salvar(Path.Combine(_pasta, "repetido.docx"));

        using (var doc = WordprocessingDocument.Open(origem, true))
        {
            foreach (var p in doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>())
                p.ParagraphId = "248735F3";
            doc.MainDocumentPart.Document.Save();
        }

        var estrutura = new DocxStructureExtractor().ExtractFromFile(origem);
        estrutura.Paragraphs.Select(p => p.ParagraphId).Should().OnlyHaveUniqueItems();

        var act = () => new CommentInserter().InsertComments(origem, Path.Combine(_pasta, "saida.docx"),
        [
            new Violation("R", Severity.Warning, "Aviso.",
                new ViolationLocation(estrutura.Paragraphs[1].ParagraphId, null, null))
        ]);
        act.Should().NotThrow();
    }

    /// <summary>
    /// O início do comentário ia antes do <c>w:pPr</c>, e o <c>w:updateFields</c> depois de
    /// elementos que o schema exige depois dele: XML que o Word pode recusar ou "reparar".
    /// </summary>
    [Fact]
    public void Saida_respeita_o_schema_do_Word()
    {
        var origem = DocxFixtureBuilder.Novo()
            .Paragrafo("Parágrafo alinhado.", alinhamento: "both")
            .Salvar(Path.Combine(_pasta, "schema.docx"));

        using (var doc = WordprocessingDocument.Open(origem, true))
        {
            var settings = doc.MainDocumentPart!.AddNewPart<DocumentSettingsPart>();
            settings.Settings = new Settings(new Compatibility(), new ThemeFontLanguages { Val = "pt-BR" });
            settings.Settings.Save();
        }

        var estrutura = new DocxStructureExtractor().ExtractFromFile(origem);
        var saida = Path.Combine(_pasta, "saida.docx");
        new CommentInserter().InsertComments(origem, saida,
        [
            new Violation("R", Severity.Error, "Erro.",
                new ViolationLocation(estrutura.Paragraphs[0].ParagraphId, null, null))
        ]);

        using var resultado = WordprocessingDocument.Open(saida, false);
        var erros = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(resultado, TestContext.Current.CancellationToken)
            .Select(e => $"{e.Path?.XPath}: {e.Description}")
            .ToList();
        erros.Should().BeEmpty();
    }
}
