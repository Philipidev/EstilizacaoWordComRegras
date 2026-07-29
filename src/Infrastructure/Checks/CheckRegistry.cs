using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// Registro central dos checks determinísticos. Fica na Infrastructure — e não no
/// <c>Program.cs</c> — para que os testes possam auditar a cobertura do CL-001 contra a
/// mesma lista que o CLI executa, em vez de uma cópia que envelhece em silêncio.
/// </summary>
public static class CheckRegistry
{
    /// <summary>
    /// Checks que não dependem de LLM. <paramref name="semantic"/>, quando fornecido, ativa o
    /// caminho com fallback semântico nos checks que o aceitam.
    /// </summary>
    public static List<IRuleCheck> Deterministicos(ISemanticChecker? semantic = null)
    {
        var checks = new List<IRuleCheck>
        {
            // --- Regras originalmente marcadas IA=Sim no CL-001 ---
            new LogomarcasNoHeaderCheck(),
            new IniciaisDistintasCheck(ChecklistPadrao.Cliente),
            new IniciaisDistintasCheck(ChecklistPadrao.Pda),
            new CodificacaoTecnicaCheck(),
            new QuadroCaracteristicasPreenchidoCheck(ChecklistPadrao.Cliente, "4.7"),
            // O 4.7:Pda caía no avaliador semântico, que lia a grade de controle de folhas
            // dentro do quadro como se fosse numeração da página do quadro. As duas exigências
            // do item (preenchimento e ausência de numeração) são decidíveis pelo OOXML.
            new QuadroCaracteristicasPreenchidoCheck(ChecklistPadrao.Pda, "4.7"),
            new QuadroCaracteristicasPreenchidoCheck(ChecklistPadrao.Cliente, "4.3.3 (letra e)"),
            new ConsistenciaIniciaisCheck(ChecklistPadrao.Cliente),
            new ConsistenciaIniciaisCheck(ChecklistPadrao.Pda),
            new IndiceAtualizadoCheck(),
            new PaginacaoAtualizadaCheck(ChecklistPadrao.Cliente, "4.3.3 (letra f)"),
            // O 4.3.3(f):Pda caía no avaliador semântico, que lia o valor cacheado do campo
            // PAGE ("FL.: 7/99", igual em toda parte de cabeçalho) como paginação repetida.
            new PaginacaoAtualizadaCheck(ChecklistPadrao.Pda, "4.3.3 (letra f)"),
            new PaginacaoAtualizadaCheck(ChecklistPadrao.Cliente, "4.3.6.6"),
            new LocalizacaoCodificacaoCheck(),
            new CodificacaoClienteCheck(),
            new EvolucaoDocumentoCheck(),
            new ReferenciasCruzadasCheck(),
            new CoerenciaRevisoesCheck(),
            new ContinuidadeTituloConteudoCheck(ChecklistPadrao.Cliente),
            new ContinuidadeTituloConteudoCheck(ChecklistPadrao.Pda),

            // --- Regras que o CL-001 marcava IA=Não mas são decidíveis pelo OOXML ---
            // PS-024: tarjas de emissão (busca literal, condicionada à revisão do documento).
            new TarjaEmissaoCheck(ChecklistPadrao.Pda, "4.1.2"),
            new TarjaEmissaoCheck(ChecklistPadrao.Cliente, "4.1.2"),
            new TarjaEmissaoCheck(ChecklistPadrao.Pda, "4.1.3"),
            new TarjaEmissaoCheck(ChecklistPadrao.Cliente, "4.1.3"),
            new TarjaEmissaoCheck(ChecklistPadrao.Pda, "4.1.4"),
            new TarjaEmissaoCheck(ChecklistPadrao.Cliente, "4.1.4"),
            // PS-002 4.3.6.x: formatação do corpo e elementos gráficos.
            new FormatacaoCorpoCheck(ChecklistPadrao.Pda, "4.3.6.1"),
            new FormatacaoCorpoCheck(ChecklistPadrao.Cliente, "4.3.6.2"),
            new ElementosGraficosCheck(ChecklistPadrao.Pda, "4.3.6.3"),
            new ElementosGraficosCheck(ChecklistPadrao.Cliente, "4.3.6.4"),
            new NumeracaoPaginasCheck(ChecklistPadrao.Pda, "4.3.6.5"),
            // PS-002 4.3.4.x / 4.3.5.2: índice e painel de navegação.
            new IndicePaginaCheck(ChecklistPadrao.Pda, "4.3.4.1"),
            new IndiceConteudoCheck(ChecklistPadrao.Pda, "4.3.4.3"),
            new IndiceConteudoCheck(ChecklistPadrao.Pda, "4.3.4.4"),
            new PainelNavegacaoCheck(ChecklistPadrao.Pda, "4.3.4.6"),
            new PainelNavegacaoCheck(ChecklistPadrao.Cliente, "4.3.5.2"),
            // PS-002 4.3.8: apêndices/anexos referenciados no quadro Características.
            new ApendiceAnexoCheck(ChecklistPadrao.Pda, "4.3.8"),
            new ApendiceAnexoCheck(ChecklistPadrao.Cliente, "4.3.8")
        };

        // Estes dois aceitam ISemanticChecker como reforço opcional; sem LLM caem no
        // caminho heurístico puro.
        checks.Add(new FolhaRostoCheck(semantic));
        checks.Add(new CabecalhosPadronizadosCheck(semantic));

        // Depende obrigatoriamente de avaliação semântica.
        if (semantic is not null)
            checks.Add(new FolhaRostoVsCaracteristicasCheck(semantic));

        return checks;
    }
}
