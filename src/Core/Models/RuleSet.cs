namespace WordComplianceValidator.Core.Models;

public sealed record RuleSet(
    string Cliente,
    string VersaoPadrao,
    IReadOnlyDictionary<string, StyleRule> Styles,
    IReadOnlyList<HeaderRule> Headers,
    IReadOnlyList<FooterRule> Footers,
    IReadOnlyList<Rule> Rules);
