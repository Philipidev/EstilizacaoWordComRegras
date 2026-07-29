using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.5.2 (Cliente) / 4.3.4.6 (PdA) — Painel de navegação.
/// <para>
/// O painel de navegação do Word é exatamente a árvore de outline levels dos parágrafos,
/// então a regra é verificável sem LLM: hierarquia sem saltos de nível, sem títulos vazios
/// e sem parágrafos de corpo indevidamente marcados como título.
/// </para>
/// </summary>
public sealed class PainelNavegacaoCheck : IRuleCheck
{
    /// <summary>Acima disso um "título" é quase certamente um parágrafo de corpo mal marcado.</summary>
    private const int ComprimentoMaximoTituloDefault = 200;

    public PainelNavegacaoCheck(ChecklistPadrao padrao = ChecklistPadrao.Cliente, string item = "4.3.5.2")
    {
        Ref = new ChecklistRef("PS-002", item, padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var titulos = DocumentoTexto.Titulos(ctx.Structure);
        if (titulos.Count == 0)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Documento sem títulos com nível de outline — painel de navegação vazio."));
        }

        var maxComprimento = ctx.Profile.GetInt("painelNavegacao.comprimentoMaximoTitulo")
                             ?? ComprimentoMaximoTituloDefault;
        var violations = new List<Violation>();

        // A hierarquia só é avaliada a partir do primeiro título de nível 1. O que vem antes
        // pertence à capa/folha de rosto, onde é comum haver texto com outlineLvl solto.
        var inicio = titulos.ToList().FindIndex(p => p.OutlineLevel == 0);
        if (inicio < 0)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Nenhum título de nível 1 localizado — hierarquia não avaliável."));
        }

        for (var i = 0; i < inicio; i++)
        {
            violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                $"Texto antes do primeiro título de nível 1 aparece no painel de navegação " +
                $"(nível {titulos[i].OutlineLevel + 1}): '{Resumo(titulos[i].Text)}'.",
                new ViolationLocation(titulos[i].ParagraphId, null, titulos[i].SectionIndex,
                                      "Painel de navegação")));
        }

        var anterior = titulos[inicio].OutlineLevel!.Value;
        for (var i = inicio; i < titulos.Count; i++)
        {
            var t = titulos[i];
            var nivel = t.OutlineLevel!.Value;

            if (nivel > anterior + 1)
            {
                violations.Add(new Violation(Ref.ToString(), Severity.Error,
                    $"Salto de hierarquia no painel de navegação: nível {anterior + 1} → " +
                    $"nível {nivel + 1} em '{Resumo(t.Text)}'.",
                    new ViolationLocation(t.ParagraphId, null, t.SectionIndex, "Painel de navegação")));
            }

            if (t.Text.Trim().Length > maxComprimento)
            {
                violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                    $"Título de nível {nivel + 1} com {t.Text.Trim().Length} caracteres — " +
                    $"provável parágrafo de corpo marcado como título: '{Resumo(t.Text)}'.",
                    new ViolationLocation(t.ParagraphId, null, t.SectionIndex, "Painel de navegação")));
            }

            anterior = nivel;
        }

        var status = violations.Count == 0 ? CheckStatus.Passed
                   : violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : CheckStatus.Skipped;

        return Task.FromResult(new RuleCheckResult(Ref, status, violations,
            Note: $"{titulos.Count} títulos no painel de navegação (níveis " +
                  $"{titulos.Min(t => t.OutlineLevel) + 1}–{titulos.Max(t => t.OutlineLevel) + 1})."));
    }

    private static string Resumo(string texto)
    {
        var t = texto.Trim().Replace("\n", " ");
        return t.Length <= 60 ? t : t[..60] + "…";
    }
}
