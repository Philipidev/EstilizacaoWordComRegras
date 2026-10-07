using AwesomeAssertions;
using Spectre.Console.Testing;
using WordComplianceValidator.Cli.Terminal;
using WordComplianceValidator.Infrastructure.OpenAI;
using WordComplianceValidator.Infrastructure.Tests.Fixtures;

namespace WordComplianceValidator.Infrastructure.Tests.Cli;

/// <summary>
/// O modo guiado de ponta a ponta, num console de mentira: a "pessoa" digita o caminho e
/// navega nos menus com as setas, e a revisão roda de verdade (sem IA) sobre um .docx gerado.
/// </summary>
public sealed class ModoGuiadoTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("modo-guiado-").FullName;
    private readonly List<string> _abertos = [];
    private readonly List<string> _pastasAbertas = [];

    public void Dispose()
    {
        try { Directory.Delete(_pasta, recursive: true); } catch (IOException) { }
    }

    private string Documento(string nome = "relatorio.docx") =>
        DocxFixtureBuilder.Novo()
            .Titulo("INTRODUÇÃO", 0)
            .Paragrafo("Texto do relatório técnico.")
            .Salvar(Path.Combine(_pasta, nome));

    private (ModoGuiado Modo, TestConsole Console) Montar()
    {
        var console = new TestConsole().Interactive();
        console.Profile.Width = 120;
        var modo = new ModoGuiado(
            new Apresentacao(console),
            new ExecutorDeRevisao(new OpenAiSettings { ApiKey = "" }),   // sem IA: nada de rede
            LocalizadorDeRecursos.Checklist(),
            LocalizadorDeRecursos.Profiles(),
            _abertos.Add,
            _pastasAbertas.Add);
        return (modo, console);
    }

    // Menu final: [Abrir o documento revisado, Abrir a pasta, Revisar outro, Sair].
    private static void Escolher(TestConsole console, int posicao)
    {
        for (var i = 0; i < posicao; i++) console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
    }

    [Fact]
    public async Task Pessoa_arrasta_o_arquivo_para_a_janela_revisa_e_sai()
    {
        var documento = Documento();
        var (modo, console) = Montar();
        // O Windows cola o caminho arrastado entre aspas.
        console.Input.PushTextWithEnter($"\"{documento}\"");
        Escolher(console, 3);

        var codigo = await modo.ExecutarAsync([], "2.0.0", TestContext.Current.CancellationToken);

        console.Output.Should().Contain("Revisor de documentos técnicos")
            .And.Contain("Qual documento você quer revisar?")
            .And.Contain("Resultado")
            .And.Contain("Documento revisado")
            .And.Contain("Até a próxima.");
        File.Exists(Path.Combine(_pasta, "relatorio-REVISADO.docx")).Should().BeTrue();
        modo.EncerradoPeloMenu.Should().BeTrue();
        codigo.Should().BeOneOf(0, 2);
    }

    [Fact]
    public async Task Arquivos_arrastados_sobre_o_exe_sao_revisados_em_sequencia_sem_perguntar()
    {
        var a = Documento("a.docx");
        var b = Documento("b.docx");
        var (modo, console) = Montar();
        Escolher(console, 3);

        await modo.ExecutarAsync([a, b], "2.0.0", TestContext.Current.CancellationToken);

        console.Output.Should().NotContain("Qual documento você quer revisar?");
        console.Output.Should().Contain("2 documento(s) recebido(s)")
            .And.Contain("Resumo dos 2 documentos");
        File.Exists(Path.Combine(_pasta, "a-REVISADO.docx")).Should().BeTrue();
        File.Exists(Path.Combine(_pasta, "b-REVISADO.docx")).Should().BeTrue();
    }

    [Fact]
    public async Task Caminho_invalido_explica_o_problema_e_pergunta_de_novo()
    {
        var documento = Documento();
        var (modo, console) = Montar();
        console.Input.PushTextWithEnter(Path.Combine(_pasta, "antigo.doc"));
        console.Input.PushTextWithEnter(Path.Combine(_pasta, "nao-existe.docx"));
        console.Input.PushTextWithEnter(documento);
        Escolher(console, 3);

        await modo.ExecutarAsync([], "2.0.0", TestContext.Current.CancellationToken);

        console.Output.Should().Contain("Word 97-2003")
            .And.Contain("Salvar como")
            .And.Contain("Não encontrei esse arquivo")
            .And.Contain("Resultado");
    }

    [Fact]
    public async Task Pasta_arrastada_lista_os_documentos_dela()
    {
        Documento("a.docx");
        Documento("b.docx");
        var (modo, console) = Montar();
        console.Input.PushTextWithEnter(_pasta);
        Escolher(console, 1);     // "b.docx"
        Escolher(console, 3);     // Sair

        await modo.ExecutarAsync([], "2.0.0", TestContext.Current.CancellationToken);

        console.Output.Should().Contain("Qual destes?");
        File.Exists(Path.Combine(_pasta, "b-REVISADO.docx")).Should().BeTrue();
        File.Exists(Path.Combine(_pasta, "a-REVISADO.docx")).Should().BeFalse();
    }

    [Fact]
    public async Task Menu_final_abre_o_documento_e_a_pasta()
    {
        var documento = Documento();
        var (modo, console) = Montar();
        console.Input.PushTextWithEnter(documento);
        Escolher(console, 0);     // Abrir o documento revisado
        Escolher(console, 1);     // Abrir a pasta
        Escolher(console, 3);     // Sair

        await modo.ExecutarAsync([], "2.0.0", TestContext.Current.CancellationToken);

        var revisado = Path.Combine(_pasta, "relatorio-REVISADO.docx");
        _abertos.Should().Equal(revisado);
        _pastasAbertas.Should().Equal(revisado);
    }

    [Fact]
    public async Task Enter_vazio_encerra_sem_revisar()
    {
        var (modo, console) = Montar();
        console.Input.PushTextWithEnter("");

        var codigo = await modo.ExecutarAsync([], "2.0.0", TestContext.Current.CancellationToken);

        codigo.Should().Be(0);
        console.Output.Should().NotContain("Resultado");
        modo.EncerradoPeloMenu.Should().BeTrue();
    }

    [Theory]
    [InlineData("\"C:\\docs\\RN-816.docx\"", "C:\\docs\\RN-816.docx")]
    [InlineData("& 'C:\\docs\\RN 816.docx'", "C:\\docs\\RN 816.docx")]
    [InlineData("  C:\\docs\\RN-816.docx  ", "C:\\docs\\RN-816.docx")]
    [InlineData("", "")]
    public void Caminho_colado_pelo_Windows_e_limpo(string colado, string esperado) =>
        ModoGuiado.LimparCaminho(colado).Should().Be(esperado);
}
