using FluentAssertions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Profile;
using WordComplianceValidator.Infrastructure.Checks;
using Xunit;

namespace WordComplianceValidator.Infrastructure.Tests.Checks;

public class IniciaisDistintasCheckTests
{
    private static DocumentContext CtxWithCharacteristicsTable(string elab, string verif)
    {
        var cells = new List<ExtractedTableCell>
        {
            new(0, 0, "Características do Documento"),
            new(1, 0, "Elaborado por"),  new(1, 1, elab),
            new(2, 0, "Verificado por"), new(2, 1, verif),
        };
        var table = new ExtractedTable(0, cells, "Características do Documento");
        var structure = new DocumentStructure(
            FileName: "abc.docx",
            Styles: new Dictionary<string, ExtractedStyle>(),
            Headers: Array.Empty<ExtractedHeaderFooter>(),
            Footers: Array.Empty<ExtractedHeaderFooter>(),
            Sections: Array.Empty<ExtractedSection>(),
            Paragraphs: Array.Empty<ExtractedParagraph>(),
            Tables: new[] { table },
            HasPendingTrackChanges: false,
            HasOpenComments: false,
            HasUpdatedToc: false);
        var profile = new ClientProfile("X", "1.0", new Dictionary<string, string>());
        return new DocumentContext("abc.docx", structure, profile);
    }

    [Fact]
    public async Task Passes_when_initials_differ()
    {
        var result = await new IniciaisDistintasCheck().RunAsync(
            CtxWithCharacteristicsTable("João A. (JAS)", "Maria B. (MBC)"));
        result.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task Fails_when_initials_match()
    {
        var result = await new IniciaisDistintasCheck().RunAsync(
            CtxWithCharacteristicsTable("João A. (JAS)", "João Outro (JAS)"));
        result.Status.Should().Be(CheckStatus.Failed);
        result.Violations.Should().Contain(v => v.Severity == Severity.Error);
    }

    [Fact]
    public async Task Pda_variant_uses_pda_ref_and_fails_on_match()
    {
        var check = new IniciaisDistintasCheck(ChecklistPadrao.Pda);
        check.Ref.Padrao.Should().Be(ChecklistPadrao.Pda);
        check.Ref.Item.Should().Be("4.3.3 (letra c)");

        var result = await check.RunAsync(
            CtxWithCharacteristicsTable("João A. (JAS)", "Outro (JAS)"));
        result.Status.Should().Be(CheckStatus.Failed);
        result.Ref.Padrao.Should().Be(ChecklistPadrao.Pda);
    }

    [Fact]
    public async Task Pda_variant_passes_when_initials_differ()
    {
        var check = new IniciaisDistintasCheck(ChecklistPadrao.Pda);
        var result = await check.RunAsync(
            CtxWithCharacteristicsTable("João A. (JAS)", "Maria B. (MBC)"));
        result.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task Skips_when_quadro_absent()
    {
        var structure = new DocumentStructure(
            FileName: "abc.docx",
            Styles: new Dictionary<string, ExtractedStyle>(),
            Headers: Array.Empty<ExtractedHeaderFooter>(),
            Footers: Array.Empty<ExtractedHeaderFooter>(),
            Sections: Array.Empty<ExtractedSection>(),
            Paragraphs: Array.Empty<ExtractedParagraph>(),
            Tables: Array.Empty<ExtractedTable>(),
            HasPendingTrackChanges: false, HasOpenComments: false, HasUpdatedToc: false);
        var ctx = new DocumentContext("abc.docx", structure,
            new ClientProfile("X", "1.0", new Dictionary<string, string>()));
        var result = await new IniciaisDistintasCheck().RunAsync(ctx);
        result.Status.Should().Be(CheckStatus.Skipped);
    }
}
