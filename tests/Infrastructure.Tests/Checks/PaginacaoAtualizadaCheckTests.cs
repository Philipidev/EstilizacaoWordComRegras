using FluentAssertions;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Profile;
using WordComplianceValidator.Infrastructure.Checks;
using Xunit;

namespace WordComplianceValidator.Infrastructure.Tests.Checks;

public class PaginacaoAtualizadaCheckTests
{
    private static DocumentContext Ctx(string? footer, string? header = null, string[]? fields = null)
    {
        var footers = footer is null ? Array.Empty<ExtractedHeaderFooter>()
                                     : new[] { new ExtractedHeaderFooter("default", footer, 0, fields) };
        var headers = header is null ? Array.Empty<ExtractedHeaderFooter>()
                                     : new[] { new ExtractedHeaderFooter("default", header, 0, fields) };
        var structure = new DocumentStructure(
            "f.docx",
            new Dictionary<string, ExtractedStyle>(),
            headers, footers,
            Array.Empty<ExtractedSection>(),
            Array.Empty<ExtractedParagraph>(),
            Array.Empty<ExtractedTable>(),
            false, false, false);
        return new DocumentContext("f.docx", structure,
            new ClientProfile("X", "1.0", new Dictionary<string, string>()));
    }

    [Fact]
    public async Task Passes_when_footer_has_page_word()
    {
        var r = await new PaginacaoAtualizadaCheck().RunAsync(Ctx(footer: "Página 1 de 10"));
        r.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task Passes_when_header_has_page_number()
    {
        var r = await new PaginacaoAtualizadaCheck().RunAsync(Ctx(footer: "logo", header: "Página 3"));
        r.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task Skipped_when_no_numbering_found()
    {
        var r = await new PaginacaoAtualizadaCheck().RunAsync(Ctx(footer: "Apenas texto"));
        r.Status.Should().Be(CheckStatus.Skipped);
    }

    [Fact]
    public async Task Skipped_when_no_footer()
    {
        var r = await new PaginacaoAtualizadaCheck().RunAsync(Ctx(footer: null));
        r.Status.Should().Be(CheckStatus.Skipped);
    }

    [Fact]
    public async Task Passes_when_footer_has_PAGE_field_even_without_text()
    {
        var r = await new PaginacaoAtualizadaCheck().RunAsync(
            Ctx(footer: " ", fields: new[] { "PAGE", "NUMPAGES" }));
        r.Status.Should().Be(CheckStatus.Passed);
    }
}
