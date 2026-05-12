using FluentAssertions;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Profile;
using WordComplianceValidator.Infrastructure.Checks;
using Xunit;

namespace WordComplianceValidator.Infrastructure.Tests.Checks;

public class IndiceAtualizadoCheckTests
{
    private static DocumentContext Ctx(bool hasToc, params string[] paragraphs)
    {
        var paras = paragraphs.Select((t, i) => new ExtractedParagraph($"p{i}", null, t)).ToArray();
        var structure = new DocumentStructure(
            "f.docx",
            new Dictionary<string, ExtractedStyle>(),
            Array.Empty<ExtractedHeaderFooter>(),
            Array.Empty<ExtractedHeaderFooter>(),
            Array.Empty<ExtractedSection>(),
            paras,
            Array.Empty<ExtractedTable>(),
            HasPendingTrackChanges: false, HasOpenComments: false, HasUpdatedToc: hasToc);
        return new DocumentContext("f.docx", structure,
            new ClientProfile("X", "1.0", new Dictionary<string, string>()));
    }

    [Fact]
    public async Task Passes_when_toc_present_and_no_error_marks()
    {
        var r = await new IndiceAtualizadoCheck().RunAsync(Ctx(hasToc: true, "1. Introdução", "Conteúdo"));
        r.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task Fails_when_toc_has_error_marks()
    {
        var r = await new IndiceAtualizadoCheck().RunAsync(
            Ctx(hasToc: true, "Sumário", "Erro! Indicador não definido."));
        r.Status.Should().Be(CheckStatus.Failed);
    }

    [Fact]
    public async Task Warns_when_toc_absent()
    {
        var r = await new IndiceAtualizadoCheck().RunAsync(Ctx(hasToc: false, "Conteúdo"));
        r.Status.Should().Be(CheckStatus.Skipped);
        r.Violations.Should().HaveCount(1);
        r.Violations[0].Severity.Should().Be(Severity.Warning);
    }
}
