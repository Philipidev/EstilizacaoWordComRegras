using AwesomeAssertions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Profile;
using WordComplianceValidator.Infrastructure.Checks;

namespace WordComplianceValidator.Infrastructure.Tests.Checks;

/// <summary>
/// Quadro "Características do Documento" no modelo novo (FR-115-01): duas tabelas
/// consecutivas, grade com o rótulo vertical "PÁGINAS" e cabeçalho do histórico em duas linhas
/// com células mescladas. E a conferência do total de páginas digitado (sem NUMPAGES), que não
/// conta as páginas do quadro.
/// </summary>
public class QuadroModeloFr115Tests
{
    private static ExtractedTableCell C(int linha, int coluna, string texto, int grade = -1, int span = 1) =>
        new(linha, coluna, texto) { GridColumn = grade < 0 ? coluna : grade, GridSpan = span };

    /// <summary>Tabela 0: título e codificações.</summary>
    private static ExtractedTable TabelaDoTitulo() => new(0,
    [
        C(0, 0, ""), C(0, 1, "QUADRO CARACTERÍSTICAS DO DOCUMENTO"),
        C(1, 0, "TÍTULO DO DOCUMENTO"),
        C(2, 0, "Título do Documento: UP 26 - Sistema de Rejeitos"),
        C(3, 0, "CODIFICAÇÃO PDA"), C(3, 1, "CODIFICAÇÃO CLIENTE"),
        C(4, 0, "RN-778-ET-67440-00"), C(4, 1, "QD5-PDA-26-04-016-SP-1"),
    ], " | QUADRO CARACTERÍSTICAS DO DOCUMENTO");

    /// <summary>
    /// Tabela 1: apêndices, grade (páginas 1–4, marcadas até a 3) e histórico com cabeçalho em
    /// duas linhas: "Iniciais" cobre três colunas da grade na linha de cima.
    /// </summary>
    private static ExtractedTable TabelaDoHistorico(int index = 1) => new(index,
    [
        C(0, 0, "RELAÇÃO DE APÊNDICES E ANEXOS"),
        C(1, 0, "REF. PDA"), C(1, 1, "REF. CLIENTE"), C(1, 2, "DESCRIÇÃO"),
        C(2, 0, "ADENDO I"), C(2, 1, "-"), C(2, 2, "Informações da MRN"),
        C(3, 0, "Rev."), C(3, 1, "0"), C(3, 2, "0"), C(3, 3, "Rev."), C(3, 4, "0"), C(3, 5, "0"),
        C(4, 0, "Rev."), C(4, 1, "A"), C(4, 2, "0"), C(4, 3, "Rev."), C(4, 4, "A"), C(4, 5, "0"),
        C(5, 0, "P\nÁ\nG\nI\nN\nA\nS"), C(5, 1, "1"), C(5, 2, "X"), C(5, 3, "X"),
        C(5, 4, "P\nÁ\nG\nI\nN\nA\nS"), C(5, 5, "2"), C(5, 6, "X"), C(5, 7, "X"),
        C(6, 0, ""), C(6, 1, "3"), C(6, 2, ""), C(6, 3, "X"),
        C(6, 4, ""), C(6, 5, "4"), C(6, 6, ""), C(6, 7, ""),
        C(7, 0, "Histórico de\nRevisões", 0), C(7, 1, "Data da Emissão", 1),
        C(7, 2, "Iniciais", 2, span: 3), C(7, 3, "Descrição das Revisões", 5),
        C(8, 0, ""), C(8, 1, ""), C(8, 2, "Elaborador"), C(8, 3, "V. Técnico"), C(8, 4, "Aprovador"), C(8, 5, ""),
        C(9, 0, "0A"), C(9, 1, "30/05/26"), C(9, 2, "YHO/JRA"), C(9, 3, "AW/LBN"), C(9, 4, "MM"), C(9, 5, "Emissão inicial."),
        C(10, 0, "00"), C(10, 1, "19/06/26"), C(10, 2, "YHO/VCS"), C(10, 3, "LNS/LLJ"), C(10, 4, "MM"), C(10, 5, "Emissão para construção."),
        C(11, 0, "Nome do Aprovador"), C(11, 1, "Assinatura do Aprovador"),
        C(12, 0, "André Otávio Moreira e Silva"), C(12, 1, ""),
    ], "RELAÇÃO DE APÊNDICES E ANEXOS");

    private static DocumentStructure Documento(
        IReadOnlyList<ExtractedTable> tabelas,
        IReadOnlyList<ExtractedParagraph>? paragrafos = null,
        string? cabecalho = null,
        int? paginasDoWord = null) =>
        new(
            FileName: "fixture.docx",
            Styles: new Dictionary<string, ExtractedStyle>(),
            Headers: cabecalho is null ? [] : [new ExtractedHeaderFooter("default", cabecalho, 0, ["PAGE"], [], 0)],
            Footers: [],
            Sections: [new ExtractedSection(0, null, null, null, null, null, null)],
            Paragraphs: paragrafos ?? [],
            Tables: tabelas,
            HasPendingTrackChanges: false, HasOpenComments: false, HasUpdatedToc: true,
            Properties: new ExtractedDocumentProperties(null, null, null, null, null, null, null) { Pages = paginasDoWord });

    private static DocumentContext Ctx(DocumentStructure doc) =>
        new("fixture.docx", doc, new ClientProfile("Teste", "1.0", new Dictionary<string, string>()));

    private static readonly string[] Aliases =
        ["Características do Documento", "Quadro de Características", "Características"];

    [Fact]
    public void Quadro_em_duas_tabelas_e_lido_como_um_so()
    {
        var quadro = QuadroCaracteristicas.Find(Documento([TabelaDoTitulo(), TabelaDoHistorico()]), Aliases);

        quadro.Should().NotBeNull();
        quadro!.Partes.Should().Equal(0, 1);
        quadro.Contem(1).Should().BeTrue();
        QuadroCaracteristicas.RevisaoVigente(quadro).Should().Be("00");

        var vigente = QuadroCaracteristicas.HistoricoDeRevisoes(quadro)[^1];
        vigente.Data.Should().Be("19/06/26");
        vigente.Coluna(["Elaborador"]).Should().Be("YHO/VCS");
        vigente.Coluna(["V. Técnico"]).Should().Be("LNS/LLJ");
        vigente.Coluna(["Aprovador"]).Should().Be("MM");
        vigente.Descricao.Should().Be("Emissão para construção.");
    }

    [Fact]
    public void Grade_com_rotulo_vertical_PAGINAS_e_reconhecida()
    {
        var quadro = QuadroCaracteristicas.Find(Documento([TabelaDoTitulo(), TabelaDoHistorico()]), Aliases)!;

        // Página 4 existe impressa, mas sem "x": slot vazio do formulário.
        QuadroCaracteristicas.MaiorFolhaMarcadaNaGrade(quadro).Should().Be(3);
    }

    [Fact]
    public async Task Campos_obrigatorios_do_modelo_novo_sao_encontrados()
    {
        var ctx = Ctx(Documento([TabelaDoTitulo(), TabelaDoHistorico()]));

        var resultado = await new QuadroCaracteristicasPreenchidoCheck()
            .RunAsync(ctx, TestContext.Current.CancellationToken);

        resultado.Violations.Should().BeEmpty();
        resultado.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public void Tabelas_separadas_por_titulo_de_secao_nao_sao_juntadas()
    {
        var paragrafos = new List<ExtractedParagraph>
        {
            new("p0", null, "QUADRO CARACTERÍSTICAS DO DOCUMENTO", IsInTable: true, TableIndex: 0),
            new("p1", null, "5 CONCLUSÕES", OutlineLevel: 0),
            new("p2", null, "RELAÇÃO DE APÊNDICES E ANEXOS", IsInTable: true, TableIndex: 1),
        };

        var quadro = QuadroCaracteristicas.Find(
            Documento([TabelaDoTitulo(), TabelaDoHistorico()], paragrafos), Aliases);

        quadro!.Partes.Should().Equal(0);
    }

    /// <summary>
    /// Quadro com uma quebra de página renderizada no meio da tabela do histórico: ocupa duas
    /// páginas.
    /// </summary>
    private static List<ExtractedParagraph> ParagrafosDoQuadroEmDuasPaginas() =>
    [
        new("p0", null, "QUADRO CARACTERÍSTICAS DO DOCUMENTO", IsInTable: true, TableIndex: 0),
        new("p1", null, "", TableIndex: null),
        new("p2", null, "RELAÇÃO DE APÊNDICES E ANEXOS", IsInTable: true, TableIndex: 1),
        new("p3", null, "Histórico de Revisões", IsInTable: true, TableIndex: 1) { RenderedPageBreaks = 1 },
    ];

    [Fact]
    public void Paginas_do_quadro_contam_as_quebras_renderizadas()
    {
        var doc = Documento([TabelaDoTitulo(), TabelaDoHistorico()], ParagrafosDoQuadroEmDuasPaginas());
        var quadro = QuadroCaracteristicas.Find(doc, Aliases)!;

        QuadroCaracteristicas.PaginasDoQuadro(doc, quadro).Should().Be(2);
    }

    private static Task<RuleCheckResult> Paginacao(DocumentStructure doc) =>
        new PaginacaoAtualizadaCheck(ChecklistPadrao.Cliente, "4.3.3 (letra f)")
            .RunAsync(Ctx(doc), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Total_digitado_que_confere_com_a_contagem_do_Word_sem_o_quadro_passa()
    {
        // Word: 5 páginas, 2 do quadro, logo 3 numeradas. A grade marca até a 3.
        var doc = Documento([TabelaDoTitulo(), TabelaDoHistorico()], ParagrafosDoQuadroEmDuasPaginas(),
            cabecalho: "FL.: 1/3", paginasDoWord: 5);

        var resultado = await Paginacao(doc);

        resultado.Status.Should().Be(CheckStatus.Passed);
        resultado.Violations.Should().BeEmpty();
        resultado.Note.Should().Contain("confere").And.Contain("2 do quadro");
    }

    [Fact]
    public async Task Total_digitado_divergente_da_contagem_e_da_grade_concordantes_e_erro()
    {
        // Word: 5 páginas − 2 do quadro = 3, e a grade também marca até a 3. O cabeçalho diz 4.
        var doc = Documento([TabelaDoTitulo(), TabelaDoHistorico()], ParagrafosDoQuadroEmDuasPaginas(),
            cabecalho: "FL.: 1/4", paginasDoWord: 5);

        var resultado = await Paginacao(doc);

        resultado.Status.Should().Be(CheckStatus.Failed);
        resultado.Violations.Should().ContainSingle(v => v.Severity == Severity.Error
                                                      && v.Message.Contains("(4) não confere"));
    }

    [Fact]
    public async Task Total_que_bate_com_a_grade_mas_nao_com_a_contagem_do_Word_e_aviso()
    {
        // A grade e o cabeçalho dizem 3; o app.xml diz 7 − 2 = 5 (pode estar defasado).
        var doc = Documento([TabelaDoTitulo(), TabelaDoHistorico()], ParagrafosDoQuadroEmDuasPaginas(),
            cabecalho: "FL.: 1/3", paginasDoWord: 7);

        var resultado = await Paginacao(doc);

        resultado.Violations.Should().ContainSingle(v => v.Severity == Severity.Warning
                                                      && v.Message.Contains("(3) não confere"));
        resultado.Violations.Should().NotContain(v => v.Severity == Severity.Error);
    }

    [Fact]
    public async Task Total_digitado_sem_como_conferir_pede_conferencia_manual()
    {
        // Sem app.xml nem quadro: aviso para conferir à mão, nunca erro.
        var doc = Documento([], cabecalho: "FL.: 1/10");

        var resultado = await Paginacao(doc);

        resultado.Violations.Should().ContainSingle(v => v.Severity == Severity.Warning
                                                      && v.Message.Contains("não pôde ser conferido"));
    }

    [Fact]
    public void Formulario_com_historico_em_branco_ainda_e_juntado()
    {
        // Rascunho: cabeçalho do histórico presente, nenhuma revisão preenchida.
        var historico = TabelaDoHistorico();
        var semRevisoes = historico with
        {
            Cells = historico.Cells.Where(c => c.Row is not (9 or 10)).ToList()
        };

        var quadro = QuadroCaracteristicas.Find(Documento([TabelaDoTitulo(), semRevisoes]), Aliases);

        quadro!.Partes.Should().Equal(0, 1);
        QuadroCaracteristicas.MaiorFolhaMarcadaNaGrade(quadro).Should().Be(3);
    }

    [Fact]
    public void Quadro_antigo_com_historico_vazio_nao_e_colado_na_tabela_seguinte()
    {
        // Modelo antigo: o cabeçalho do histórico já está na própria tabela.
        var antigo = new ExtractedTable(0,
        [
            C(0, 0, "CARACTERÍSTICAS DO DOCUMENTO"),
            C(1, 0, "Rev."), C(1, 1, "Data"), C(1, 2, "Emissor"), C(1, 3, "Descrição"),
        ], "CARACTERÍSTICAS DO DOCUMENTO");

        var quadro = QuadroCaracteristicas.Find(Documento([antigo, TabelaDoHistorico()]), Aliases);

        quadro!.Partes.Should().Equal(0);
    }

    [Fact]
    public async Task Texto_guia_Preencher_nao_conta_como_valor()
    {
        var historico = TabelaDoHistorico();
        var comGuia = historico with
        {
            Cells = historico.Cells.Select(c => c.Row == 10 && c.Column == 2 ? c with { Text = "Preencher" } : c).ToList()
        };

        var resultado = await new QuadroCaracteristicasPreenchidoCheck()
            .RunAsync(Ctx(Documento([TabelaDoTitulo(), comGuia])), TestContext.Current.CancellationToken);

        resultado.Violations.Should().ContainSingle(v => v.Message.Contains("'Elaborado por'"));
    }

    [Fact]
    public async Task Nota_informa_modelo_novo()
    {
        var resultado = await new QuadroCaracteristicasPreenchidoCheck()
            .RunAsync(Ctx(Documento([TabelaDoTitulo(), TabelaDoHistorico()])), TestContext.Current.CancellationToken);

        resultado.Note.Should().Contain("Modelo do quadro: novo (FR-115-01");
    }

    [Fact]
    public async Task Nota_informa_modelo_antigo()
    {
        var antigo = new ExtractedTable(0,
        [
            C(0, 0, "CARACTERÍSTICAS DO DOCUMENTO"),
            C(1, 0, "Rev."), C(1, 1, "Data"), C(1, 2, "Emissor"), C(1, 3, "Descrição"),
        ], "CARACTERÍSTICAS DO DOCUMENTO");

        var resultado = await new QuadroCaracteristicasPreenchidoCheck()
            .RunAsync(Ctx(Documento([antigo])), TestContext.Current.CancellationToken);

        resultado.Note.Should().Contain("Modelo do quadro: antigo (tabela única)");
    }
}
