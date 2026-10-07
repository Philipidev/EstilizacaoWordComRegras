using WordComplianceValidator.Application.Services;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Infrastructure.Checks;
using WordComplianceValidator.Infrastructure.Excel;
using WordComplianceValidator.Infrastructure.OpenAI;
using WordComplianceValidator.Infrastructure.OpenXml;
using WordComplianceValidator.Infrastructure.Profile;

namespace WordComplianceValidator.Cli.Terminal;

/// <summary>O que revisar e como.</summary>
public sealed record PedidoDeRevisao(
    string Documento,
    string Checklist,
    string Profile,
    string Saida,
    bool SemIa,
    bool SoIaSim = false,
    bool Detalhado = false);

/// <summary>Uma revisão concluída e o código de saída correspondente (0 conforme, 2 não conforme).</summary>
public sealed record ResultadoDaRevisao(DocumentReviewReport Relatorio, string Saida, int CodigoDeSaida);

/// <summary>
/// Executa uma revisão e a apresenta. É a única porta para o motor: o comando <c>review</c> e o
/// modo guiado passam por aqui — sem isso as duas entradas divergiriam em silêncio na
/// configuração do motor (com ou sem IA, que checks, que modelo).
/// </summary>
public sealed class ExecutorDeRevisao(OpenAiSettings openAi)
{
    public bool IaDisponivel => !string.IsNullOrWhiteSpace(openAi.ApiKey);

    public string DescricaoDaIa =>
        string.IsNullOrWhiteSpace(openAi.ReasoningEffort) ? openAi.Model : $"{openAi.Model}, raciocínio {openAi.ReasoningEffort}";

    /// <summary>
    /// Revisa e apresenta. Devolve <c>null</c> quando a revisão não chegou ao fim (erro já
    /// mostrado na tela) — o chamador decide o código de saída.
    /// </summary>
    public async Task<ResultadoDaRevisao?> ExecutarAsync(
        PedidoDeRevisao pedido, Apresentacao tela, CancellationToken cancellationToken)
    {
        var usarIa = !pedido.SemIa && IaDisponivel;
        var semantico = usarIa ? new OpenAiSemanticChecker(openAi) : null;
        var servico = MontarServico(semantico, pedido.SoIaSim);

        var perfil = await new JsonClientProfileRepository().LoadAsync(pedido.Profile, cancellationToken);
        tela.Cabecalho(pedido.Documento, perfil.Cliente, pedido.Checklist, pedido.Saida,
            usarIa ? $"completa, com IA ({DescricaoDaIa})"
                   : pedido.SemIa ? "rápida, só regras automáticas"
                   : "só regras automáticas (IA não configurada: defina OPENAI_API_KEY)");

        DocumentReviewReport relatorio;
        try
        {
            relatorio = await tela.ComProgressoAsync(progresso => servico.ReviewAsync(
                pedido.Documento, pedido.Checklist, pedido.Profile, pedido.Saida, cancellationToken, progresso));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            tela.Erro("Revisão cancelada", null, "Nenhum arquivo de saída foi gravado.");
            return null;
        }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020))
        {
            // ERROR_SHARING_VIOLATION: o caso mais comum na prática — a cópia revisada anterior
            // ainda está aberta no Word.
            tela.Erro("O arquivo de saída está aberto em outro programa", pedido.Saida,
                "Feche o documento no Word e tente de novo.");
            return null;
        }
        catch (Exception ex)
        {
            tela.Erro("A revisão não foi concluída", null, ex.Message);
            return null;
        }

        tela.Resumo(relatorio);
        tela.Detalhes(relatorio, pedido.Detalhado);
        tela.Saida(pedido.Saida, relatorio.CommentsInserted);
        if (semantico is not null)
            tela.Consumo(semantico.Consumo.Snapshot(), openAi.Precos, pedido.Detalhado);

        var codigo = relatorio.Violations.Any(v => v.Severity == Severity.Error) ? 2 : 0;
        return new ResultadoDaRevisao(relatorio, pedido.Saida, codigo);
    }

    private static DocumentReviewService MontarServico(OpenAiSemanticChecker? semantico, bool soIaSim)
    {
        Func<ChecklistEntry, DocumentContext, IRuleCheck?>? fallback =
            semantico is null ? null : new SemanticCheckFactory(semantico).Create;

        var engine = new ChecklistEngine(
            CheckRegistry.Deterministicos(semantico), honrarColunaIa: soIaSim, fallback: fallback);

        return new DocumentReviewService(
            new DocxStructureExtractor(),
            new ExcelChecklistRepository(),
            new JsonClientProfileRepository(),
            engine,
            new CommentInserter());
    }
}
