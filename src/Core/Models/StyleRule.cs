namespace WordComplianceValidator.Core.Models;

public sealed record StyleRule(
    string? Font,
    double? Size,
    bool? Bold,
    bool? Italic,
    string? Alignment);
