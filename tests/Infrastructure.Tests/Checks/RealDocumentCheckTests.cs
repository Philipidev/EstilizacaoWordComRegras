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

    [Fact]
    public async Task Implemented_checks_pass_for_reference_document()
    {
        var ctx = ReferenceDocumentContext();
        IRuleCheck[] checks =
        [
            new LogomarcasNoHeaderCheck(),
            new IniciaisDistintasCheck(),
            new CodificacaoTecnicaCheck()
        ];

        foreach (var check in checks)
        {
            var result = await check.RunAsync(ctx);
            var details = $"{result.Ref}: {string.Join(" | ", result.Violations.Select(v => $"{v.Severity}: {v.Message}"))}";

            result.Status.Should().Be(CheckStatus.Passed, details);
            result.Violations.Should().BeEmpty(details);
        }
    }

    private static DocumentContext ReferenceDocumentContext()
    {
        var path = RepoFile("templates", "RN799RL6496600.docx");
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
