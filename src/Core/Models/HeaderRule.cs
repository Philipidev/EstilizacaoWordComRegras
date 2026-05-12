namespace WordComplianceValidator.Core.Models;

public sealed record HeaderRule(
    bool Required,
    IReadOnlyList<string> Contains);
