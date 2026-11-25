using AwesomeAssertions;
using WordComplianceValidator.Infrastructure.Profile;

namespace WordComplianceValidator.Infrastructure.Tests.Profile;

/// <summary>
/// O cliente é reconhecido pela codificação do cliente no quadro e nos cabeçalhos: cada
/// documento real cai no profile certo, e o formulário em branco (sem código) fica sem
/// resposta, para a pessoa escolher.
/// </summary>
public class IdentificadorDeClienteTests
{
    [Theory]
    [InlineData("RN799RL6496600.docx", "mrn.json", "MRN")]
    [InlineData("RN-664-RL-60947-02.docx", "mrn.json", "MRN")]
    [InlineData("RN-778-ET-67440-00.docx", "mrn.json", "MRN")]
    [InlineData("RD-276-RL-68014-01.docx", "vale.json", "Vale")]
    [InlineData("RD-673-RL-65239-00.docx", "vale.json", "Vale")]
    [InlineData("RD-771-RL-67131-00.docx", "vale.json", "Vale")]
    public async Task Documento_real_e_reconhecido_pelo_codigo_do_cliente(string documento, string profile, string cliente)
    {
        var resultado = await IdentificadorDeCliente.IdentificarAsync(
            RepoFile("templates", documento), Profiles(), TestContext.Current.CancellationToken);

        resultado.Should().NotBeNull();
        Path.GetFileName(resultado!.Profile).Should().Be(profile);
        resultado.Cliente.Should().Be(cliente);
    }

    [Fact]
    public async Task Formulario_sem_codigo_nao_e_reconhecido()
    {
        var resultado = await IdentificadorDeCliente.IdentificarAsync(
            RepoFile("templates", "FR-115-01 - Quadro Características_Word.docx"), Profiles(),
            TestContext.Current.CancellationToken);

        resultado.Should().BeNull();
    }

    private static IReadOnlyList<string> Profiles() =>
        Directory.GetFiles(RepoFile("profiles"), "*.json").OrderBy(p => p).ToList();

    private static string RepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "EstilizacaoWordComRegras.slnx")))
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        return Path.Combine(new[] { dir }.Concat(parts).ToArray());
    }
}
