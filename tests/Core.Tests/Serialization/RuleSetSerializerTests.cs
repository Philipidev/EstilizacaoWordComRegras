using FluentAssertions;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Serialization;
using Xunit;

namespace WordComplianceValidator.Core.Tests.Serialization;

public class RuleSetSerializerTests
{
    [Fact]
    public void Roundtrip_preserves_data()
    {
        var rs = new RuleSet(
            Cliente: "Cliente X",
            VersaoPadrao: "1.0",
            Styles: new Dictionary<string, StyleRule>
            {
                ["titulo1"] = new("Calibri", 14, true, null, null)
            },
            Headers: new[] { new HeaderRule(true, new[] { "Nome Cliente", "Código Documento" }) },
            Footers: Array.Empty<FooterRule>(),
            Rules: new[] { new Rule("R001", RuleType.Header, Severity.Error, "Header obrigatório ausente.") });

        var json = RuleSetSerializer.Serialize(rs);
        json.Should().Contain("\"cliente\": \"Cliente X\"");
        json.Should().Contain("\"severity\": \"error\"");

        var rt = RuleSetSerializer.Deserialize(json);
        rt.Cliente.Should().Be("Cliente X");
        rt.Styles["titulo1"].Font.Should().Be("Calibri");
        rt.Rules.Single().Severity.Should().Be(Severity.Error);
    }
}
