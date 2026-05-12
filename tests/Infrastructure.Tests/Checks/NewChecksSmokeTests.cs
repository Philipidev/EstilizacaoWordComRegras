using FluentAssertions;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Profile;
using WordComplianceValidator.Infrastructure.Checks;
using Xunit;

namespace WordComplianceValidator.Infrastructure.Tests.Checks;

public class NewChecksSmokeTests
{
    private static DocumentStructure Build(
        IEnumerable<ExtractedParagraph>? paragraphs = null,
        IEnumerable<ExtractedTable>? tables = null,
        IEnumerable<ExtractedHeaderFooter>? headers = null,
        IEnumerable<ExtractedHeaderFooter>? footers = null,
        string fileName = "f.docx") =>
        new(fileName,
            new Dictionary<string, ExtractedStyle>(),
            (headers ?? Array.Empty<ExtractedHeaderFooter>()).ToList(),
            (footers ?? Array.Empty<ExtractedHeaderFooter>()).ToList(),
            Array.Empty<ExtractedSection>(),
            (paragraphs ?? Array.Empty<ExtractedParagraph>()).ToList(),
            (tables ?? Array.Empty<ExtractedTable>()).ToList(),
            false, false, false);

    private static DocumentContext Ctx(DocumentStructure s, Dictionary<string, string>? p = null) =>
        new("f.docx", s, new ClientProfile("X", "1.0", p ?? new Dictionary<string, string>()));

    private static ExtractedTable Quadro(params (string label, string value)[] rows)
    {
        var cells = new List<ExtractedTableCell> { new(0, 0, "Características do Documento") };
        int r = 1;
        foreach (var (l, v) in rows)
        {
            cells.Add(new ExtractedTableCell(r, 0, l));
            cells.Add(new ExtractedTableCell(r, 1, v));
            r++;
        }
        return new ExtractedTable(0, cells, "Características do Documento");
    }

    // --- LocalizacaoCodificacaoCheck (PS-018 4.3.2 a) ---
    [Fact]
    public async Task LocalizacaoCodificacao_passes_when_present_everywhere()
    {
        var profile = new Dictionary<string, string>
        {
            ["codificacao.pdaRegex"] = @"^[A-Z]{2}\d{1,3}-PDA-\d{2}-\d{2}-\d{3}-[A-Z]{2}$"
        };
        var quadro = Quadro(("Codificação", "QC5-PDA-26-04-022-RT-00"));
        var paragraphs = new[]
        {
            new ExtractedParagraph("p1", null, "QC5-PDA-26-04-022-RT")
        };
        var ctx = Ctx(Build(paragraphs: paragraphs, tables: new[] { quadro },
                            fileName: "QC5-PDA-26-04-022-RT-00.docx"), profile);
        var r = await new LocalizacaoCodificacaoCheck().RunAsync(ctx);
        r.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task LocalizacaoCodificacao_fails_when_diverges()
    {
        var profile = new Dictionary<string, string>
        {
            ["codificacao.pdaRegex"] = @"^[A-Z]{2}\d{1,3}-PDA-\d{2}-\d{2}-\d{3}-[A-Z]{2}$"
        };
        var quadro = Quadro(("Codificação", "QC5-PDA-26-04-022-RT-00"));
        var paragraphs = new[]
        {
            new ExtractedParagraph("p1", null, "XX9-PDA-99-99-999-RT")
        };
        var ctx = Ctx(Build(paragraphs: paragraphs, tables: new[] { quadro }), profile);
        var r = await new LocalizacaoCodificacaoCheck().RunAsync(ctx);
        r.Status.Should().Be(CheckStatus.Failed);
    }

    // --- CodificacaoClienteCheck (PS-018 4.7) ---
    [Fact]
    public async Task CodificacaoCliente_passes_when_code_in_quadro()
    {
        var profile = new Dictionary<string, string>
        {
            ["codificacao.clienteRegex"] = @"^MRN-[A-Z]{2}-\d{3}$"
        };
        var quadro = Quadro(("Cliente", "MRN-AB-001"));
        var ctx = Ctx(Build(tables: new[] { quadro }), profile);
        var r = await new CodificacaoClienteCheck().RunAsync(ctx);
        r.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task CodificacaoCliente_fails_when_code_missing()
    {
        var profile = new Dictionary<string, string>
        {
            ["codificacao.clienteRegex"] = @"^MRN-[A-Z]{2}-\d{3}$"
        };
        var quadro = Quadro(("Cliente", "outro"));
        var ctx = Ctx(Build(tables: new[] { quadro }), profile);
        var r = await new CodificacaoClienteCheck().RunAsync(ctx);
        r.Status.Should().Be(CheckStatus.Failed);
    }

    // --- EvolucaoDocumentoCheck (PS-018 4.8) ---
    [Theory]
    [InlineData("00")]
    [InlineData("0A")]
    [InlineData("01")]
    [InlineData("99")]
    public async Task Evolucao_accepts_valid_revisions(string rev)
    {
        var quadro = Quadro(("Revisão", rev));
        var ctx = Ctx(Build(tables: new[] { quadro }));
        var r = await new EvolucaoDocumentoCheck().RunAsync(ctx);
        r.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task Evolucao_fails_when_revision_invalid()
    {
        var quadro = Quadro(("Revisão", "X"));
        var ctx = Ctx(Build(tables: new[] { quadro }));
        var r = await new EvolucaoDocumentoCheck().RunAsync(ctx);
        r.Status.Should().Be(CheckStatus.Failed);
    }

    // --- CabecalhosPadronizadosCheck (PS-002 4.3.3 g) ---
    [Fact]
    public async Task Cabecalhos_pass_when_uniform()
    {
        var headers = new[]
        {
            new ExtractedHeaderFooter("default", "Empresa - Projeto X", 2),
            new ExtractedHeaderFooter("default", "Empresa - Projeto X", 2),
        };
        var ctx = Ctx(Build(headers: headers));
        var r = await new CabecalhosPadronizadosCheck().RunAsync(ctx);
        r.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task Cabecalhos_fail_when_diverge()
    {
        var headers = new[]
        {
            new ExtractedHeaderFooter("default", "Empresa - Projeto X", 2),
            new ExtractedHeaderFooter("default", "Empresa - Projeto Y", 1),
        };
        var ctx = Ctx(Build(headers: headers));
        var r = await new CabecalhosPadronizadosCheck().RunAsync(ctx);
        r.Status.Should().Be(CheckStatus.Failed);
    }

    // --- ReferenciasCruzadasCheck (PS-002 4.3.3 h) ---
    [Fact]
    public async Task RefCruzadas_pass_when_no_error_marks()
    {
        var paragraphs = new[] { new ExtractedParagraph("p1", null, "Ver Figura 1.") };
        var r = await new ReferenciasCruzadasCheck().RunAsync(Ctx(Build(paragraphs: paragraphs)));
        r.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task RefCruzadas_fail_when_error_mark_present()
    {
        var paragraphs = new[]
        {
            new ExtractedParagraph("p1", null, "Ver Erro! Indicador não definido. para detalhes.")
        };
        var r = await new ReferenciasCruzadasCheck().RunAsync(Ctx(Build(paragraphs: paragraphs)));
        r.Status.Should().Be(CheckStatus.Failed);
    }

    // --- CoerenciaRevisoesCheck (PS-002 4.3.3 i) ---
    [Fact]
    public async Task CoerenciaRevisoes_pass_when_revision_appears_in_folha()
    {
        var quadro = Quadro(("Revisão", "01"));
        var paragraphs = new[] { new ExtractedParagraph("p1", null, "Documento Rev. 01 - emitido") };
        var r = await new CoerenciaRevisoesCheck().RunAsync(
            Ctx(Build(paragraphs: paragraphs, tables: new[] { quadro })));
        r.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task CoerenciaRevisoes_fail_when_diverge()
    {
        var quadro = Quadro(("Revisão", "05"));
        var paragraphs = new[] { new ExtractedParagraph("p1", null, "Documento Rev. 01") };
        var r = await new CoerenciaRevisoesCheck().RunAsync(
            Ctx(Build(paragraphs: paragraphs, tables: new[] { quadro })));
        r.Status.Should().Be(CheckStatus.Failed);
    }

    // --- FolhaRostoCheck (PS-002 4.3.1 PdA) ---
    [Fact]
    public async Task FolhaRosto_pass_when_all_fields_present()
    {
        var profile = new Dictionary<string, string>
        {
            ["folhaRosto.contratante"] = "Mineradora ABC",
            ["folhaRosto.titulo"] = "Relatório Técnico",
            ["codificacao.pdaRegex"] = @"^[A-Z]{2}\d{1,3}-PDA-\d{2}-\d{2}-\d{3}-[A-Z]{2}$"
        };
        var paragraphs = new[]
        {
            new ExtractedParagraph("p1", null, "Mineradora ABC - Projeto Vale Verde"),
            new ExtractedParagraph("p2", null, "Relatório Técnico - Análise Estrutural"),
            new ExtractedParagraph("p3", null, "março de 2026"),
            new ExtractedParagraph("p4", null, "QC5-PDA-26-04-022-RT")
        };
        var r = await new FolhaRostoCheck().RunAsync(Ctx(Build(paragraphs: paragraphs), profile));
        r.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task FolhaRosto_fail_when_codificacao_missing()
    {
        var profile = new Dictionary<string, string>
        {
            ["codificacao.pdaRegex"] = @"^[A-Z]{2}\d{1,3}-PDA-\d{2}-\d{2}-\d{3}-[A-Z]{2}$"
        };
        var paragraphs = new[]
        {
            new ExtractedParagraph("p1", null, "Documento sem código"),
            new ExtractedParagraph("p2", null, "março/2026")
        };
        var r = await new FolhaRostoCheck().RunAsync(Ctx(Build(paragraphs: paragraphs), profile));
        r.Status.Should().Be(CheckStatus.Failed);
    }
}
