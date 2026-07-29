namespace WordComplianceValidator.Infrastructure.OpenAI;

public sealed class OpenAiSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-5.6-sol";
}
