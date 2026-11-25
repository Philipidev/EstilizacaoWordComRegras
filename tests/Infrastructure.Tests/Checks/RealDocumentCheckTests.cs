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
            ["iniciais.rotuloVerificador"] = "Verificado por|Verificador|Verificador Técnico|V. Técnico",
            // Codificação PdA: 15 caracteres (PS-018 4.4), ex.: RN-816-RL-67456; com o sufixo de
            // revisão, 18 (RN-816-RL-67456-00). O "QD5-PDA-26-04-095-RT" é a codificação do
            // Cliente (MRN) — o regex antigo, rotulado PdA, casava justamente ela.
            ["codificacao.pdaRegex"] = "^[A-Z]{2}-\\d{3}-[A-Z]{2}-\\d{5}$",
            ["codificacao.clienteRegex"] = "^[A-Z0-9]{2,4}-[A-Z]{2,4}-\\d{2}-\\d{2}-\\d{3}-[A-Z]{2}$",
            ["codificacao.aliasesRotulo"] = "Codificação PdA|Codificação|Código do Documento|Documento|Código"
        };

    [Theory]
    [InlineData("RN799RL6496600.docx")]
    [InlineData("RD-276-RL-68014-01.docx")]   // Vale: capa = folha de revisões do cliente
    [InlineData("RD-673-RL-65239-00.docx")]
    [InlineData("RD-771-RL-67131-00.docx")]   // Vale: quadro principal + quadro dos apêndices
    [InlineData("RN-664-RL-60947-02.docx")]   // quadro no modelo antigo (uma tabela)
    [InlineData("RN-778-ET-67440-00.docx")]   // quadro no modelo novo FR-115-01 (duas tabelas)
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
    /// </summary>
    private static readonly HashSet<(string Arquivo, string Regra)> DefeitosConhecidos =
    [
        // RN-664: uma referência cruzada mostra "Erro! Fonte de referência não encontrada" e
        // 15 legendas (Figura 27, Tabelas 5 e 6…) não constam do índice.
        ("RN-664-RL-60947-02.docx", "PS-002:4.3.3 (letra h):Cliente"),
        ("RN-664-RL-60947-02.docx", "PS-002:4.3.4.4:Pda"),
        // RN-664: o texto do cabeçalho não é igual em todas as seções (aviso).
        ("RN-664-RL-60947-02.docx", "PS-002:4.3.3 (letra g):Cliente"),
        // RN-778: o corpo chama o anexo de "ANEXO I", o quadro o lista como "ADENDO I"; e 8
        // legendas não constam do índice.
        ("RN-778-ET-67440-00.docx", "PS-002:4.3.8:Cliente"),
        ("RN-778-ET-67440-00.docx", "PS-002:4.3.4.4:Pda"),
        // RD-673: "Metodologia FMEA aplicada ao HIRA" é nível 3 logo abaixo de um nível 1.
        ("RD-673-RL-65239-00.docx", "PS-002:4.3.5.2:Cliente")
    ];

    /// <summary>
    /// O total de páginas dos dois documentos é digitado ("FL.: x/144", "FL.: x/107") e está
    /// certo: o Word registrou 146 e 109 páginas, duas delas do quadro "Características", que
    /// não entra na contagem. Antes, todo total digitado gerava aviso de "texto fixo".
    /// </summary>
    [Theory]
    [InlineData("RN-778-ET-67440-00.docx", 144, 146)]
    [InlineData("RN-664-RL-60947-02.docx", 107, 109)]
    public async Task Total_de_paginas_digitado_confere_sem_o_quadro(string arquivo, int total, int paginasDoWord)
    {
        var ctx = ReferenceDocumentContext(arquivo);

        var resultado = await new PaginacaoAtualizadaCheck(
                WordComplianceValidator.Core.Checklist.ChecklistPadrao.Cliente, "4.3.3 (letra f)")
            .RunAsync(ctx, TestContext.Current.CancellationToken);

        ctx.Structure.Properties!.Pages.Should().Be(paginasDoWord);
        resultado.Violations.Should().BeEmpty();
        resultado.Note.Should().Contain($"Total fixo {total} confere").And.Contain("2 do quadro");
    }

    /// <summary>
    /// RN-778 usa o modelo FR-115-01: o quadro são duas tabelas (título/codificações e
    /// apêndices/grade/histórico), a grade tem o rótulo vertical "PÁGINAS" e o cabeçalho do
    /// histórico ocupa duas linhas mescladas. Lido como uma tabela só, revisão, datas e iniciais
    /// saíam "ausentes".
    /// </summary>
    [Fact]
    public void Quadro_do_modelo_novo_e_lido_inteiro()
    {
        var ctx = ReferenceDocumentContext("RN-778-ET-67440-00.docx");

        var quadro = QuadroCaracteristicas.Find(ctx.Structure,
            ["Características do Documento", "Quadro de Características", "Características"]);

        quadro.Should().NotBeNull();
        quadro!.Partes.Should().HaveCount(2);
        QuadroCaracteristicas.HistoricoDeRevisoes(quadro).Select(e => e.Revisao).Should().Equal("0A", "00");
        QuadroCaracteristicas.MaiorFolhaMarcadaNaGrade(quadro).Should().Be(144);
        QuadroCaracteristicas.PaginasDoQuadro(ctx.Structure, quadro).Should().Be(2);
    }

    /// <summary>
    /// O RN-664 tem três referências cruzadas cujo indicador de destino não existe; o Word já
    /// mostra "Erro! Fonte de referência não encontrada" nesses pontos.
    /// </summary>
    [Fact]
    public async Task Referencias_cruzadas_quebradas_do_RN664_sao_detectadas()
    {
        var ctx = ReferenceDocumentContext("RN-664-RL-60947-02.docx");

        var result = await new ReferenciasCruzadasCheck().RunAsync(ctx, TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Violations.Should().NotBeEmpty();
        // A mensagem diz onde está o campo, não o nome interno do indicador.
        result.Violations.Should().NotContain(v => v.Message.Contains("_Ref"));
    }

    /// <summary>
    /// O RD-771 (revisão 00) guarda a tarja "EMISSÃO PARA COMENTÁRIOS DO CLIENTE" no
    /// cabeçalho de páginas pares da seção 2, mas o documento não usa "pares e ímpares
    /// diferentes": a tarja não aparece em página nenhuma. É aviso de resíduo, não erro.
    /// </summary>
    [Fact]
    public async Task Tarja_do_RD771_so_existe_em_cabecalho_que_o_Word_nao_exibe()
    {
        var ctx = ReferenceDocumentContext("RD-771-RL-67131-00.docx");
        var check = new TarjaEmissaoCheck(
            WordComplianceValidator.Core.Checklist.ChecklistPadrao.Cliente, "4.1.2");

        var result = await check.RunAsync(ctx, TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Skipped);
        var aviso = result.Violations.Should().ContainSingle().Which;
        aviso.Severity.Should().Be(Severity.Warning);
        aviso.Message.Should().Contain("não aparece").And.Contain("Comentários do Cliente");

        ctx.Structure.HiddenHeaderFooters.Should().Contain(h =>
            DocumentoTexto.Normalizar(h.Text).Contains("emissao para comentarios do cliente"));
    }

    [Fact]
    public void Revisao_vigente_do_RD276_e_a_ultima_do_historico()
    {
        var ctx = ReferenceDocumentContext("RD-276-RL-68014-01.docx");
        var quadro = QuadroCaracteristicas.Find(ctx.Structure, ["Características do Documento"]);

        quadro.Should().NotBeNull();

        var historico = QuadroCaracteristicas.HistoricoDeRevisoes(quadro!);
        historico.Select(e => e.Revisao).Last().Should().Be("01");

        // O cabeçalho da grade de folhas começa com "Rev." e devolvia a revisão errada a quem
        // lesse a primeira linha rotulada — a vigente é a última do histórico.
        QuadroCaracteristicas.RevisaoVigente(quadro!).Should().Be("01");
    }

    /// <summary>
    /// Modelo da Vale: a capa é a folha de revisões do cliente, com as codificações (Nº VALE e
    /// Nº da contratada) no cabeçalho da primeira página, e o total de páginas é um PAGEREF
    /// que, no RD-771, desconta o quadro principal e o "Quadro Características dos Apêndices".
    /// </summary>
    [Fact]
    public async Task Modelo_Vale_le_codificacao_da_capa_e_desconta_os_dois_quadros()
    {
        var ctx = ReferenceDocumentContext("RD-771-RL-67131-00.docx");
        var quadro = QuadroCaracteristicas.Find(ctx.Structure, ["Características do Documento"]);

        QuadroCaracteristicas.PaginasDoQuadro(ctx.Structure, quadro!).Should().Be(2);

        var paginacao = await new PaginacaoAtualizadaCheck(
                WordComplianceValidator.Core.Checklist.ChecklistPadrao.Cliente, "4.3.3 (letra f)")
            .RunAsync(ctx, TestContext.Current.CancellationToken);
        paginacao.Violations.Should().BeEmpty();
        paginacao.Note.Should().Contain("Total fixo 17 confere");

        var localizacao = await new LocalizacaoCodificacaoCheck().RunAsync(ctx, TestContext.Current.CancellationToken);
        localizacao.Violations.Should().NotContain(v => v.Message.Contains("folha de rosto"));

        var cliente = await new CodificacaoClienteCheck().RunAsync(ctx, TestContext.Current.CancellationToken);
        cliente.Violations.Should().BeEmpty();

        var numeracao = await new NumeracaoPaginasCheck(
                WordComplianceValidator.Core.Checklist.ChecklistPadrao.Pda, "4.3.6.5")
            .RunAsync(ctx, TestContext.Current.CancellationToken);
        numeracao.Violations.Should().NotContain(v => v.Message.Contains("folha de rosto"));
    }

    /// <summary>
    /// O sumário do modelo da Vale traz só os títulos; a relação de figuras e tabelas no índice
    /// (PS-002 4.3.4.4) não é exigida para ela — o profile desliga a regra.
    /// </summary>
    [Fact]
    public async Task Vale_nao_exige_figuras_e_tabelas_no_indice()
    {
        var ctx = ReferenceDocumentContext("RD-673-RL-65239-00.docx");

        var resultado = await new IndiceConteudoCheck(
                WordComplianceValidator.Core.Checklist.ChecklistPadrao.Pda, "4.3.4.4")
            .RunAsync(ctx, TestContext.Current.CancellationToken);

        resultado.Status.Should().Be(CheckStatus.Skipped);
        resultado.Violations.Should().BeEmpty();
        resultado.Note.Should().Contain("não exigida");
    }

    /// <summary>
    /// Depois da capa da Vale vem a página do índice: o título "ÍNDICE"/"SUMÁRIO" e uma tabela
    /// "ITEM | DESCRIÇÃO | PÁGINA" antes da primeira entrada. Essa tabela não é da capa — lida
    /// como formulário, a IA pedia para "preencher o campo PÁGINA".
    /// </summary>
    [Theory]
    [InlineData("RD-276-RL-68014-01.docx")]
    [InlineData("RD-673-RL-65239-00.docx")]
    [InlineData("RD-771-RL-67131-00.docx")]
    public void Folha_de_rosto_da_Vale_termina_antes_do_indice(string arquivo)
    {
        var ctx = ReferenceDocumentContext(arquivo);

        var capa = DocumentoTexto.FolhaDeRosto(ctx.Structure);

        capa.Where(p => p.TableIndex is not null).Select(p => p.TableIndex).Distinct().Should().Equal(0);
        capa.Should().NotContain(p => DocumentoTexto.Normalizar(p.Text) == "pagina");
    }

    /// <summary>
    /// Na capa da Vale as iniciais vêm às vezes em minúsculas ("rbc/LPB/ABF"): ainda são as
    /// mesmas iniciais do quadro.
    /// </summary>
    [Fact]
    public async Task Iniciais_minusculas_da_capa_Vale_conferem_com_o_quadro()
    {
        var ctx = ReferenceDocumentContext("RD-673-RL-65239-00.docx");

        var resultado = await new ConsistenciaIniciaisCheck().RunAsync(ctx, TestContext.Current.CancellationToken);

        resultado.Violations.Should().BeEmpty();
    }

    private static DocumentContext ReferenceDocumentContext(string fileName)
    {
        var path = RepoFile("templates", fileName);
        var structure = new DocxStructureExtractor().ExtractFromFile(path);
        // Documentos da Vale (RD-…) usam o profile real do repositório: o teste cobre também
        // o que está cadastrado para a equipe.
        var profile = fileName.StartsWith("RD-", StringComparison.Ordinal)
            ? ProfileVale.Value
            : new ClientProfile("MRN (exemplo)", "1.0", ProfileParameters);

        return new DocumentContext(path, structure, profile);
    }

    private static readonly Lazy<ClientProfile> ProfileVale = new(() =>
    {
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(RepoFile("profiles", "vale.json")));
        var parametros = json.RootElement.GetProperty("parameters").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        return new ClientProfile("Vale", "1.0", parametros);
    });

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
