using FluentAssertions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Profile;
using WordComplianceValidator.Infrastructure.Checks;
using WordComplianceValidator.Infrastructure.Tests.Fixtures;

namespace WordComplianceValidator.Infrastructure.Tests.Checks;

/// <summary>
/// Regressões da revisão do RN-816: três defeitos que produziam não conformidades inexistentes
/// (ou apontadas no lugar errado) sem que nenhum check estivesse errado — o que chegava ao
/// avaliador é que já vinha corrompido.
/// </summary>
public class EvidenciaEAncoraTests
{
    private sealed class RespostaFixa : ISemanticChecker
    {
        private readonly SemanticEvaluation _resposta;
        public RespostaFixa(SemanticEvaluation resposta) => _resposta = resposta;

        public Task<SemanticVerdict> EvaluateAsync(string i, string c, CancellationToken ct = default) =>
            Task.FromResult(new SemanticVerdict(true, "ok"));

        public Task<SemanticEvaluation> AvaliarAsync(
            string instrucao, string conteudo, string? modelo = null, CancellationToken ct = default) =>
            Task.FromResult(_resposta);
    }

    private static ChecklistEntry Entrada() =>
        new(new ChecklistRef("PS-002", "4.2", ChecklistPadrao.Cliente),
            31, "Edição de Documentos Técnicos", "Corpo", "Ortografia e Gramática",
            "Verificar correção gramatical.", IaAutomatizavel: false, Observacao: null);

    [Fact]
    public void Entrada_de_indice_preserva_a_separacao_entre_numeracao_titulo_e_pagina()
    {
        var doc = DocxFixtureBuilder.Novo()
            .EntradaIndiceComTabs("10.4", "Avaliação granulométrica do rachão", "67")
            .Extrair();

        var entrada = doc.Paragraphs.Single(p => p.Text.Contains("granulométrica"));

        // Antes: "10.4Avaliação granulométrica do rachão67" — o InnerText descartava os tabs
        // e gerava acusações de falta de espaço e de número colado ao título.
        entrada.Text.Should().NotContain("10.4Avaliação");
        entrada.Text.Should().NotContain("rachão67");
        DocumentoTexto.Normalizar(entrada.Text)
            .Should().Contain("avaliacao granulometrica do rachao");
    }

    [Fact]
    public void Folha_de_rosto_expoe_a_tabela_da_capa_linha_a_linha()
    {
        var doc = DocxFixtureBuilder.Novo()
            .TabelaGrade(
                ["REV.", "EMISSÃO", "DATA", "DESCRIÇÃO DAS REVISÕES"],
                ["0", "B", "11/03/26", "Documento para comentários do cliente."],
                ["1", "F", "26/03/26", "Conforme construído."],
                ["", "", "", ""])
            .Titulo("INTRODUÇÃO", 0)
            .Paragrafo("Texto do corpo.")
            .Extrair();

        var ctx = new DocumentContext("fixture.docx", doc,
            new ClientProfile("Teste", "1.0", new Dictionary<string, string>()));
        var evidencia = new DefaultEvidenceSelector().Build(ctx, Entrada());

        // A linha precisa chegar inteira: é o que impede ler "REV. 0" + "EMISSÃO B" como um
        // código de revisão "0/B" e acusar divergência com o quadro "Características".
        evidencia.Should().Contain("REV. | EMISSÃO | DATA | DESCRIÇÃO DAS REVISÕES");
        evidencia.Should().Contain("0 | B | 11/03/26 | Documento para comentários do cliente.");
        evidencia.Should().Contain("1 | F | 26/03/26 | Conforme construído.");
    }

    [Fact]
    public void Ancora_ignora_celulas_curtas_e_cai_no_paragrafo_citado()
    {
        // A capa contribui com células de um caractere; o parágrafo real do problema vem depois.
        var doc = DocxFixtureBuilder.Novo()
            .TabelaGrade(["REV.", "EMISSÃO"], ["0", "B"], ["1", "F"])
            .Titulo("ANEXOS", 0)
            .Paragrafo("ANEXO II – E-mail Relevantes")
            .Extrair();

        var ctx = new DocumentContext("fixture.docx", doc,
            new ClientProfile("Teste", "1.0", new Dictionary<string, string>()));

        var avaliacao = new SemanticEvaluation(
            SemanticStatus.NaoConforme,
            [new SemanticFinding(Severity.Error, "Deve ser \"E-mails Relevantes\".",
                "ANEXO II – E-mail Relevantes")],
            "Concordância nominal.");

        var check = new SemanticChecklistCheck(
            Entrada(), new RespostaFixa(avaliacao), new DefaultEvidenceSelector());
        var resultado = check.RunAsync(ctx).GetAwaiter().GetResult();

        var alvo = doc.Paragraphs.Single(p => p.Text.Contains("E-mail Relevantes"));
        var celulaCurta = doc.Paragraphs.First(p => p.Text == "0");

        var local = resultado.Violations.Single().Location;
        local!.ParagraphId.Should().Be(alvo.ParagraphId);
        local.ParagraphId.Should().NotBe(celulaCurta.ParagraphId);
    }

    [Fact]
    public void Ancora_prefere_igualdade_exata_a_substring_que_aparece_antes()
    {
        // "Aprovado" (resultado de ensaio) é substring de "assinatura do aprovador" e vem
        // muito antes no documento — sem ordem de preferência, o comentário ancorava nele.
        var doc = DocxFixtureBuilder.Novo()
            .Titulo("CONTROLE TECNOLÓGICO", 0)
            .Paragrafo("Aprovado")
            .Paragrafo("Assinatura do Aprovador")
            .Extrair();

        var ctx = new DocumentContext("fixture.docx", doc,
            new ClientProfile("Teste", "1.0", new Dictionary<string, string>()));

        var avaliacao = new SemanticEvaluation(
            SemanticStatus.NaoConforme,
            [new SemanticFinding(Severity.Error, "Campo em branco.", "Assinatura do Aprovador")],
            "Quadro incompleto.");

        var check = new SemanticChecklistCheck(
            Entrada(), new RespostaFixa(avaliacao), new DefaultEvidenceSelector());
        var resultado = check.RunAsync(ctx).GetAwaiter().GetResult();

        var esperado = doc.Paragraphs.Single(p => p.Text == "Assinatura do Aprovador");
        resultado.Violations.Single().Location!.ParagraphId.Should().Be(esperado.ParagraphId);
    }

    [Fact]
    public void Campo_preenchido_por_imagem_nao_chega_a_evidencia_como_vazio()
    {
        // Na folha de rosto, "CONTRATADA" é preenchido com o logo — texto vazio, campo cheio.
        // A estrutura vai montada direto no modelo: sintetizar DrawingML valido num
        // .docx de fixture custa mais do que este teste precisa provar, que e a renderizacao.
        var capa = new ExtractedTable(0, [
            new ExtractedTableCell(0, 0, "CONTRATANTE"),
            new ExtractedTableCell(0, 1, "MRN"),
            new ExtractedTableCell(1, 0, "CONTRATADA"),
            new ExtractedTableCell(1, 1, "", ImageCount: 1)
        ], "CONTRATANTE | MRN");

        var doc = new DocumentStructure(
            FileName: "fixture.docx",
            Styles: new Dictionary<string, ExtractedStyle>(),
            Headers: [], Footers: [],
            Sections: [new ExtractedSection(0, null, null, null, null, null, null)],
            Paragraphs: [
                new ExtractedParagraph("p0", null, "CONTRATANTE", IsInTable: true, TableIndex: 0),
                new ExtractedParagraph("p1", null, "MRN", IsInTable: true, TableIndex: 0),
                new ExtractedParagraph("p2", null, "CONTRATADA", IsInTable: true, TableIndex: 0),
                new ExtractedParagraph("p3", null, "", IsInTable: true, TableIndex: 0, ImageCount: 1),
                new ExtractedParagraph("p4", "Heading1", "INTRODUCAO", OutlineLevel: 0)
            ],
            Tables: [capa],
            HasPendingTrackChanges: false, HasOpenComments: false, HasUpdatedToc: true);

        var ctx = new DocumentContext("fixture.docx", doc,
            new ClientProfile("Teste", "1.0", new Dictionary<string, string>()));
        var evidencia = new DefaultEvidenceSelector().Build(ctx, Entrada());

        evidencia.Should().Contain("CONTRATADA | [imagem]");
        evidencia.Should().NotContain("CONTRATADA | \n");
    }

    [Fact]
    public void Folha_de_rosto_alcanca_os_campos_que_vem_depois_da_tabela_da_capa()
    {
        // A folha indice do cliente ocupa centenas de celulas antes da folha de rosto real.
        // Contar celula como paragrafo esgotava a cota e cortava fora codigo/titulo/cliente.
        var linhas = Enumerable.Range(0, 60)
            .Select(i => new[] { i.ToString(), "x", "11/03/26", "linha de controle" })
            .ToArray();

        var doc = DocxFixtureBuilder.Novo()
            .TabelaGrade(linhas)
            .Paragrafo("CONTRATADA")
            .Paragrafo("QD5-PDA-26-04-095-RT")
            .Titulo("INTRODUCAO", 0)
            .Paragrafo("Texto do corpo.")
            .Extrair();

        var ctx = new DocumentContext("fixture.docx", doc,
            new ClientProfile("Teste", "1.0", new Dictionary<string, string>()));
        var evidencia = new DefaultEvidenceSelector().Build(ctx, Entrada());

        evidencia.Should().Contain("QD5-PDA-26-04-095-RT",
            "a codificacao vem depois da tabela da capa e precisa chegar ao avaliador");
    }

    [Fact]
    public void Quadro_sem_campo_de_pagina_na_secao_nao_acusa_numeracao()
    {
        var ctx = ContextoDoQuadro(cabecalhoComCampoPagina: false);
        var check = new QuadroCaracteristicasPreenchidoCheck(ChecklistPadrao.Pda, "4.7");

        var resultado = check.RunAsync(ctx).GetAwaiter().GetResult();

        resultado.Violations.Should().NotContain(v => v.Message.Contains("numeracao de paginas")
                                                   || v.Message.Contains("numeração de páginas"));
    }

    [Fact]
    public void Quadro_com_campo_de_pagina_na_secao_acusa_numeracao()
    {
        var ctx = ContextoDoQuadro(cabecalhoComCampoPagina: true);
        var check = new QuadroCaracteristicasPreenchidoCheck(ChecklistPadrao.Pda, "4.7");

        var resultado = check.RunAsync(ctx).GetAwaiter().GetResult();

        resultado.Violations.Should().Contain(v => v.Message.Contains("numeração de páginas"));
    }

    /// <summary>
    /// Quadro na secao 1, com um unico campo obrigatorio ja preenchido, para isolar a
    /// verificacao de numeracao das verificacoes de preenchimento.
    /// </summary>
    private static DocumentContext ContextoDoQuadro(bool cabecalhoComCampoPagina)
    {
        var quadro = new ExtractedTable(0, [
            new ExtractedTableCell(0, 0, "CARACTERÍSTICAS DO DOCUMENTO"),
            new ExtractedTableCell(0, 1, ""),
            new ExtractedTableCell(1, 0, "Revisão"),
            new ExtractedTableCell(1, 1, "00")
        ], "CARACTERÍSTICAS DO DOCUMENTO");

        var campos = cabecalhoComCampoPagina ? new[] { "PAGE" } : Array.Empty<string>();

        var doc = new DocumentStructure(
            FileName: "fixture.docx",
            Styles: new Dictionary<string, ExtractedStyle>(),
            Headers: [new ExtractedHeaderFooter("default", "cabeçalho", 0, campos, [], 1)],
            Footers: [],
            Sections: [
                new ExtractedSection(0, null, null, null, null, null, null),
                new ExtractedSection(1, null, null, null, null, null, null)
            ],
            Paragraphs: [
                new ExtractedParagraph("p0", "Heading1", "CONCLUSÃO", OutlineLevel: 0),
                new ExtractedParagraph("p1", null, "CARACTERÍSTICAS DO DOCUMENTO",
                    SectionIndex: 1, IsInTable: true, TableIndex: 0),
                new ExtractedParagraph("p2", null, "00",
                    SectionIndex: 1, IsInTable: true, TableIndex: 0)
            ],
            Tables: [quadro],
            HasPendingTrackChanges: false, HasOpenComments: false, HasUpdatedToc: true);

        return new DocumentContext("fixture.docx", doc, new ClientProfile("Teste", "1.0",
            new Dictionary<string, string> { ["quadro.camposObrigatorios"] = "Revisão" }));
    }

    [Fact]
    public void Quadro_preserva_as_colunas_PdA_e_Cliente_e_resume_a_grade_de_folhas()
    {
        var celulas = new List<ExtractedTableCell>
        {
            new(0, 0, "CARACTERÍSTICAS DO DOCUMENTO"),
            new(1, 0, "Pimenta de Ávila"),
            new(1, 1, "Cliente"),
            new(2, 0, "RN-816-RL-67456-00"),
            new(2, 1, "QD5-PDA-26-04-095-RT-1")
        };
        // Grade de controle de folhas: so numeros e marcacoes "x".
        for (var folha = 1; folha <= 12; folha++)
        {
            celulas.Add(new ExtractedTableCell(2 + folha, 0, folha.ToString()));
            celulas.Add(new ExtractedTableCell(2 + folha, 1, "x"));
            celulas.Add(new ExtractedTableCell(2 + folha, 2, ""));
            celulas.Add(new ExtractedTableCell(2 + folha, 3, "x"));
        }
        celulas.Add(new ExtractedTableCell(20, 0, "00"));
        celulas.Add(new ExtractedTableCell(20, 1, "26/03/26"));
        celulas.Add(new ExtractedTableCell(20, 2, "Conforme construído."));

        var quadro = new ExtractedTable(0, celulas, "CARACTERÍSTICAS DO DOCUMENTO");

        var doc = new DocumentStructure(
            FileName: "fixture.docx",
            Styles: new Dictionary<string, ExtractedStyle>(),
            Headers: [], Footers: [],
            Sections: [new ExtractedSection(0, null, null, null, null, null, null)],
            Paragraphs: [
                new ExtractedParagraph("p0", "Heading1", "CONCLUSÃO", OutlineLevel: 0),
                new ExtractedParagraph("p1", null, "CARACTERÍSTICAS DO DOCUMENTO",
                    IsInTable: true, TableIndex: 0)
            ],
            Tables: [quadro],
            HasPendingTrackChanges: false, HasOpenComments: false, HasUpdatedToc: true);

        var ctx = new DocumentContext("fixture.docx", doc,
            new ClientProfile("Teste", "1.0", new Dictionary<string, string>()));
        var evidencia = new DefaultEvidenceSelector().Build(ctx, Entrada());

        // O par de codigos precisa chegar como duas colunas: achatado, o "-1" do codigo do
        // Cliente era lido como revisao PdA e acusado de divergir do historico "00".
        evidencia.Should().Contain("Pimenta de Ávila | Cliente");
        evidencia.Should().Contain("RN-816-RL-67456-00 | QD5-PDA-26-04-095-RT-1");

        // A grade sai resumida, e o resumo diz o que ela e — para nao virar "numeracao da pagina".
        evidencia.Should().Contain("grade de controle de folhas: 12 linhas, folhas 1 a 12");
        evidencia.Should().NotContain("- 7 | x |");

        // O historico continua inteiro.
        evidencia.Should().Contain("00 | 26/03/26 | Conforme construído.");
    }

    [Fact]
    public void Paragrafos_de_uma_mesma_celula_nao_saem_colados()
    {
        // A celula de titulo da folha de rosto tem "…BARRAGEM A1" e "ENGENHARIA" como dois
        // paragrafos; concatenados sem separador viravam uma aglutinacao inexistente.
        var doc = DocxFixtureBuilder.Novo()
            .CelulaComVariosParagrafos("REJ0140 - DRENAGEM DA BARRAGEM A1", "ENGENHARIA")
            .Titulo("INTRODUCAO", 0)
            .Extrair();

        var celula = doc.Tables.Single().Cells.First();
        celula.Text.Should().NotContain("A1ENGENHARIA");
        DocumentoTexto.Normalizar(celula.Text).Should().Contain("barragem a1 engenharia");
    }

    [Fact]
    public void Cabecalho_da_grade_de_folhas_e_absorvido_pelo_resumo()
    {
        var celulas = new List<ExtractedTableCell>
        {
            new(0, 0, "CARACTERÍSTICAS DO DOCUMENTO"),
            // Cabecalhos da grade: "Rev." com codigos de revisao, "Pag." com celulas vazias.
            new(1, 0, "Rev."), new(1, 1, "0A"), new(1, 2, "0B"), new(1, 3, "00"),
            new(2, 0, "Pag."), new(2, 1, ""), new(2, 2, ""), new(2, 3, "")
        };
        for (var folha = 1; folha <= 5; folha++)
        {
            celulas.Add(new ExtractedTableCell(2 + folha, 0, folha.ToString()));
            celulas.Add(new ExtractedTableCell(2 + folha, 1, "x"));
            celulas.Add(new ExtractedTableCell(2 + folha, 2, ""));
            celulas.Add(new ExtractedTableCell(2 + folha, 3, "x"));
        }
        // Cabecalho do historico: mesma primeira celula "Rev.", mas demais sao palavras.
        celulas.Add(new ExtractedTableCell(9, 0, "Rev."));
        celulas.Add(new ExtractedTableCell(9, 1, "Data"));
        celulas.Add(new ExtractedTableCell(9, 2, "Emissor"));
        celulas.Add(new ExtractedTableCell(9, 3, "Descrição das Revisões"));

        var evidencia = EvidenciaDoQuadro(new ExtractedTable(0, celulas, "CARACTERÍSTICAS DO DOCUMENTO"));

        // "Pag." com celulas vazias parecia formulario em branco enquanto os dados eram resumidos.
        evidencia.Should().NotContain("Pag. |");
        evidencia.Should().Contain("grade de controle de folhas");
        // O cabecalho do historico nao pode ser absorvido junto.
        evidencia.Should().Contain("Rev. | Data | Emissor | Descrição das Revisões");
    }

    private static string EvidenciaDoQuadro(ExtractedTable quadro)
    {
        var doc = new DocumentStructure(
            FileName: "fixture.docx",
            Styles: new Dictionary<string, ExtractedStyle>(),
            Headers: [], Footers: [],
            Sections: [new ExtractedSection(0, null, null, null, null, null, null)],
            Paragraphs: [
                new ExtractedParagraph("p0", "Heading1", "CONCLUSÃO", OutlineLevel: 0),
                new ExtractedParagraph("p1", null, "CARACTERÍSTICAS DO DOCUMENTO",
                    IsInTable: true, TableIndex: 0)
            ],
            Tables: [quadro],
            HasPendingTrackChanges: false, HasOpenComments: false, HasUpdatedToc: true);

        var ctx = new DocumentContext("fixture.docx", doc,
            new ClientProfile("Teste", "1.0", new Dictionary<string, string>()));
        return new DefaultEvidenceSelector().Build(ctx, Entrada());
    }

    [Fact]
    public void Total_de_paginas_literal_menor_que_a_grade_e_erro()
    {
        var celulas = new List<ExtractedTableCell>
        {
            new(0, 0, "CARACTERÍSTICAS DO DOCUMENTO")
        };
        // Folha 100 MARCADA: divergencia real com o total fixo "99".
        celulas.Add(new ExtractedTableCell(1, 0, "99"));
        celulas.Add(new ExtractedTableCell(1, 1, "x"));
        celulas.Add(new ExtractedTableCell(1, 2, "100"));
        celulas.Add(new ExtractedTableCell(1, 3, "x"));

        var doc = new DocumentStructure(
            FileName: "fixture.docx",
            Styles: new Dictionary<string, ExtractedStyle>(),
            // Cabecalho com campo PAGE e total "99" gravado como texto.
            Headers: [new ExtractedHeaderFooter("default", "FL.: 7/99", 0, ["PAGE"], [], 0)],
            Footers: [],
            Sections: [new ExtractedSection(0, null, null, null, null, null, null)],
            Paragraphs: [new ExtractedParagraph("p0", null, "CARACTERÍSTICAS DO DOCUMENTO",
                IsInTable: true, TableIndex: 0)],
            Tables: [new ExtractedTable(0, celulas, "CARACTERÍSTICAS DO DOCUMENTO")],
            HasPendingTrackChanges: false, HasOpenComments: false, HasUpdatedToc: true);

        var ctx = new DocumentContext("fixture.docx", doc,
            new ClientProfile("Teste", "1.0", new Dictionary<string, string>()));

        var resultado = new PaginacaoAtualizadaCheck(ChecklistPadrao.Pda, "4.3.3 (letra f)")
            .RunAsync(ctx).GetAwaiter().GetResult();

        resultado.Status.Should().Be(CheckStatus.Failed);
        resultado.Violations.Should().Contain(v => v.Message.Contains("folha 100")
                                                && v.Message.Contains("NUMPAGES"));
    }

    [Fact]
    public void Folha_alem_do_total_mas_sem_marcacao_e_slot_vazio_e_nao_erro()
    {
        // Na grade do RN-816 a folha 100 existe impressa mas nao tem "x" em revisao nenhuma:
        // e linha reservada do formulario, nao pagina do documento.
        var celulas = new List<ExtractedTableCell>
        {
            new(0, 0, "CARACTERÍSTICAS DO DOCUMENTO"),
            new(1, 0, "99"), new(1, 1, "x"), new(1, 2, "100"), new(1, 3, "")
        };

        var doc = new DocumentStructure(
            FileName: "fixture.docx",
            Styles: new Dictionary<string, ExtractedStyle>(),
            Headers: [new ExtractedHeaderFooter("default", "FL.: 7/99", 0, ["PAGE"], [], 0)],
            Footers: [],
            Sections: [new ExtractedSection(0, null, null, null, null, null, null)],
            Paragraphs: [new ExtractedParagraph("p0", null, "CARACTERÍSTICAS DO DOCUMENTO",
                IsInTable: true, TableIndex: 0)],
            Tables: [new ExtractedTable(0, celulas, "CARACTERÍSTICAS DO DOCUMENTO")],
            HasPendingTrackChanges: false, HasOpenComments: false, HasUpdatedToc: true);

        var ctx = new DocumentContext("fixture.docx", doc,
            new ClientProfile("Teste", "1.0", new Dictionary<string, string>()));

        var resultado = new PaginacaoAtualizadaCheck(ChecklistPadrao.Pda, "4.3.3 (letra f)")
            .RunAsync(ctx).GetAwaiter().GetResult();

        resultado.Status.Should().NotBe(CheckStatus.Failed);
        resultado.Violations.Should().NotContain(v => v.Severity == Severity.Error);
    }

    [Fact]
    public void Total_de_paginas_por_campo_NUMPAGES_nao_gera_achado()
    {
        var doc = new DocumentStructure(
            FileName: "fixture.docx",
            Styles: new Dictionary<string, ExtractedStyle>(),
            Headers: [new ExtractedHeaderFooter("default", "FL.: 7/99", 0, ["PAGE", "NUMPAGES"], [], 0)],
            Footers: [],
            Sections: [new ExtractedSection(0, null, null, null, null, null, null)],
            Paragraphs: [new ExtractedParagraph("p0", null, "Texto")],
            Tables: [],
            HasPendingTrackChanges: false, HasOpenComments: false, HasUpdatedToc: true);

        var ctx = new DocumentContext("fixture.docx", doc,
            new ClientProfile("Teste", "1.0", new Dictionary<string, string>()));

        var resultado = new PaginacaoAtualizadaCheck(ChecklistPadrao.Pda, "4.3.3 (letra f)")
            .RunAsync(ctx).GetAwaiter().GetResult();

        resultado.Violations.Should().BeEmpty();
    }
}
