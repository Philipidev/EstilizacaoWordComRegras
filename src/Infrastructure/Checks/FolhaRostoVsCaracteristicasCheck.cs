using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.2 Cliente — Folha de Rosto: padrão de folha de rosto do cliente e compatibilidade
/// entre a folha de rosto e o quadro "Características do Documento" (avaliação semântica).
/// <para>
/// Antes este check montava a própria evidência: os 40 primeiros parágrafos do documento como
/// "folha de rosto" e o quadro achatado em rótulo→valor. Nos documentos de referência a folha
/// índice do Cliente sozinha ocupa ~200 parágrafos de célula, então código, título e cliente
/// nunca chegavam ao avaliador ("não constam na folha de rosto apresentada"), e o quadro
/// achatado misturava as colunas PdA e Cliente. Resultado: reprovação nos dois documentos
/// conformes. Agora ele usa o mesmo pacote de evidências do motor genérico — que preserva as
/// colunas e traz a capa inteira — e sai mais barato, porque esse pacote já está no cache.
/// </para>
/// </summary>
public sealed class FolhaRostoVsCaracteristicasCheck : IRuleCheck
{
    private static readonly ChecklistRef Item = new("PS-002", "4.3.2", ChecklistPadrao.Cliente);

    private readonly SemanticChecklistCheck _avaliacao;

    public FolhaRostoVsCaracteristicasCheck(ISemanticChecker semantic, IEvidenceSelector? evidence = null)
    {
        var entrada = new ChecklistEntry(
            Ref: Item,
            Revisao: null,
            ProcedimentoTitulo: "Edição de Documentos Técnicos",
            Assunto: "Folha de Rosto",
            Titulo: "Folha de Rosto",
            Descricao:
                "Verificar se o padrão de folha de rosto do cliente foi adotado (quando aplicável) e " +
                "se há compatibilidade entre as informações da folha de rosto e do quadro " +
                "“Características do Documento”, assegurando coerência e rastreabilidade. Compare " +
                "codificação (PdA e Cliente), título, cliente/contratante, revisões e datas de emissão.",
            IaAutomatizavel: true,
            Observacao: null);

        _avaliacao = new SemanticChecklistCheck(entrada, semantic, evidence ?? DefaultEvidenceSelector.Compartilhado);
    }

    public ChecklistRef Ref => Item;

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default) =>
        _avaliacao.RunAsync(ctx, cancellationToken);
}
