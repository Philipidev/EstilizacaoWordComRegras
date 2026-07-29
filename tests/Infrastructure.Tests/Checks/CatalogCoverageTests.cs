using FluentAssertions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Infrastructure.Checks;
using WordComplianceValidator.Infrastructure.Excel;
using WordComplianceValidator.Infrastructure.Profile;

namespace WordComplianceValidator.Infrastructure.Tests.Checks;

/// <summary>
/// Audita a cobertura do CL-001 contra a MESMA lista que o CLI executa
/// (<see cref="CheckRegistry"/>) e a allow-list do profile de exemplo.
/// <para>
/// O objetivo é impedir o modo de falha silencioso: uma regra que ninguém cobre e ninguém
/// declarou manual simplesmente vira "Skipped" no relatório e passa despercebida.
/// </para>
/// </summary>
public class CatalogCoverageTests
{
    /// <summary>
    /// Refs que dependem de sistemas externos ao .docx (fluxo Meridian, e-mails ao GQ,
    /// autoridade do aprovador, PL-011) e por isso permanecem de revisão manual.
    /// Manter a lista explícita força a decisão a ser consciente.
    /// </summary>
    private static readonly HashSet<string> ManuaisDeclarados = new(StringComparer.OrdinalIgnoreCase)
    {
        // PL-011 é um documento externo com a tabela oficial de iniciais.
        "PS-002:4.3.3 (letra b):Pda",
        "PS-002:4.3.3 (letra b):Cliente"
    };

    /// <summary>
    /// A auditoria roda sobre o registro em capacidade plena (com LLM), porque é essa a
    /// pergunta relevante: o que a ferramenta consegue cobrir. Regras que dependem
    /// obrigatoriamente de LLM — como PS-002:4.3.2 — só aparecem nessa configuração.
    /// </summary>
    private sealed class SemanticStub : WordComplianceValidator.Core.Checks.ISemanticChecker
    {
        public static readonly SemanticStub Instancia = new();

        public Task<WordComplianceValidator.Core.Checks.SemanticVerdict> EvaluateAsync(
            string instrucao, string conteudo, CancellationToken ct = default) =>
            throw new NotSupportedException("Auditoria de catálogo não executa checks.");

        public Task<WordComplianceValidator.Core.Checks.SemanticEvaluation> AvaliarAsync(
            string instrucao, string conteudo, string? modelo = null, CancellationToken ct = default) =>
            throw new NotSupportedException("Auditoria de catálogo não executa checks.");
    }

    private static IReadOnlyList<ChecklistEntry> Catalogo() =>
        new ExcelChecklistRepository()
            .LoadAsync(RepoFile("templates", "checklists", "CL-001-CL00100.xlsx"))
            .GetAwaiter().GetResult();

    [Fact]
    public void Toda_entrada_do_CL001_tem_destino_explicito()
    {
        var catalogo = Catalogo();
        var profile = new JsonClientProfileRepository()
            .LoadAsync(RepoFile("profiles", "exemplo.json")).GetAwaiter().GetResult();

        var dedicados = CheckRegistry.Deterministicos(SemanticStub.Instancia)
            .Select(c => c.Ref.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var semDestino = catalogo
            .Where(e => !dedicados.Contains(e.Ref.ToString())
                     && !SemanticCheckFactory.Habilitada(e.Ref, profile)
                     && !ManuaisDeclarados.Contains(e.Ref.ToString())
                     && !e.Ref.Ps.Equals("PS-005", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"{e.Ref} — {e.Titulo}")
            .ToList();

        semDestino.Should().BeEmpty(
            "toda regra fora do PS-005 precisa de check dedicado, entrada na allow-list " +
            "semântica ou declaração explícita de revisão manual. Sem destino:\n" +
            string.Join("\n", semDestino));
    }

    [Fact]
    public void Todo_check_registrado_corresponde_a_uma_entrada_real_do_CL001()
    {
        var refsDoCatalogo = Catalogo().Select(e => e.Ref.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orfaos = CheckRegistry.Deterministicos(SemanticStub.Instancia)
            .Select(c => c.Ref.ToString())
            .Where(r => !refsDoCatalogo.Contains(r))
            .ToList();

        orfaos.Should().BeEmpty(
            "um check cujo Ref não existe no CL-001 nunca é executado. Órfãos: " +
            string.Join(", ", orfaos));
    }

    [Fact]
    public void Allow_list_semantica_do_profile_referencia_apenas_refs_existentes()
    {
        var catalogo = Catalogo();
        var profile = new JsonClientProfileRepository()
            .LoadAsync(RepoFile("profiles", "exemplo.json")).GetAwaiter().GetResult();

        var habilitadas = (profile.Get("semantico.regrasHabilitadas") ?? "")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var refsDoCatalogo = catalogo.Select(e => e.Ref.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        habilitadas.Should().NotBeEmpty();
        habilitadas.Where(r => r != "*")
                   .Should().OnlyContain(r => refsDoCatalogo.Contains(r));
    }

    [Fact]
    public void Nenhuma_regra_do_PS005_consome_tokens_semanticos()
    {
        var profile = new JsonClientProfileRepository()
            .LoadAsync(RepoFile("profiles", "exemplo.json")).GetAwaiter().GetResult();

        var ps005 = Catalogo().Where(e => e.Ref.Ps.Equals("PS-005", StringComparison.OrdinalIgnoreCase)).ToList();

        ps005.Should().NotBeEmpty();
        ps005.Should().OnlyContain(e => !SemanticCheckFactory.Habilitada(e.Ref, profile),
            "PS-005 trata de fluxo Meridian, e-mails e autoridade — indecidível a partir do .docx");
    }

    [Fact]
    public void Registro_nao_tem_refs_duplicados()
    {
        var refs = CheckRegistry.Deterministicos(SemanticStub.Instancia).Select(c => c.Ref.ToString()).ToList();
        refs.Should().OnlyHaveUniqueItems("o ChecklistEngine rejeita Refs duplicados");
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(new[] { dir }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        throw new FileNotFoundException("Arquivo do repositório não encontrado.", Path.Combine(parts));
    }
}
