using FluentAssertions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Profile;
using Xunit;

namespace WordComplianceValidator.Core.Tests.Checks;

public class ChecklistEngineTests
{
    private static DocumentContext EmptyContext() => new(
        sourcePath: "x.docx",
        structure: new DocumentStructure(
            FileName: "x.docx",
            Styles: new Dictionary<string, ExtractedStyle>(),
            Headers: Array.Empty<ExtractedHeaderFooter>(),
            Footers: Array.Empty<ExtractedHeaderFooter>(),
            Sections: Array.Empty<ExtractedSection>(),
            Paragraphs: Array.Empty<ExtractedParagraph>(),
            Tables: Array.Empty<ExtractedTable>(),
            HasPendingTrackChanges: false,
            HasOpenComments: false,
            HasUpdatedToc: false),
        profile: new ClientProfile("X", "1.0", new Dictionary<string, string>()));

    private static ChecklistEntry Entry(string ps, string item, ChecklistPadrao pad, bool iaAuto) =>
        new(new ChecklistRef(ps, item, pad), 1, "Proc", "Assunto", "Titulo", "Descr", iaAuto, null);

    [Fact]
    public async Task NonAutomatable_entries_are_skipped()
    {
        var engine = new ChecklistEngine(Array.Empty<IRuleCheck>());
        var catalog = new[] { Entry("PS-002", "4.2", ChecklistPadrao.Cliente, iaAuto: false) };
        var results = await engine.RunAsync(EmptyContext(), catalog);
        results.Should().HaveCount(1);
        results[0].Status.Should().Be(CheckStatus.Skipped);
        results[0].Note.Should().Contain("manual");
    }

    [Fact]
    public async Task Automatable_without_implementation_is_skipped_with_note()
    {
        var engine = new ChecklistEngine(Array.Empty<IRuleCheck>());
        var catalog = new[] { Entry("PS-002", "4.1", ChecklistPadrao.Cliente, iaAuto: true) };
        var results = await engine.RunAsync(EmptyContext(), catalog);
        results[0].Status.Should().Be(CheckStatus.Skipped);
        results[0].Note.Should().Contain("não implementado");
    }

    [Fact]
    public async Task Check_runs_for_matching_ref()
    {
        var check = new FakeCheck(new ChecklistRef("PS-002", "4.1", ChecklistPadrao.Cliente),
                                  CheckStatus.Passed);
        var engine = new ChecklistEngine(new[] { check });
        var catalog = new[] { Entry("PS-002", "4.1", ChecklistPadrao.Cliente, iaAuto: true) };
        var results = await engine.RunAsync(EmptyContext(), catalog);
        results[0].Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task Check_exception_is_captured_as_error_status()
    {
        var check = new FakeCheck(new ChecklistRef("PS-002", "4.1", ChecklistPadrao.Cliente),
                                  throws: new InvalidOperationException("boom"));
        var engine = new ChecklistEngine(new[] { check });
        var catalog = new[] { Entry("PS-002", "4.1", ChecklistPadrao.Cliente, iaAuto: true) };
        var results = await engine.RunAsync(EmptyContext(), catalog);
        results[0].Status.Should().Be(CheckStatus.Error);
        results[0].Note.Should().Contain("boom");
    }

    private sealed class FakeCheck : IRuleCheck
    {
        private readonly CheckStatus _status;
        private readonly Exception? _ex;
        public ChecklistRef Ref { get; }
        public FakeCheck(ChecklistRef refId, CheckStatus status) { Ref = refId; _status = status; _ex = null; }
        public FakeCheck(ChecklistRef refId, Exception throws) { Ref = refId; _status = CheckStatus.Error; _ex = throws; }
        public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken ct = default)
        {
            if (_ex is not null) throw _ex;
            return Task.FromResult(new RuleCheckResult(Ref, _status, Array.Empty<Violation>()));
        }
    }
}
