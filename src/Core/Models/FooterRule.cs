namespace WordComplianceValidator.Core.Models;

public sealed record FooterRule(
    bool Required,
    IReadOnlyList<string> Contains);
