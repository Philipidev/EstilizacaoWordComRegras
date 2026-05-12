using System.Text.Json;

namespace WordComplianceValidator.Core.Models;

public sealed record Rule(
    string Id,
    RuleType Type,
    Severity Severity,
    string Message,
    JsonElement? Parameters = null);
