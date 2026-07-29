using System.Collections.Concurrent;
using System.ClientModel;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.OpenAI;

public sealed class OpenAiSemanticChecker : ISemanticChecker
{
    private readonly OpenAiSettings _settings;

    // Um ChatClient por modelo, criado uma vez. Antes era instanciado a cada chamada, o que
    // com dezenas de regras semânticas por documento significava dezenas de handshakes.
    private readonly ConcurrentDictionary<string, ChatClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public OpenAiSemanticChecker(OpenAiSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException(
                "OpenAI API key não configurada. Defina OPENAI_API_KEY ou OpenAI:ApiKey em appsettings.");
        _settings = settings;
    }

    private ChatClient Client(string? modelo) =>
        _clients.GetOrAdd(
            string.IsNullOrWhiteSpace(modelo) ? _settings.Model : modelo,
            nome => new ChatClient(nome, new ApiKeyCredential(_settings.ApiKey)));

    public async Task<SemanticVerdict> EvaluateAsync(
        string instrucao, string conteudo, CancellationToken cancellationToken = default)
    {
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

        var response = await Client(null).CompleteChatAsync(messages, options, cancellationToken);
        var text = response.Value.Content.Count > 0 ? response.Value.Content[0].Text : "{}";

        using var doc = JsonDocument.Parse(text);
        var conforme = doc.RootElement.GetProperty("conforme").GetBoolean();
        var justif = doc.RootElement.GetProperty("justificativa").GetString() ?? string.Empty;
        return new SemanticVerdict(conforme, justif);
    }

    public async Task<SemanticEvaluation> AvaliarAsync(
        string instrucao, string conteudo, string? modelo = null,
        CancellationToken cancellationToken = default)
    {
        var schema = BinaryData.FromString("""
        {
          "type":"object",
          "additionalProperties":false,
          "required":["status","justificativa","achados"],
          "properties":{
            "status":{"type":"string","enum":["conforme","nao_aplicavel","nao_conforme"]},
            "justificativa":{"type":"string"},
            "achados":{
              "type":"array",
              "items":{
                "type":"object",
                "additionalProperties":false,
                "required":["severidade","mensagem","trecho"],
                "properties":{
                  "severidade":{"type":"string","enum":["info","aviso","erro"]},
                  "mensagem":{"type":"string"},
                  "trecho":{"type":"string"}
                }
              }
            }
          }
        }
        """);

        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "avaliacao",
                jsonSchema: schema,
                jsonSchemaFormatDescription: "Avaliação de conformidade com achados individuais.",
                jsonSchemaIsStrict: true)
        };

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                """
                Você é um revisor de conformidade de documentos técnicos de engenharia, avaliando
                um documento contra um item de checklist. Responda EXCLUSIVAMENTE com JSON no
                schema fornecido, em PT-BR.

                Regras de julgamento:
                - "nao_aplicavel": a regra é condicional e a condição não ocorre neste documento
                  (ex.: regra sobre apêndices num documento sem apêndices), OU o conteúdo
                  fornecido não contém evidência suficiente para julgar. Na dúvida, prefira
                  "nao_aplicavel" a acusar não-conformidade.
                - "nao_conforme": há evidência concreta no conteúdo de que a regra foi violada.
                - "conforme": há evidência de que a regra foi atendida.

                Emita um item em "achados" para cada problema concreto, e apenas quando o status
                for "nao_conforme". Em "trecho", copie LITERALMENTE um trecho curto do conteúdo
                fornecido onde o problema aparece — ele é usado para ancorar o comentário no
                documento; deixe vazio se não houver trecho aplicável. Severidade: "erro" para
                violação inequívoca, "aviso" para indício, "info" para observação.
                """),
            new UserChatMessage(
                $"### Item do checklist a verificar\n{instrucao}\n\n### Conteúdo extraído do documento\n{conteudo}")
        };

        var response = await Client(modelo).CompleteChatAsync(messages, options, cancellationToken);
        var text = response.Value.Content.Count > 0 ? response.Value.Content[0].Text : "{}";

        return Parse(text);
    }

    /// <summary>
    /// Converte a resposta JSON do modelo em <see cref="SemanticEvaluation"/>. Função pura,
    /// separada da chamada de rede para poder ser exercitada sem consumir tokens.
    /// </summary>
    public static SemanticEvaluation Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var status = root.TryGetProperty("status", out var s)
            ? s.GetString() switch
            {
                "conforme" => SemanticStatus.Conforme,
                "nao_conforme" => SemanticStatus.NaoConforme,
                _ => SemanticStatus.NaoAplicavel
            }
            : SemanticStatus.NaoAplicavel;

        var justificativa = root.TryGetProperty("justificativa", out var j) ? j.GetString() : null;

        var achados = new List<SemanticFinding>();
        if (root.TryGetProperty("achados", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var mensagem = item.TryGetProperty("mensagem", out var m) ? m.GetString() : null;
                if (string.IsNullOrWhiteSpace(mensagem)) continue;

                var severidade = item.TryGetProperty("severidade", out var sev)
                    ? sev.GetString() switch
                    {
                        "erro" => Severity.Error,
                        "info" => Severity.Info,
                        _ => Severity.Warning
                    }
                    : Severity.Warning;

                var trecho = item.TryGetProperty("trecho", out var t) ? t.GetString() : null;
                achados.Add(new SemanticFinding(severidade, mensagem!,
                    string.IsNullOrWhiteSpace(trecho) ? null : trecho));
            }
        }

        // Coerência: um "nao_conforme" sem achados não é acionável, e achados sem
        // "nao_conforme" seriam descartados silenciosamente.
        if (status == SemanticStatus.NaoConforme && achados.Count == 0)
        {
            achados.Add(new SemanticFinding(Severity.Warning,
                justificativa ?? "Não conformidade relatada sem detalhamento.", null));
        }

        return new SemanticEvaluation(status, achados, justificativa);
    }
}
