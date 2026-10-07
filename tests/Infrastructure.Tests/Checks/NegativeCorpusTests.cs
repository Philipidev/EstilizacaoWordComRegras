using AwesomeAssertions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Profile;
using WordComplianceValidator.Infrastructure.Checks;
using WordComplianceValidator.Infrastructure.Tests.Fixtures;

namespace WordComplianceValidator.Infrastructure.Tests.Checks;

/// <summary>
/// Corpus negativo: cada teste constrói um documento com um defeito deliberado e exige que a
/// regra correspondente o detecte.
/// <para>
/// <see cref="RealDocumentCheckTests"/> prova o oposto — que documentos conformes não geram
/// alarme. As duas metades juntas medem precisão e recall; sozinha, qualquer uma delas pode
/// ser satisfeita por um check que nunca acusa nada (ou que acusa tudo).
/// </para>
/// </summary>
public class NegativeCorpusTests
{
    private static DocumentContext Contexto(
        DocumentStructure structure, params (string Chave, string Valor)[] parametros)
    {
        var mapa = new Dictionary<string, string>
        {
            ["quadroCaracteristicas.aliases"] = "Características do Documento|Características",
            ["revisao.aliasesRotulo"] = "Revisão|Rev.|Rev",
            ["corpo.fonte"] = "Times New Roman",
            ["corpo.tamanho"] = "12"
        };
        foreach (var (chave, valor) in parametros) mapa[chave] = valor;

        return new DocumentContext("fixture.docx", structure, new ClientProfile("Teste", "1.0", mapa));
    }

    [Fact]
    public async Task FormatacaoCorpo_detecta_fonte_e_tamanho_fora_do_padrao()
    {
        var builder = DocxFixtureBuilder.Novo().Titulo("INTRODUÇÃO", 0);
        for (var i = 0; i < 10; i++)
            builder.Paragrafo($"Parágrafo de corpo número {i} com texto suficiente.", fonte: "Arial", tamanho: 10);

        var result = await new FormatacaoCorpoCheck(ChecklistPadrao.Cliente, "4.3.6.2")
            .RunAsync(Contexto(builder.Extrair()), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Violations.Should().Contain(v => v.Message.Contains("Arial"));
        result.Violations.Should().Contain(v => v.Message.Contains("10"));
    }

    [Fact]
    public async Task FormatacaoCorpo_aceita_documento_no_padrao()
    {
        var builder = DocxFixtureBuilder.Novo().Titulo("INTRODUÇÃO", 0);
        for (var i = 0; i < 10; i++)
            builder.Paragrafo($"Parágrafo de corpo número {i} conforme o padrão.", fonte: "Times New Roman", tamanho: 12);

        var result = await new FormatacaoCorpoCheck(ChecklistPadrao.Cliente, "4.3.6.2")
            .RunAsync(Contexto(builder.Extrair()), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task PainelNavegacao_detecta_salto_de_hierarquia()
    {
        var structure = DocxFixtureBuilder.Novo()
            .Titulo("INTRODUÇÃO", 0)
            .Paragrafo("Texto introdutório.")
            .Titulo("Subitem de terceiro nível sem o segundo", 2)   // salto 1 → 3
            .Paragrafo("Mais texto.")
            .Extrair();

        var result = await new PainelNavegacaoCheck(ChecklistPadrao.Cliente, "4.3.5.2")
            .RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Violations.Should().Contain(v => v.Message.Contains("Salto de hierarquia"));
    }

    [Fact]
    public async Task PainelNavegacao_aceita_hierarquia_correta()
    {
        var structure = DocxFixtureBuilder.Novo()
            .Titulo("INTRODUÇÃO", 0)
            .Paragrafo("Texto.")
            .Titulo("Critérios", 1)
            .Paragrafo("Texto.")
            .Titulo("Premissas", 2)
            .Extrair();

        var result = await new PainelNavegacaoCheck(ChecklistPadrao.Cliente, "4.3.5.2")
            .RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task TarjaEmissao_detecta_revisao_0A_sem_tarja()
    {
        var structure = DocxFixtureBuilder.Novo()
            .Tabela("Características do Documento", ("Revisão", "0A"), ("Título", "Relatório técnico"))
            .Paragrafo("Documento sem qualquer tarja de emissão.")
            .Extrair();

        var result = await new TarjaEmissaoCheck(ChecklistPadrao.Cliente, "4.1.2")
            .RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Violations.Should().Contain(v => v.Severity == Severity.Error);
    }

    [Fact]
    public async Task TarjaEmissao_aceita_revisao_0A_com_tarja()
    {
        var structure = DocxFixtureBuilder.Novo()
            .Tabela("Características do Documento", ("Revisão", "0A"), ("Título", "Relatório técnico"))
            .Paragrafo("Emissão para Comentários do Cliente")
            .Extrair();

        var result = await new TarjaEmissaoCheck(ChecklistPadrao.Cliente, "4.1.2")
            .RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Passed);
    }

    /// <summary>
    /// O histórico descreve a emissão anterior com as mesmas palavras da tarja ("0A | data |
    /// … | Emissão para comentários do cliente"). Isso bastava para dar a tarja como presente.
    /// </summary>
    [Fact]
    public async Task TarjaEmissao_nao_aceita_descricao_do_historico_como_tarja()
    {
        var structure = DocxFixtureBuilder.Novo()
            .Tabela("Características do Documento", ("Revisão", "0B"))
            .TabelaGrade(
                ["Rev.", "Data", "Emissor", "Descrição das Revisões"],
                ["0A", "14/03/2025", "LBN", "Emissão para comentários do cliente."],
                ["0B", "11/04/2025", "LBN", "Emissão para comentários do cliente."])
            .Paragrafo("Corpo sem tarja.")
            .Extrair();

        var result = await new TarjaEmissaoCheck(ChecklistPadrao.Cliente, "4.1.2")
            .RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Failed);
    }

    [Fact]
    public async Task TarjaEmissao_aceita_tarja_em_marca_dagua()
    {
        var structure = DocxFixtureBuilder.Novo()
            .Tabela("Características do Documento", ("Revisão", "0A"))
            .Paragrafo("Corpo.")
            .MarcaDagua("EMISSÃO PARA COMENTÁRIOS DO CLIENTE")
            .Extrair();

        var result = await new TarjaEmissaoCheck(ChecklistPadrao.Cliente, "4.1.2")
            .RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task TarjaEmissao_reprova_marca_dagua_de_comentarios_visivel_apos_a_00()
    {
        var structure = DocxFixtureBuilder.Novo()
            .Tabela("Características do Documento", ("Revisão", "00"))
            .Paragrafo("Corpo.")
            .MarcaDagua("EMISSÃO PARA COMENTÁRIOS DO CLIENTE")
            .Extrair();

        var result = await new TarjaEmissaoCheck(ChecklistPadrao.Cliente, "4.1.2")
            .RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Violations.Should().ContainSingle(v => v.Severity == Severity.Error);
    }

    [Fact]
    public async Task TarjaEmissao_so_avisa_quando_a_tarja_esta_em_cabecalho_oculto()
    {
        var structure = DocxFixtureBuilder.Novo()
            .Tabela("Características do Documento", ("Revisão", "00"))
            .Paragrafo("Corpo.")
            .Cabecalho("CABEÇALHO PADRÃO")
            .MarcaDagua("EMISSÃO PARA COMENTÁRIOS DO CLIENTE", kind: "first")
            .PrimeiraPaginaDiferente(false)
            .Extrair();

        var result = await new TarjaEmissaoCheck(ChecklistPadrao.Cliente, "4.1.2")
            .RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Skipped);
        result.Violations.Should().ContainSingle(v => v.Severity == Severity.Warning);
    }

    private const string Titulo1 =
        "<w:p><w:pPr><w:pStyle w:val=\"PDA-T1\"/><w:outlineLvl w:val=\"0\"/></w:pPr><w:r><w:t>RESULTADOS</w:t></w:r></w:p>";

    /// <summary>Estilo próprio do cliente ("PDA-T1") com quebra depois do texto do título.</summary>
    [Fact]
    public async Task ContinuidadeTitulo_detecta_quebra_depois_do_titulo_em_estilo_proprio()
    {
        var structure = DocxFixtureBuilder.Novo()
            .ParagrafoXml("<w:p><w:pPr><w:pStyle w:val=\"PDA-T1\"/><w:outlineLvl w:val=\"0\"/></w:pPr>" +
                          "<w:r><w:t>RESULTADOS</w:t></w:r><w:r><w:br w:type=\"page\"/></w:r></w:p>")
            .Paragrafo("Os resultados obtidos indicam…")
            .Extrair();

        var result = await new ContinuidadeTituloConteudoCheck().RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Failed);
    }

    /// <summary>Na última paginação o conteúdo abriu página nova e o título ficou sozinho no pé.</summary>
    [Fact]
    public async Task ContinuidadeTitulo_avisa_titulo_orfao_na_paginacao_do_Word()
    {
        var structure = DocxFixtureBuilder.Novo()
            .ParagrafoXml(Titulo1)
            .ParagrafoXml("<w:p><w:r><w:lastRenderedPageBreak/><w:t>Os resultados obtidos indicam…</w:t></w:r></w:p>")
            .Extrair();

        var result = await new ContinuidadeTituloConteudoCheck().RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Violations.Should().ContainSingle(v => v.Severity == Severity.Warning);
    }

    /// <summary>
    /// Capítulo que abre página nova (quebra antes do texto) é o caso normal — antes era lido
    /// como "termina com quebra" e reprovado.
    /// </summary>
    [Fact]
    public async Task ContinuidadeTitulo_aceita_titulo_que_abre_pagina_nova()
    {
        var structure = DocxFixtureBuilder.Novo()
            .Paragrafo("Fim do capítulo anterior.")
            .ParagrafoXml("<w:p><w:pPr><w:pStyle w:val=\"PDA-T1\"/><w:outlineLvl w:val=\"0\"/></w:pPr>" +
                          "<w:r><w:br w:type=\"page\"/><w:lastRenderedPageBreak/><w:t>RESULTADOS</w:t></w:r></w:p>")
            .Paragrafo("Os resultados obtidos indicam…")
            .Extrair();

        var result = await new ContinuidadeTituloConteudoCheck().RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Passed);
        result.Violations.Should().BeEmpty();
    }

    /// <summary>
    /// "ANEXO II" listado no corpo e ausente do quadro, que só cita o "ANEXO I". Antes o
    /// romano virava "Anexo I" e "anexo i" casava dentro de "anexo ii": passava.
    /// </summary>
    [Fact]
    public async Task ApendiceAnexo_reconhece_numeral_romano_com_fronteira()
    {
        var structure = DocxFixtureBuilder.Novo()
            .Titulo("ANEXO I - Checklist", 0)
            .Titulo("ANEXO II - E-mails relevantes", 0)
            .Tabela("Características do Documento", ("ANEXO I - Checklist", "-"))
            .Extrair();

        var result = await new ApendiceAnexoCheck(ChecklistPadrao.Cliente, "4.3.8")
            .RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Violations.Should().ContainSingle().Which.Message.Should().Contain("Anexo II");
    }

    /// <summary>Quadro no layout dos documentos MRN: título "Rótulo: valor", códigos em duas
    /// colunas, histórico de revisões e aprovador em linha de cabeçalho.</summary>
    private static DocumentStructure QuadroMrn(string verificadorVigente = "LBN", string aprovador = "André Silva",
                                               string titulo = "Relatório de as built") =>
        DocxFixtureBuilder.Novo()
            .TabelaGrade(
                ["CARACTERÍSTICAS DO DOCUMENTO"],
                [$"Título do Documento: {titulo}"],
                ["Pimenta de Ávila", "Cliente"],
                ["RN-816-RL-67456-00", "QD5-PDA-26-04-095-RT-1"],
                ["Rev.", "0A", "0B", "00", "01"],
                ["1", "x", "", "x", ""],
                ["Rev.", "Data", "Emissor", "Verificador", "Descrição das Revisões"],
                ["0A", "10/03/26", "BB", "LBN", "Para comentários."],
                ["00", "26/03/26", "BB", verificadorVigente, "Conforme construído."],
                ["Nome do Aprovador", "Assinatura do Aprovador"],
                [aprovador, ""])
            .Extrair();

    private static readonly (string, string)[] ParametrosMrn =
    [
        ("codificacao.pdaRegex", @"^[A-Z]{2}-\d{3}-[A-Z]{2}-\d{5}$"),
        ("quadro.aliases.Elaborado por", "Elaborado por|Elaborador|Emissor"),
        ("quadro.aliases.Verificado por", "Verificado por|Verificador|Verificador Técnico"),
        ("quadro.aliases.Aprovado por", "Aprovado por|Aprovador")
    ];

    [Fact]
    public async Task QuadroPreenchido_aceita_quadro_mrn_completo()
    {
        var result = await new QuadroCaracteristicasPreenchidoCheck()
            .RunAsync(Contexto(QuadroMrn(), ParametrosMrn), TestContext.Current.CancellationToken);

        result.Violations.Should().BeEmpty();
        result.Status.Should().Be(CheckStatus.Passed);
    }

    /// <summary>O verificador da revisão anterior estava preenchido — e bastava para passar.</summary>
    [Fact]
    public async Task QuadroPreenchido_exige_o_campo_na_revisao_vigente()
    {
        var result = await new QuadroCaracteristicasPreenchidoCheck()
            .RunAsync(Contexto(QuadroMrn(verificadorVigente: ""), ParametrosMrn), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Violations.Should().ContainSingle()
            .Which.Message.Should().Contain("Verificado por").And.Contain("vigente (00)");
    }

    /// <summary>O rótulo vizinho "Assinatura do Aprovador" contava como valor do campo.</summary>
    [Fact]
    public async Task QuadroPreenchido_detecta_aprovador_vazio_e_titulo_vazio()
    {
        var result = await new QuadroCaracteristicasPreenchidoCheck()
            .RunAsync(Contexto(QuadroMrn(aprovador: "", titulo: ""), ParametrosMrn), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Violations.Select(v => v.Message).Should().Contain(m => m.Contains("Aprovado por"))
            .And.Contain(m => m.Contains("Título"));
    }

    [Fact]
    public async Task IndiceConteudo_detecta_titulo_ausente_do_indice()
    {
        var structure = DocxFixtureBuilder.Novo()
            .EntradaIndice("INTRODUÇÃO")
            .EntradaIndice("METODOLOGIA")
            .Titulo("INTRODUÇÃO", 0)
            .Titulo("METODOLOGIA", 0)
            .Titulo("CONCLUSÕES ESQUECIDAS NO INDICE", 0)   // não consta do índice
            .Extrair();

        var result = await new IndiceConteudoCheck(ChecklistPadrao.Pda, "4.3.4.3")
            .RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Violations.Should().Contain(v => v.Message.Contains("CONCLUSÕES ESQUECIDAS"));
    }

    [Fact]
    public async Task IndiceConteudo_aceita_indice_completo()
    {
        var structure = DocxFixtureBuilder.Novo()
            .EntradaIndice("INTRODUÇÃO")
            .EntradaIndice("METODOLOGIA")
            .Titulo("INTRODUÇÃO", 0)
            .Titulo("METODOLOGIA", 0)
            .Extrair();

        var result = await new IndiceConteudoCheck(ChecklistPadrao.Pda, "4.3.4.3")
            .RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task ApendiceAnexo_detecta_anexo_nao_referenciado_no_quadro()
    {
        var structure = DocxFixtureBuilder.Novo()
            .Tabela("Características do Documento", ("Revisão", "00"), ("Título", "Relatório"))
            .Titulo("ANEXO A - Ensaios de laboratório", 0)
            .Extrair();

        var result = await new ApendiceAnexoCheck(ChecklistPadrao.Cliente, "4.3.8")
            .RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Violations.Should().Contain(v => v.Message.Contains("Anexo A"));
    }

    [Fact]
    public async Task ApendiceAnexo_aceita_anexo_referenciado_no_quadro()
    {
        var structure = DocxFixtureBuilder.Novo()
            .Tabela("Características do Documento",
                    ("Revisão", "00"), ("Documentos complementares", "Anexo A - Ensaios"))
            .Titulo("ANEXO A - Ensaios de laboratório", 0)
            .Extrair();

        var result = await new ApendiceAnexoCheck(ChecklistPadrao.Cliente, "4.3.8")
            .RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Passed);
    }

    [Fact]
    public async Task NumeracaoPaginas_detecta_numeracao_apenas_no_rodape()
    {
        var structure = DocxFixtureBuilder.Novo()
            .Paragrafo("Corpo do documento.")
            .Rodape("Página ", comCampoPagina: true)
            .Extrair();

        var result = await new NumeracaoPaginasCheck(ChecklistPadrao.Pda, "4.3.6.5")
            .RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Failed);
        result.Violations.Should().Contain(v => v.Message.Contains("rodapé"));
    }

    [Fact]
    public async Task NumeracaoPaginas_aceita_numeracao_no_cabecalho()
    {
        var structure = DocxFixtureBuilder.Novo()
            .Paragrafo("Corpo do documento.")
            .Cabecalho("Folha ", comCampoPagina: true)
            .Extrair();

        var result = await new NumeracaoPaginasCheck(ChecklistPadrao.Pda, "4.3.6.5")
            .RunAsync(Contexto(structure), TestContext.Current.CancellationToken);

        result.Status.Should().Be(CheckStatus.Passed);
    }
}
