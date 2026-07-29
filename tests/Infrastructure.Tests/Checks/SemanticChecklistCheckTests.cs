using FluentAssertions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Profile;
using WordComplianceValidator.Infrastructure.Checks;
using WordComplianceValidator.Infrastructure.OpenAI;
using WordComplianceValidator.Infrastructure.Tests.Fixtures;

namespace WordComplianceValidator.Infrastructure.Tests.Checks;

public class SemanticChecklistCheckTests
{
    /// <summary>ISemanticChecker roteirizado: exercita o motor genérico sem chamar a rede.</summary>
    private sealed class FakeSemanticChecker : ISemanticChecker
    {
        private readonly SemanticEvaluation _resposta;
        public string? UltimaInstrucao { get; private set; }
        public string? UltimoConteudo { get; private set; }
        public string? UltimoModelo { get; private set; }
        public int Chamadas { get; private set; }

        public FakeSemanticChecker(SemanticEvaluation resposta) => _resposta = resposta;

        public Task<SemanticVerdict> EvaluateAsync(string i, string c, CancellationToken ct = default) =>
            Task.FromResult(new SemanticVerdict(true, "ok"));

        public Task<SemanticEvaluation> AvaliarAsync(
            string instrucao, string conteudo, string? modelo = null, CancellationToken ct = default)
        {
            Chamadas++;
            UltimaInstrucao = instrucao;
            UltimoConteudo = conteudo;
            UltimoModelo = modelo;
            return Task.FromResult(_resposta);
        }
    }

    private static ChecklistEntry Entrada(string descricao = "Verificar se o documento usa linguagem formal.") =>
        new(new ChecklistRef("PS-002", "4.2", ChecklistPadrao.Cliente),
            31, "Edição de Documentos Técnicos", "Corpo", "Ortografia e Gramática",
            descricao, IaAutomatizavel: false, Observacao: "Caso a pensar.");

    private static DocumentContext Contexto(params (string, string)[] parametros)
    {
        var structure = DocxFixtureBuilder.Novo()
            .Titulo("INTRODUÇÃO", 0)
            .Paragrafo("O aterro adjacentes foram executado com rejeito ressecado.")
            .Extrair();

        var mapa = parametros.ToDictionary(p => p.Item1, p => p.Item2);
        return new DocumentContext("fixture.docx", structure, new ClientProfile("Teste", "1.0", mapa));
    }

    [Fact]
    public async Task Nao_aplicavel_vira_Skipped_e_nao_gera_violacao()
    {
        var fake = new FakeSemanticChecker(new SemanticEvaluation(
            SemanticStatus.NaoAplicavel, [], "Documento não possui apêndices."));

        var result = await new SemanticChecklistCheck(Entrada(), fake, new DefaultEvidenceSelector())
            .RunAsync(Contexto());

        result.Status.Should().Be(CheckStatus.Skipped);
        result.Violations.Should().BeEmpty();
        result.Note.Should().Contain("apêndices");
    }

    [Fact]
    public async Task Nao_conforme_com_erro_vira_Failed()
    {
        var fake = new FakeSemanticChecker(new SemanticEvaluation(
            SemanticStatus.NaoConforme,
            [new SemanticFinding(Severity.Error, "Erro de concordância nominal.", "aterro adjacentes")],
            "Há violação de concordância."));

        var result = await new SemanticChecklistCheck(Entrada(), fake, new DefaultEvidenceSelector())
            .RunAsync(Contexto());

        result.Status.Should().Be(CheckStatus.Failed);
        result.Violations.Should().ContainSingle()
              .Which.Severity.Should().Be(Severity.Error);
    }

    [Fact]
    public async Task Achado_com_trecho_e_ancorado_no_paragrafo_correspondente()
    {
        var fake = new FakeSemanticChecker(new SemanticEvaluation(
            SemanticStatus.NaoConforme,
            [new SemanticFinding(Severity.Error, "Erro de concordância.", "aterro adjacentes foram executado")],
            null));

        var ctx = Contexto();
        var result = await new SemanticChecklistCheck(Entrada(), fake, new DefaultEvidenceSelector())
            .RunAsync(ctx);

        var location = result.Violations.Single().Location;
        location!.ParagraphId.Should().NotBeNullOrEmpty();

        // A âncora tem de apontar para o parágrafo que realmente contém o trecho.
        var alvo = ctx.Structure.Paragraphs.Single(p => p.ParagraphId == location.ParagraphId);
        alvo.Text.Should().Contain("aterro adjacentes");
    }

    [Fact]
    public async Task Conforme_vira_Passed()
    {
        var fake = new FakeSemanticChecker(new SemanticEvaluation(
            SemanticStatus.Conforme, [], "Texto formal e correto."));

        var result = await new SemanticChecklistCheck(Entrada(), fake, new DefaultEvidenceSelector())
            .RunAsync(Contexto());

        result.Status.Should().Be(CheckStatus.Passed);
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public async Task Instrucao_enviada_ao_LLM_vem_da_planilha()
    {
        var fake = new FakeSemanticChecker(new SemanticEvaluation(SemanticStatus.Conforme, []));
        var entrada = Entrada("Verificar se o documento utiliza linguagem formal e correção gramatical.");

        await new SemanticChecklistCheck(entrada, fake, new DefaultEvidenceSelector()).RunAsync(Contexto());

        fake.UltimaInstrucao.Should().Contain("linguagem formal e correção gramatical");
        fake.UltimaInstrucao.Should().Contain("Ortografia e Gramática");
        fake.UltimaInstrucao.Should().Contain("Caso a pensar.", "a observação do checklist entra na instrução");
        fake.UltimoConteudo.Should().Contain("Estrutura de títulos");
    }

    [Fact]
    public async Task Modelo_por_regra_e_repassado_ao_checker()
    {
        var fake = new FakeSemanticChecker(new SemanticEvaluation(SemanticStatus.Conforme, []));

        await new SemanticChecklistCheck(Entrada(), fake, new DefaultEvidenceSelector(), "gpt-5.6-luna")
            .RunAsync(Contexto());

        fake.UltimoModelo.Should().Be("gpt-5.6-luna");
    }

    // --- Fábrica / allow-list ---

    [Fact]
    public void Factory_nao_cria_check_sem_allow_list()
    {
        var factory = new SemanticCheckFactory(new FakeSemanticChecker(
            new SemanticEvaluation(SemanticStatus.Conforme, [])));

        factory.Create(Entrada(), Contexto()).Should().BeNull(
            "sem 'semantico.regrasHabilitadas' nenhuma regra deve consumir tokens");
    }

    [Fact]
    public void Factory_cria_check_para_ref_na_allow_list()
    {
        var factory = new SemanticCheckFactory(new FakeSemanticChecker(
            new SemanticEvaluation(SemanticStatus.Conforme, [])));
        var ctx = Contexto(("semantico.regrasHabilitadas", "PS-002:4.2:Cliente|PS-018:4.4:Pda"));

        factory.Create(Entrada(), ctx).Should().BeOfType<SemanticChecklistCheck>();
    }

    [Fact]
    public void Factory_respeita_curinga_e_ignora_entrada_sem_descricao()
    {
        var factory = new SemanticCheckFactory(new FakeSemanticChecker(
            new SemanticEvaluation(SemanticStatus.Conforme, [])));
        var ctx = Contexto(("semantico.regrasHabilitadas", "*"));

        factory.Create(Entrada(), ctx).Should().NotBeNull();
        factory.Create(Entrada(descricao: "  "), ctx).Should().BeNull(
            "sem descrição não há instrução de verificação a enviar");
    }

    [Fact]
    public void Modelo_por_regra_tem_precedencia_sobre_o_default()
    {
        var profile = new ClientProfile("Teste", "1.0", new Dictionary<string, string>
        {
            ["semantico.modelo.default"] = "gpt-5.6-sol",
            ["semantico.modelo.PS-002:4.2:Cliente"] = "gpt-5.6-luna"
        });

        SemanticCheckFactory.ModeloPara(new ChecklistRef("PS-002", "4.2", ChecklistPadrao.Cliente), profile)
            .Should().Be("gpt-5.6-luna");
        SemanticCheckFactory.ModeloPara(new ChecklistRef("PS-018", "4.4", ChecklistPadrao.Pda), profile)
            .Should().Be("gpt-5.6-sol");
    }

    // --- Parsing da resposta do modelo ---

    [Fact]
    public void Parse_mapeia_status_severidade_e_trecho()
    {
        var json = """
        {
          "status":"nao_conforme",
          "justificativa":"Há erros.",
          "achados":[
            {"severidade":"erro","mensagem":"Concordância incorreta.","trecho":"aterro adjacentes"},
            {"severidade":"aviso","mensagem":"Sigla sem hífen.","trecho":""}
          ]
        }
        """;

        var avaliacao = OpenAiSemanticChecker.Parse(json);

        avaliacao.Status.Should().Be(SemanticStatus.NaoConforme);
        avaliacao.Achados.Should().HaveCount(2);
        avaliacao.Achados[0].Severidade.Should().Be(Severity.Error);
        avaliacao.Achados[0].TrechoAncora.Should().Be("aterro adjacentes");
        avaliacao.Achados[1].Severidade.Should().Be(Severity.Warning);
        avaliacao.Achados[1].TrechoAncora.Should().BeNull("trecho vazio não é âncora");
    }

    [Fact]
    public void Parse_nao_conforme_sem_achados_ainda_produz_algo_acionavel()
    {
        var avaliacao = OpenAiSemanticChecker.Parse(
            """{"status":"nao_conforme","justificativa":"Divergência encontrada.","achados":[]}""");

        avaliacao.Status.Should().Be(SemanticStatus.NaoConforme);
        avaliacao.Achados.Should().ContainSingle()
                 .Which.Mensagem.Should().Contain("Divergência encontrada");
    }

    [Fact]
    public void Parse_trata_status_desconhecido_como_nao_aplicavel()
    {
        var avaliacao = OpenAiSemanticChecker.Parse(
            """{"status":"talvez","justificativa":"","achados":[]}""");

        avaliacao.Status.Should().Be(SemanticStatus.NaoAplicavel,
            "na dúvida o motor não deve acusar não-conformidade");
    }
}
