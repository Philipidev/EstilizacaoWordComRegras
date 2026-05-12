using FluentAssertions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Profile;
using WordComplianceValidator.Infrastructure.Checks;
using Xunit;

namespace WordComplianceValidator.Infrastructure.Tests.Checks;

public class QuadroCaracteristicasPreenchidoCheckTests
{
    private static DocumentContext Ctx(IEnumerable<(string Label, string Value)> rows, string? camposObrigatorios = null)
    {
        var cells = new List<ExtractedTableCell> { new(0, 0, "Características do Documento") };
        int r = 1;
        foreach (var (label, value) in rows)
        {
            cells.Add(new ExtractedTableCell(r, 0, label));
            cells.Add(new ExtractedTableCell(r, 1, value));
            r++;
        }
        var table = new ExtractedTable(0, cells, "Características do Documento");
        var structure = new DocumentStructure(
            FileName: "f.docx",
            Styles: new Dictionary<string, ExtractedStyle>(),
            Headers: Array.Empty<ExtractedHeaderFooter>(),
            Footers: Array.Empty<ExtractedHeaderFooter>(),
            Sections: Array.Empty<ExtractedSection>(),
            Paragraphs: Array.Empty<ExtractedParagraph>(),
            Tables: new[] { table },
            HasPendingTrackChanges: false, HasOpenComments: false, HasUpdatedToc: false);
        var parameters = new Dictionary<string, string>();
        if (camposObrigatorios is not null) parameters["quadro.camposObrigatorios"] = camposObrigatorios;
        var profile = new ClientProfile("X", "1.0", parameters);
        return new DocumentContext("f.docx", structure, profile);
    }

    [Fact]
    public async Task Passes_when_all_required_fields_have_values()
    {
        var ctx = Ctx(new[]
        {
            ("Codificação", "ABC-001"),
            ("Título",      "Doc X"),
            ("Revisão",     "00"),
            ("Data",        "01/2026"),
            ("Elaborado por", "Fulano (FUL)"),
            ("Verificado por", "Beltrano (BEL)"),
            ("Aprovado por", "Ciclano (CIC)"),
        });
        var result = await new QuadroCaracteristicasPreenchidoCheck().RunAsync(ctx);
        result.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task Fails_when_required_field_is_empty()
    {
        var ctx = Ctx(new[]
        {
            ("Codificação", "ABC-001"),
            ("Título",      ""),
            ("Revisão",     "00"),
        }, camposObrigatorios: "Codificação|Título|Revisão");
        var result = await new QuadroCaracteristicasPreenchidoCheck().RunAsync(ctx);
        result.Status.Should().Be(CheckStatus.Failed);
        result.Violations.Should().Contain(v => v.Message.Contains("Título"));
    }

    [Fact]
    public async Task Fails_when_quadro_absent()
    {
        var structure = new DocumentStructure(
            "f.docx",
            new Dictionary<string, ExtractedStyle>(),
            Array.Empty<ExtractedHeaderFooter>(),
            Array.Empty<ExtractedHeaderFooter>(),
            Array.Empty<ExtractedSection>(),
            Array.Empty<ExtractedParagraph>(),
            Array.Empty<ExtractedTable>(),
            false, false, false);
        var ctx = new DocumentContext("f.docx", structure,
            new ClientProfile("X", "1.0", new Dictionary<string, string>()));
        var result = await new QuadroCaracteristicasPreenchidoCheck().RunAsync(ctx);
        result.Status.Should().Be(CheckStatus.Failed);
    }
}
