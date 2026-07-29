using FluentAssertions;
using WordComplianceValidator.Cli.Terminal;

namespace WordComplianceValidator.Infrastructure.Tests.Cli;

/// <summary>
/// Resolução de caminhos do CLI. O risco coberto aqui é o binário publicado não achar o
/// checklist nem os profiles: eles eram procurados a partir do diretório de trabalho, que num
/// .exe chamado de outra pasta é qualquer coisa.
/// </summary>
public class ResolucaoDeCaminhosTests
{
    [Fact]
    public void Saida_padrao_fica_ao_lado_do_documento()
    {
        var entrada = Path.Combine(Path.GetTempPath(), "pasta", "RN-816-RL-67456-00.docx");

        var saida = LocalizadorDeRecursos.SaidaPadrao(entrada);

        Path.GetDirectoryName(saida).Should().Be(Path.GetDirectoryName(Path.GetFullPath(entrada)));
        Path.GetFileName(saida).Should().Be("RN-816-RL-67456-00-REVISADO.docx");
    }

    [Fact]
    public void Checklist_e_profiles_sao_encontrados_a_partir_do_executavel()
    {
        LocalizadorDeRecursos.Checklist().Should().NotBeNull(
            "o default de --checklist nao pode depender do diretorio de trabalho");
        LocalizadorDeRecursos.Profiles().Should().NotBeEmpty();
    }
}
