using System.ClientModel;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;
using WordComplianceValidator.Core.Checks;

namespace WordComplianceValidator.Infrastructure.OpenAI;

public sealed class OpenAiSemanticChecker : ISemanticChecker
{
    private readonly OpenAiSettings _settings;

    public OpenAiSemanticChecker(OpenAiSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException(
                "OpenAI API key não configurada. Defina OPENAI_API_KEY ou OpenAI:ApiKey em appsettings.");
        _settings = settings;
    }

    public async Task<SemanticVerdict> EvaluateAsync(
        string instrucao, string conteudo, CancellationToken cancellationToken = default)
    {
        var client = new ChatClient(_settings.Model, new ApiKeyCredential(_settings.ApiKey));

        var schema = BinaryData.FromString("""
        {
          "type":"object",
          "additionalProperties":false,
          "required":["conforme","justificativa"],
          "properties":{
            "conforme":{"type":"boolean"},
            "justificativa":{"type":"string"}
          }
        }
        """);

        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "verdict",
                jsonSchema: schema,
                jsonSchemaFormatDescription: "Veredito booleano com justificativa.",
                jsonSchemaIsStrict: true)
        };

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                "Você avalia conformidade documental. Responda EXCLUSIVAMENTE com JSON " +
                "no schema fornecido. 'conforme'=true se o conteúdo atende à instrução; " +
                "'justificativa' deve ser curta (1-2 frases) em PT-BR."),
            new UserChatMessage(
                $"Instrução de verificação:\n{instrucao}\n\nConteúdo a avaliar:\n{conteudo}")
        };

        var response = await client.CompleteChatAsync(messages, options, cancellationToken);
        var text = response.Value.Content.Count > 0 ? response.Value.Content[0].Text : "{}";

        using var doc = JsonDocument.Parse(text);
        var conforme = doc.RootElement.GetProperty("conforme").GetBoolean();
        var justif = doc.RootElement.GetProperty("justificativa").GetString() ?? string.Empty;
        return new SemanticVerdict(conforme, justif);
    }
}
