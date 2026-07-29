using FluentAssertions;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Profile;
using WordComplianceValidator.Infrastructure.Checks;
using WordComplianceValidator.Infrastructure.OpenXml;

namespace WordComplianceValidator.Infrastructure.Tests.Checks;

public class RealDocumentCheckTests
{
    private static readonly IReadOnlyDictionary<string, string> ProfileParameters =
        new Dictionary<string, string>
        {
            ["logomarcas.minimo"] = "2",
            ["quadroCaracteristicas.aliases"] = "Características do Documento|Características Técnicas|Quadro de Características|Características",
            ["iniciais.rotuloElaborador"] = "Elaborado por|Emissor|Elaborador",
            ["iniciais.rotuloVerificador"] = "Verificado por|Verificador|Verificador Técnico",
            ["codificacao.pdaRegex"] = "^[A-Z]{2}\\d{1,3}-PDA-\\d{2}-\\d{2}-\\d{3}-[A-Z]{2}$",
            ["codificacao.clienteRegex"] = "^[A-Z0-9]{2,4}-[A-Z]{2,4}-\\d{2}-\\d{2}-\\d{3}-[A-Z]{2}$",
            ["codificacao.aliasesRotulo"] = "Codificação PdA|Codificação|Código do Documento|Documento|Código"
        };

    [Theory]
    [InlineData("RN799RL6496600.docx")]
    [InlineData("RN-816-RL-67456-00.docx")]
    public async Task Implemented_checks_pass_for_reference_document(string fileName)
    {
        var ctx = ReferenceDocumentContext(fileName);
        IRuleCheck[] checks =
        [
            new LogomarcasNoHeaderCheck(),
            new IniciaisDistintasCheck(),
            new IniciaisDistintasCheck(WordComplianceValidator.Core.Checklist.ChecklistPadrao.Pda),
            new CodificacaoTecnicaCheck(),
            new QuadroCaracteristicasPreenchidoCheck(),
            new QuadroCaracteristicasPreenchidoCheck(
                WordComplianceValidator.Core.Checklist.ChecklistPadrao.Cliente, "4.3.3 (letra e)"),
            new ConsistenciaIniciaisCheck(),
            new IndiceAtualizadoCheck(),
            new PaginacaoAtualizadaCheck(
                WordComplianceValidator.Core.Checklist.ChecklistPadrao.Cliente, "4.3.3 (letra f)"),
            new PaginacaoAtualizadaCheck(
                WordComplianceValidator.Core.Checklist.ChecklistPadrao.Cliente, "4.3.6.6"),
            new CabecalhosPadronizadosCheck(),
            new ReferenciasCruzadasCheck(),
            new ContinuidadeTituloConteudoCheck(),
            new LocalizacaoCodificacaoCheck(),
            new CodificacaoClienteCheck(),
            new EvolucaoDocumentoCheck(),
            new FolhaRostoCheck(),

            // Regras que o CL-001 marcava IA=Não e passaram a ser automatizadas.
            new TarjaEmissaoCheck(WordComplianceValidator.Core.Checklist.ChecklistPadrao.Cliente, "4.1.2"),
            new TarjaEmissaoCheck(WordComplianceValidator.Core.Checklist.ChecklistPadrao.Cliente, "4.1.3"),
            new TarjaEmissaoCheck(WordComplianceValidator.Core.Checklist.ChecklistPadrao.Cliente, "4.1.4"),
            new FormatacaoCorpoCheck(WordComplianceValidator.Core.Checklist.ChecklistPadrao.Pda, "4.3.6.1"),
            new FormatacaoCorpoCheck(WordComplianceValidator.Core.Checklist.ChecklistPadrao.Cliente, "4.3.6.2"),
            new ElementosGraficosCheck(WordComplianceValidator.Core.Checklist.ChecklistPadrao.Cliente, "4.3.6.4"),
            new NumeracaoPaginasCheck(WordComplianceValidator.Core.Checklist.ChecklistPadrao.Pda, "4.3.6.5"),
            new IndicePaginaCheck(WordComplianceValidator.Core.Checklist.ChecklistPadrao.Pda, "4.3.4.1"),
            new IndiceConteudoCheck(WordComplianceValidator.Core.Checklist.ChecklistPadrao.Pda, "4.3.4.3"),
            new IndiceConteudoCheck(WordComplianceValidator.Core.Checklist.ChecklistPadrao.Pda, "4.3.4.4"),
            new PainelNavegacaoCheck(WordComplianceValidator.Core.Checklist.ChecklistPadrao.Cliente, "4.3.5.2"),
            new ApendiceAnexoCheck(WordComplianceValidator.Core.Checklist.ChecklistPadrao.Cliente, "4.3.8")
        ];

        foreach (var check in checks)
        {
            var result = await check.RunAsync(ctx);
            var details = $"{fileName} - {result.Ref}: " +
                          string.Join(" | ", result.Violations.Select(v => $"{v.Severity}: {v.Message}"));

            // Aceita Passed ou Skipped (heurísticas que não conseguem afirmar).
            result.Status.Should().BeOneOf(new[] { CheckStatus.Passed, CheckStatus.Skipped }, details);
            result.Violations.Should().NotContain(v => v.Severity == Severity.Error, details);
        }
    }

    private static DocumentContext ReferenceDocumentContext(string fileName)
    {
        var path = RepoFile("templates", fileName);
        var structure = new DocxStructureExtractor().ExtractFromFile(path);
        var profile = new ClientProfile("MRN (exemplo)", "1.0", ProfileParameters);

        return new DocumentContext(path, structure, profile);
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(new[] { dir }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }

        throw new FileNotFoundException("Arquivo do repositório não encontrado.", Path.Combine(parts));
    }
}
