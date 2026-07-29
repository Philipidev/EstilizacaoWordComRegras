using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Profile;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// Decide, por entrada do checklist, se o motor semântico genérico deve assumi-la e com qual
/// modelo.
/// <para>
/// A habilitação é por allow-list explícita no profile, nunca automática. Itens do PS-005
/// (fluxo Meridian, e-mails ao GQ, autoridade do aprovador) são indecidíveis a partir do
/// .docx: rodá-los custaria tokens para produzir "não aplicável" em todos.
/// </para>
/// <para>
/// Parâmetros de profile:
/// <c>semantico.regrasHabilitadas</c> — lista de Refs separada por '|', ou <c>*</c> para todas
/// as entradas sem check dedicado.
/// <c>semantico.modelo.default</c> — modelo padrão das regras semânticas.
/// <c>semantico.modelo.&lt;Ref&gt;</c> — override por regra (ex.: um modelo mais barato para
/// checagens simples de presença de texto).
/// </para>
/// </summary>
public sealed class SemanticCheckFactory
{
    private readonly ISemanticChecker _semantic;
    private readonly IEvidenceSelector _evidence;

    public SemanticCheckFactory(ISemanticChecker semantic, IEvidenceSelector? evidence = null)
    {
        _semantic = semantic;
        _evidence = evidence ?? new DefaultEvidenceSelector();
    }

    public IRuleCheck? Create(ChecklistEntry entry, DocumentContext ctx)
    {
        if (!Habilitada(entry.Ref, ctx.Profile)) return null;

        // Sem descrição não há instrução de verificação — melhor pular do que perguntar vago.
        if (string.IsNullOrWhiteSpace(entry.Descricao)) return null;

        return new SemanticChecklistCheck(entry, _semantic, _evidence, ModeloPara(entry.Ref, ctx.Profile));
    }

    /// <summary>Refs elegíveis segundo o profile. Público para permitir auditoria e testes.</summary>
    public static bool Habilitada(ChecklistRef refId, ClientProfile profile)
    {
        var lista = profile.Get("semantico.regrasHabilitadas");
        if (string.IsNullOrWhiteSpace(lista)) return false;

        if (lista.Trim() == "*") return true;

        return lista
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(r => string.Equals(r, refId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    public static string? ModeloPara(ChecklistRef refId, ClientProfile profile) =>
        profile.Get($"semantico.modelo.{refId}")
        ?? profile.Get("semantico.modelo.default");
}
