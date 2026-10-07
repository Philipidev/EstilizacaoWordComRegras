using System.Collections.Concurrent;
using System.ClientModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    // Uma entrada por (modelo, prefixo cacheável): a primeira chamada com aquele prefixo segue
    // sozinha e as demais esperam ela terminar. Ver CompletarAsync.
    private readonly ConcurrentDictionary<string, Task> _aquecimento = new(StringComparer.Ordinal);

    public ConsumoDeTokens Consumo { get; } = new();

    public OpenAiSemanticChecker(OpenAiSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException(
                "OpenAI API key não configurada. Defina OPENAI_API_KEY ou OpenAI:ApiKey em appsettings.");
        _settings = settings;
    }

    private string Modelo(string? modelo) =>
        string.IsNullOrWhiteSpace(modelo) ? _settings.Model : modelo;

    private ChatClient Client(string modelo) =>
        _clients.GetOrAdd(modelo, nome => new ChatClient(nome, new ApiKeyCredential(_settings.ApiKey)));

    private ChatCompletionOptions Opcoes(ChatResponseFormat formato)
    {
        var options = new ChatCompletionOptions { ResponseFormat = formato };
        // OPENAI001: o SDK marca reasoning_effort como experimental, mas o parâmetro é estável
        // na API e é o que define o custo de raciocínio de cada chamada.
#pragma warning disable OPENAI001
        if (!string.IsNullOrWhiteSpace(_settings.ReasoningEffort))
            options.ReasoningEffortLevel =
                new ChatReasoningEffortLevel(_settings.ReasoningEffort.Trim().ToLowerInvariant());
#pragma warning restore OPENAI001
        return options;
    }

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

        var options = Opcoes(ChatResponseFormat.CreateJsonSchemaFormat(
            jsonSchemaFormatName: "verdict",
            jsonSchema: schema,
            jsonSchemaFormatDescription: "Veredito booleano com justificativa.",
            jsonSchemaIsStrict: true));

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                "Você avalia conformidade documental. Responda EXCLUSIVAMENTE com JSON " +
                "no schema fornecido. 'conforme'=true se o conteúdo atende à instrução; " +
                "'justificativa' deve ser curta (1-2 frases) em PT-BR."),
            new UserChatMessage(
                $"Instrução de verificação:\n{instrucao}\n\nConteúdo a avaliar:\n{conteudo}")
        };

        var text = await CompletarAsync(Modelo(null), messages, options, prefixoCacheavel: null, cancellationToken);

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

        var options = Opcoes(ChatResponseFormat.CreateJsonSchemaFormat(
            jsonSchemaFormatName: "avaliacao",
            jsonSchema: schema,
            jsonSchemaFormatDescription: "Avaliação de conformidade com achados individuais.",
            jsonSchemaIsStrict: true));

        // Ordem pensada para o cache de prompt: instruções gerais e evidência (iguais para todas
        // as regras do documento) vêm em mensagens de sistema no início, e só o item do
        // checklist — a parte que muda — vai na mensagem do usuário. O cache implícito marca o
        // fim do bloco inicial de mensagens de sistema como fronteira; com o item antes da
        // evidência, como era antes, nenhuma chamada reaproveitava o prefixo e o gpt-6.1-sol
        // ainda cobrava a gravação do cache (1,25× a entrada) em todas elas.
        var evidencia = $"### Conteúdo extraído do documento\n{conteudo}";
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(InstrucoesDoAvaliador),
            new SystemChatMessage(evidencia),
            new UserChatMessage($"### Item do checklist a verificar\n{instrucao}")
        };

        var text = await CompletarAsync(Modelo(modelo), messages, options, prefixoCacheavel: evidencia, cancellationToken);
        return Parse(text);
    }

    private const string InstrucoesDoAvaliador =
        """
        Você é um revisor de conformidade de documentos técnicos de engenharia, avaliando
        um documento contra um item de checklist. Responda EXCLUSIVAMENTE com JSON no
        schema fornecido, em PT-BR. A próxima mensagem traz o conteúdo extraído do
        documento; a mensagem do usuário traz o item do checklist a verificar.

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
        """;

    /// <summary>
    /// Envia a requisição, registra o consumo e devolve o texto da resposta.
    /// <para>
    /// Com <paramref name="prefixoCacheavel"/>, a primeira chamada de cada (modelo, prefixo)
    /// vai sozinha e as concorrentes esperam: o cache só pode ser lido depois de gravado, e
    /// seis regras disparadas juntas sobre um cache frio pagariam seis gravações em vez de
    /// uma — no gpt-6.1-sol, gravar custa 1,25× a entrada e ler custa 0,05×.
    /// </para>
    /// </summary>
    private async Task<string> CompletarAsync(
        string modelo, List<ChatMessage> messages, ChatCompletionOptions options,
        string? prefixoCacheavel, CancellationToken cancellationToken)
    {
        if (prefixoCacheavel is null)
            return await EnviarAsync(modelo, messages, options, cancellationToken);

        var chave = modelo + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prefixoCacheavel)));
        var minha = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var primeira = _aquecimento.GetOrAdd(chave, minha.Task);

        if (primeira != minha.Task)
        {
            // A primeira chamada sempre libera, com sucesso ou falha: o objetivo é só não
            // disputar o cache frio, não depender do resultado dela.
            await primeira.WaitAsync(cancellationToken);
            return await EnviarAsync(modelo, messages, options, cancellationToken);
        }

        try
        {
            return await EnviarAsync(modelo, messages, options, cancellationToken);
        }
        finally
        {
            minha.TrySetResult();
        }
    }

    private async Task<string> EnviarAsync(
        string modelo, List<ChatMessage> messages, ChatCompletionOptions options,
        CancellationToken cancellationToken)
    {
        var response = await Client(modelo).CompleteChatAsync(messages, options, cancellationToken);
        RegistrarConsumo(modelo, response.GetRawResponse().Content);

        var completion = response.Value;
        var text = completion.Content.Count > 0 ? completion.Content[0].Text : null;

        // Resposta vazia era tratada como "{}" e virava "não aplicável" em silêncio: uma recusa
        // ou um corte por limite de tokens aprovava a regra sem ninguém avaliar nada. Agora a
        // regra sai como erro de execução, que aparece no relatório.
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException(
                $"O modelo {modelo} não devolveu conteúdo (motivo: {completion.FinishReason}" +
                (string.IsNullOrWhiteSpace(completion.Refusal) ? "" : $"; recusa: {completion.Refusal}") + ").");

        return text;
    }

    /// <summary>
    /// Lê o consumo do JSON bruto da resposta: o SDK não expõe <c>cache_write_tokens</c>, e é
    /// justamente a parcela que encarece o gpt-6.1-sol quando o cache não é reaproveitado.
    /// </summary>
    private void RegistrarConsumo(string modelo, BinaryData? corpo)
    {
        if (corpo is null) return;
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            if (!doc.RootElement.TryGetProperty("usage", out var u)) return;

            Consumo.Registrar(modelo,
                entrada: Inteiro(u, "prompt_tokens"),
                entradaEmCache: Inteiro(u, "prompt_tokens_details", "cached_tokens"),
                escritaEmCache: Inteiro(u, "prompt_tokens_details", "cache_write_tokens"),
                saida: Inteiro(u, "completion_tokens"),
                raciocinio: Inteiro(u, "completion_tokens_details", "reasoning_tokens"));
        }
        catch (JsonException)
        {
            // Contabilidade é acessória: um corpo inesperado não pode derrubar a avaliação.
        }
    }

    private static long Inteiro(JsonElement e, params string[] caminho)
    {
        foreach (var nome in caminho)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(nome, out e)) return 0;
        }
        return e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var v) ? v : 0;
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
