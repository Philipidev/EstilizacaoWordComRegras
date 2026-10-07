using AwesomeAssertions;
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
            // Codificação PdA: 15 caracteres (PS-018 4.4), ex.: RN-816-RL-67456; com o sufixo de
            // revisão, 18 (RN-816-RL-67456-00). O "QD5-PDA-26-04-095-RT" é a codificação do
            // Cliente (MRN) — o regex antigo, rotulado PdA, casava justamente ela.
            ["codificacao.pdaRegex"] = "^[A-Z]{2}-\\d{3}-[A-Z]{2}-\\d{5}$",
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
            var result = await check.RunAsync(ctx, TestContext.Current.CancellationToken);
            var details = $"{fileName} - {result.Ref}: " +
                          string.Join(" | ", result.Violations.Select(v => $"{v.Severity}: {v.Message}"));

            if (DefeitosConhecidos.Contains((fileName, result.Ref.ToString()))) continue;

            // Aceita Passed ou Skipped (heurísticas que não conseguem afirmar).
            result.Status.Should().BeOneOf(new[] { CheckStatus.Passed, CheckStatus.Skipped }, details);
            result.Violations.Should().NotContain(v => v.Severity == Severity.Error, details);
        }
    }

    /// <summary>
    /// Não conformidades reais dos documentos de referência.
    /// <para>
    /// O corpus prova ausência de falso positivo, o que só vale enquanto os documentos forem
    /// de fato conformes. Cada entrada aqui é dívida do documento, não do validador, e precisa
    /// de um teste próprio que fixe o achado para que a exceção não vire um buraco silencioso.
    /// </para>
    /// O RN-816 já constou aqui pela tarja de comentários "remanescente" — que está só em
    /// cabeçalhos 'first'/'even' que o Word não exibe (ver
    /// <see cref="Tarja_do_RN816_so_existe_em_cabecalho_que_o_Word_nao_exibe"/>).
    /// </summary>
    private static readonly HashSet<(string Arquivo, string Regra)> DefeitosConhecidos =
    [
        ("RN-816-RL-67456-00.docx", "PS-002:4.3.3 (letra h):Cliente")
    ];

    /// <summary>
    /// O RN-816 tem quatro campos REF vazios — sem resultado, logo depois de referências
    /// válidas a "Figura 77", "88", "91" e "92" — cujos indicadores de destino não existem em
    /// parte nenhuma do pacote. Hoje não aparecem no texto; ao atualizar os campos, o Word
    /// escreve "Erro! Indicador não definido" nesses pontos.
    /// </summary>
    [Fact]
    public async Task Referencias_cruzadas_quebradas_do_RN816_sao_detectadas()
    {
        var ctx = ReferenceDocumentContext("RN-816-RL-67456-00.docx");

        var result = await new ReferenciasCruzadasCheck().RunAsync(ctx, TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Failed);
        ctx.Structure.BrokenReferences.Select(r => r.Bookmark).Should().BeEquivalentTo(
            "_Ref223474267", "_Ref222749372", "_Ref222920728", "_Ref222920729");

        // São campos REF vazios (begin/instr/end, sem resultado) logo depois de uma referência
        // válida: invisíveis hoje, viram "Erro! Indicador não definido" ao atualizar os campos.
        ctx.Structure.BrokenReferences.Should().OnlyContain(r => r.DisplayedText == null);
        ctx.Structure.BrokenReferences.Should().Contain(r => r.TextBefore.EndsWith("A Figura 77"));

        // A mensagem diz onde está o campo, não o nome interno do indicador.
        result.Violations.Should().Contain(v => v.Message.Contains("logo depois de «A Figura 77»"));
        result.Violations.Should().NotContain(v => v.Message.Contains("_Ref"));
    }

    /// <summary>
    /// O RN-816 (revisão 00) guarda a tarja "EMISSÃO PARA COMENTÁRIOS DO CLIENTE" nos
    /// cabeçalhos de primeira página e de páginas pares de várias seções, mas nenhuma seção
    /// ativa "primeira página diferente" e o documento não usa "pares e ímpares diferentes":
    /// a tarja não aparece em página nenhuma. Isso era reprovado como erro — e o mesmo resíduo
    /// alimentava quatro achados semânticos sobre "status de emissão incoerente".
    /// </summary>
    [Fact]
    public async Task Tarja_do_RN816_so_existe_em_cabecalho_que_o_Word_nao_exibe()
    {
        var ctx = ReferenceDocumentContext("RN-816-RL-67456-00.docx");
        var check = new TarjaEmissaoCheck(
            WordComplianceValidator.Core.Checklist.ChecklistPadrao.Cliente, "4.1.2");

        var result = await check.RunAsync(ctx, TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Skipped);
        var aviso = result.Violations.Should().ContainSingle().Which;
        aviso.Severity.Should().Be(Severity.Warning);
        aviso.Message.Should().Contain("não aparece").And.Contain("Comentários do Cliente");

        ctx.Structure.Headers.Should().OnlyContain(h => h.Kind == "default",
            "sem titlePg e sem evenAndOddHeaders só o cabeçalho padrão é exibido");
        ctx.Structure.HiddenHeaderFooters.Should().Contain(h =>
            DocumentoTexto.Normalizar(h.Text).Contains("emissao para comentarios do cliente"));
    }

    [Fact]
    public async Task Revisao_vigente_do_RN816_e_a_ultima_do_historico()
    {
        var ctx = ReferenceDocumentContext("RN-816-RL-67456-00.docx");
        var quadro = QuadroCaracteristicas.Find(ctx.Structure, ["Características do Documento"]);

        quadro.Should().NotBeNull();

        var historico = QuadroCaracteristicas.HistoricoDeRevisoes(quadro!);
        historico.Select(e => e.Revisao).Should().Equal("0A", "0B", "00");

        // O cabeçalho da grade de folhas comeca com "Rev." e devolvia "0B" a quem lesse a
        // primeira linha rotulada — a revisao vigente e a ultima do historico.
        QuadroCaracteristicas.RevisaoVigente(quadro!).Should().Be("00");
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
