using FluentAssertions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Profile;
using WordComplianceValidator.Infrastructure.Checks;
using Xunit;

namespace WordComplianceValidator.Infrastructure.Tests.Checks;

public class ConsistenciaIniciaisCheckTests
{
    private static DocumentContext Ctx(string folhaRostoTexto, string elab, string verif, string aprov)
    {
        var cells = new List<ExtractedTableCell>
        {
            new(0, 0, "Características do Documento"),
            new(1, 0, "Elaborado por"),  new(1, 1, elab),
            new(2, 0, "Verificado por"), new(2, 1, verif),
            new(3, 0, "Aprovado por"),   new(3, 1, aprov),
        };
        var table = new ExtractedTable(0, cells, "Características do Documento");
        var paragraphs = new List<ExtractedParagraph>
        {
            new("p1", null, folhaRostoTexto),
        };
        var structure = new DocumentStructure(
            "f.docx",
            new Dictionary<string, ExtractedStyle>(),
            Array.Empty<ExtractedHeaderFooter>(),
            Array.Empty<ExtractedHeaderFooter>(),
            Array.Empty<ExtractedSection>(),
            paragraphs,
            new[] { table },
            false, false, false);
        var profile = new ClientProfile("X", "1.0", new Dictionary<string, string>());
        return new DocumentContext("f.docx", structure, profile);
    }

    [Fact]
    public async Task Passes_when_initials_match_folha_rosto()
    {
        var ctx = Ctx("Elaborado por JAS, verificado por MBC, aprovado por XYZ",
            elab: "João A. (JAS)", verif: "Maria B. (MBC)", aprov: "Sr. X (XYZ)");
        var result = await new ConsistenciaIniciaisCheck().RunAsync(ctx);
        result.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task Fails_when_initials_diverge()
    {
        var ctx = Ctx("Folha de rosto sem indicação correta: AAA BBB",
            elab: "João A. (JAS)", verif: "Maria B. (MBC)", aprov: "Sr. X (XYZ)");
        var result = await new ConsistenciaIniciaisCheck().RunAsync(ctx);
        result.Status.Should().Be(CheckStatus.Failed);
        result.Violations.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task Pda_variant_uses_pda_ref()
    {
        var check = new ConsistenciaIniciaisCheck(ChecklistPadrao.Pda);
        check.Ref.Padrao.Should().Be(ChecklistPadrao.Pda);
        check.Ref.Item.Should().Be("4.3.3 (letra d)");
    }
}
