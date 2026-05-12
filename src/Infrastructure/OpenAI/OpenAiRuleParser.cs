using System.ClientModel;
using System.Text;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Rules;
using WordComplianceValidator.Core.Serialization;

namespace WordComplianceValidator.Infrastructure.OpenAI;

public sealed class OpenAiRuleParser : IRuleParser
{
    private readonly OpenAiSettings _settings;

    public OpenAiRuleParser(OpenAiSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException(
                "OpenAI API key não configurada. Defina OPENAI_API_KEY ou OpenAI:ApiKey em appsettings.");
        _settings = settings;
    }

    public async Task<RuleSet> ParseAsync(
        string clientName,
        string versionLabel,
        string rulesText,
        DocumentStructure templateStructure,
        CancellationToken cancellationToken = default)
    {
        var client = new ChatClient(_settings.Model, new ApiKeyCredential(_settings.ApiKey));

        var schema = BinaryData.FromString(RuleSetJsonSchema);
        var responseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
            jsonSchemaFormatName: "rule_set",
            jsonSchema: schema,
            jsonSchemaFormatDescription: "Padrão de conformidade estruturado para documentos Word.",
            jsonSchemaIsStrict: true);

        var options = new ChatCompletionOptions { ResponseFormat = responseFormat };

        var system = """
            Você é um especialista em padronização documental. Receba (1) regras textuais
            descritivas e (2) a estrutura extraída de um modelo .docx (estilos, cabeçalhos,
            rodapés e seções). Produza um RuleSet em JSON estrito conforme o schema fornecido.

            Diretrizes:
            - Sempre use camelCase nas chaves.
            - Severidades válidas: info | warning | error.
            - Tipos de regra válidos: style | header | footer | section | governance.
            - Estilos referenciam nomes ou ids vistos no modelo (titulo1, Heading1, Normal, etc.).
            - Em "contains" de headers/footers, liste tokens curtos e literais a procurar.
            - Não invente regras sem suporte no texto ou no modelo.
            """;

        var user = BuildUserPrompt(clientName, versionLabel, rulesText, templateStructure);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(system),
            new UserChatMessage(user)
        };

        var response = await client.CompleteChatAsync(messages, options, cancellationToken);
        var text = response.Value.Content.Count > 0 ? response.Value.Content[0].Text : "{}";

        return RuleSetSerializer.Deserialize(text);
    }

    private static string BuildUserPrompt(
        string clientName, string versionLabel, string rulesText, DocumentStructure t)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Cliente: {clientName}");
        sb.AppendLine($"Versão a gerar: {versionLabel}");
        sb.AppendLine();
        sb.AppendLine("=== Regras textuais ===");
        sb.AppendLine(rulesText);
        sb.AppendLine();
        sb.AppendLine("=== Estrutura extraída do modelo ===");
        sb.AppendLine(JsonSerializer.Serialize(new
        {
            styles = t.Styles.Values.Take(40),
            headers = t.Headers,
            footers = t.Footers,
            sections = t.Sections,
            tableCount = t.TableCount
        }, new JsonSerializerOptions { WriteIndented = true }));
        return sb.ToString();
    }

    private const string RuleSetJsonSchema = """
    {
      "type": "object",
      "additionalProperties": false,
      "required": ["cliente", "versaoPadrao", "styles", "headers", "footers", "rules"],
      "properties": {
        "cliente": { "type": "string" },
        "versaoPadrao": { "type": "string" },
        "styles": {
          "type": "object",
          "additionalProperties": {
            "type": "object",
            "additionalProperties": false,
            "required": ["font", "size", "bold", "italic", "alignment"],
            "properties": {
              "font": { "type": ["string", "null"] },
              "size": { "type": ["number", "null"] },
              "bold": { "type": ["boolean", "null"] },
              "italic": { "type": ["boolean", "null"] },
              "alignment": { "type": ["string", "null"] }
            }
          }
        },
        "headers": {
          "type": "array",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "required": ["required", "contains"],
            "properties": {
              "required": { "type": "boolean" },
              "contains": { "type": "array", "items": { "type": "string" } }
            }
          }
        },
        "footers": {
          "type": "array",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "required": ["required", "contains"],
            "properties": {
              "required": { "type": "boolean" },
              "contains": { "type": "array", "items": { "type": "string" } }
            }
          }
        },
        "rules": {
          "type": "array",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "required": ["id", "type", "severity", "message"],
            "properties": {
              "id": { "type": "string" },
              "type": { "type": "string", "enum": ["style", "header", "footer", "section", "governance"] },
              "severity": { "type": "string", "enum": ["info", "warning", "error"] },
              "message": { "type": "string" }
            }
          }
        }
      }
    }
    """;
}
