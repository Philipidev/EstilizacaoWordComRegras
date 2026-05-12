using FluentAssertions;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Validation;
using Xunit;

namespace WordComplianceValidator.Core.Tests.Validation;

public class RuleEngineTests
{
    private static RuleSet Sample(IReadOnlyDictionary<string, StyleRule>? styles = null,
                                  IReadOnlyList<HeaderRule>? headers = null,
                                  IReadOnlyList<FooterRule>? footers = null,
                                  IReadOnlyList<Rule>? rules = null) =>
        new(
            Cliente: "Cliente Teste",
            VersaoPadrao: "1.0",
            Styles: styles ?? new Dictionary<string, StyleRule>(),
            Headers: headers ?? Array.Empty<HeaderRule>(),
            Footers: footers ?? Array.Empty<FooterRule>(),
            Rules: rules ?? Array.Empty<Rule>());

    private static DocumentStructure EmptyDoc(
        IReadOnlyDictionary<string, ExtractedStyle>? styles = null,
        IReadOnlyList<ExtractedHeaderFooter>? headers = null,
        IReadOnlyList<ExtractedHeaderFooter>? footers = null,
        bool track = false, bool comments = false) =>
        new(
            Styles: styles ?? new Dictionary<string, ExtractedStyle>(),
            Headers: headers ?? Array.Empty<ExtractedHeaderFooter>(),
            Footers: footers ?? Array.Empty<ExtractedHeaderFooter>(),
            Sections: Array.Empty<ExtractedSection>(),
            Paragraphs: Array.Empty<ExtractedParagraph>(),
            TableCount: 0,
            HasPendingTrackChanges: track,
            HasOpenComments: comments);

    [Fact]
    public void Style_mismatch_yields_violation()
    {
        var rs = Sample(
            styles: new Dictionary<string, StyleRule>
            {
                ["titulo1"] = new("Calibri", 14, true, null, null)
            },
            rules: new[] { new Rule("R-STYLE", RuleType.Style, Severity.Warning, "Estilo fora do padrão.") });

        var doc = EmptyDoc(
            styles: new Dictionary<string, ExtractedStyle>(StringComparer.OrdinalIgnoreCase)
            {
                ["titulo1"] = new("titulo1", "Titulo 1", "Arial", 12, false, null, null)
            });

        var violations = new RuleEngine().Run(rs, doc);
        violations.Should().HaveCountGreaterThan(0);
        violations.Should().Contain(v => v.Message.Contains("fonte"));
    }

    [Fact]
    public void Required_header_missing_yields_error()
    {
        var rs = Sample(
            headers: new[] { new HeaderRule(true, new[] { "Cliente" }) },
            rules: new[] { new Rule("R001", RuleType.Header, Severity.Error, "Header obrigatório ausente.") });

        var violations = new RuleEngine().Run(rs, EmptyDoc());
        violations.Should().ContainSingle(v => v.RuleId == "R001" && v.Severity == Severity.Error);
    }

    [Fact]
    public void Header_missing_token_yields_warning()
    {
        var rs = Sample(headers: new[] { new HeaderRule(true, new[] { "Código Documento" }) });
        var doc = EmptyDoc(headers: new[] { new ExtractedHeaderFooter("default", "Nome do Cliente") });
        var violations = new RuleEngine().Run(rs, doc);
        violations.Should().Contain(v => v.Message.Contains("Código Documento"));
    }

    [Fact]
    public void Track_changes_pending_yields_governance_violation()
    {
        var rs = Sample(rules: new[] { new Rule("GOV1", RuleType.Governance, Severity.Error, "Sem track changes pendentes.") });
        var doc = EmptyDoc(track: true);
        var violations = new RuleEngine().Run(rs, doc);
        violations.Should().Contain(v => v.RuleId == "GOV1");
    }

    [Fact]
    public void Clean_document_against_empty_ruleset_has_no_violations()
    {
        var violations = new RuleEngine().Run(Sample(), EmptyDoc());
        violations.Should().BeEmpty();
    }
}
