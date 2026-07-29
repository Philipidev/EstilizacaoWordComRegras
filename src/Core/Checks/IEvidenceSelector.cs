using WordComplianceValidator.Core.Checklist;

namespace WordComplianceValidator.Core.Checks;

/// <summary>
/// Monta o "pacote de evidências" enviado ao LLM para uma entrada do checklist. É o ponto
/// de extensão que controla custo e precisão do motor semântico: enviar o documento inteiro
/// a cada regra seria caro e diluiria o sinal, então cada regra recebe só o recorte relevante.
/// </summary>
public interface IEvidenceSelector
{
    string Build(DocumentContext ctx, ChecklistEntry entry);
}
