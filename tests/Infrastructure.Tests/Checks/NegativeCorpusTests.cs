using FluentAssertions;
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
            .RunAsync(Contexto(builder.Extrair()));

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
            .RunAsync(Contexto(builder.Extrair()));

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
            .RunAsync(Contexto(structure));

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
            .RunAsync(Contexto(structure));

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
            .RunAsync(Contexto(structure));

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
            .RunAsync(Contexto(structure));

        result.Status.Should().Be(CheckStatus.Passed);
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
            .RunAsync(Contexto(structure));

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
            .RunAsync(Contexto(structure));

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
            .RunAsync(Contexto(structure));

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
            .RunAsync(Contexto(structure));

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
            .RunAsync(Contexto(structure));

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
            .RunAsync(Contexto(structure));

        result.Status.Should().Be(CheckStatus.Passed);
    }
}
