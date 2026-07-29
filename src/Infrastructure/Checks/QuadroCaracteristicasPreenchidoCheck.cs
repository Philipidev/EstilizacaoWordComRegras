using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.7 — Quadro "Características do Documento": confirma preenchimento
/// dos campos obrigatórios configurados em <c>quadro.camposObrigatorios</c>.
/// Funciona tanto em layout rótulo→valor quanto em layout coluna (histórico).
/// </summary>
public sealed class QuadroCaracteristicasPreenchidoCheck : IRuleCheck
{
    private static readonly string DefaultCampos =
        "Codificação|Título|Revisão|Data|Elaborado por|Verificado por|Aprovado por";

    /// <summary>
    /// Aliases padrão por campo (rótulos equivalentes que podem aparecer no quadro).
    /// Configurável via profile com chaves do tipo <c>quadro.aliases.&lt;campo&gt;</c>.
    /// </summary>
    private static readonly Dictionary<string, string[]> DefaultAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Codificação"]   = new[] { "Codificação", "Código", "Codificação PdA", "Documento" },
        ["Título"]        = new[] { "Título", "Titulo", "Assunto" },
        ["Revisão"]       = new[] { "Revisão", "Rev.", "Rev" },
        ["Data"]          = new[] { "Data", "Data de emissão", "Emissão" },
        ["Elaborado por"] = new[] { "Elaborado por", "Elaborador", "Emissor" },
        ["Verificado por"] = new[] { "Verificado por", "Verificador", "Verificador Técnico" },
        ["Aprovado por"]  = new[] { "Aprovado por", "Aprovador" },
    };

    public QuadroCaracteristicasPreenchidoCheck(
        ChecklistPadrao padrao = ChecklistPadrao.Cliente,
        string item = "4.7")
    {
        Ref = new ChecklistRef("PS-002", item, padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var titulosAlias = (ctx.Profile.Get("quadroCaracteristicas.aliases")
            ?? "Características do Documento|Quadro de Características|Características")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var camposRaw = ctx.Profile.Get("quadro.camposObrigatorios") ?? DefaultCampos;
        var camposObrigatorios = camposRaw
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var table = QuadroCaracteristicas.Find(ctx.Structure, titulosAlias);
        if (table is null)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Failed,
                new[]
                {
                    new Violation(Ref.ToString(), Severity.Error,
                        "Quadro 'Características do Documento' não localizado.",
                        new ViolationLocation(null, null, null, "Quadro Características"))
                }));
        }

        var byLabel = QuadroCaracteristicas.FieldsByLabel(table);

        var violations = new List<Violation>();
        foreach (var campo in camposObrigatorios)
        {
            var aliases = AliasesFor(ctx, campo);
            if (CampoPreenchido(table, byLabel, aliases)) continue;

            violations.Add(new Violation(
                RuleId: Ref.ToString(),
                Severity: Severity.Error,
                Message: $"Campo obrigatório '{campo}' está ausente ou vazio no quadro 'Características do Documento'.",
                Location: new ViolationLocation(null, null, null, "Quadro Características")));
        }

        var (numeracao, notaNumeracao) = NumeracaoDaPaginaDoQuadro(ctx, table);
        if (numeracao is not null) violations.Add(numeracao);

        var status = violations.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations, Note: notaNumeracao));
    }

    /// <summary>
    /// Segunda exigência do item 4.7: a página do quadro não pode ser numerada. É decidível
    /// pelo OOXML — basta olhar se a seção que contém o quadro tem campo PAGE/NUMPAGES no
    /// cabeçalho ou rodapé — e por isso não deve ficar a cargo do avaliador semântico, que
    /// confunde a grade de controle de folhas <em>dentro</em> do quadro com numeração
    /// <em>da</em> página.
    /// </summary>
    private (Violation? Violacao, string Nota) NumeracaoDaPaginaDoQuadro(
        DocumentContext ctx, ExtractedTable table)
    {
        var secao = ctx.Structure.Paragraphs
            .Where(p => p.TableIndex == table.Index)
            .Select(p => (int?)p.SectionIndex)
            .FirstOrDefault();

        if (secao is null)
            return (null, "Seção do quadro não identificada; numeração da página não verificada.");

        // SectionIndex de um header/footer é a primeira seção que o referencia. Se a seção do
        // quadro reaproveitar um header de seção anterior, esta checagem se cala em vez de
        // acusar — falso negativo é preferível a falso positivo numa regra de forma.
        var numerados = ctx.Structure.Headers.Concat(ctx.Structure.Footers)
            .Where(hf => hf.SectionIndex == secao && TemCampoPagina(hf))
            .ToList();

        if (numerados.Count == 0)
            return (null, $"Página do quadro (seção {secao}) sem campo de numeração.");

        return (new Violation(
            RuleId: Ref.ToString(),
            Severity: Severity.Error,
            Message: "A página do quadro 'Características do Documento' contém numeração de páginas.",
            Location: new ViolationLocation(null, numerados[0].Kind, secao, "Quadro Características")),
            $"Página do quadro (seção {secao}) com campo de numeração em {numerados.Count} cabeçalho/rodapé.");
    }

    private static bool TemCampoPagina(ExtractedHeaderFooter hf) =>
        hf.FieldCodes.Any(c => c.Equals("PAGE", StringComparison.OrdinalIgnoreCase)
                            || c.Equals("NUMPAGES", StringComparison.OrdinalIgnoreCase));

    private static bool CampoPreenchido(
        ExtractedTable table,
        IReadOnlyDictionary<string, string> byLabel,
        IReadOnlyList<string> aliases)
    {
        // 1) Layout linha: qualquer alias localizado como rótulo em FieldsByLabel.
        var rotuloEncontrado = false;
        foreach (var alias in aliases)
        {
            var match = byLabel.FirstOrDefault(kv =>
                kv.Key.Contains(alias, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(match.Key)) continue;

            rotuloEncontrado = true;
            if (!string.IsNullOrWhiteSpace(match.Value))
                return true;
        }
        // Se algum rótulo foi achado em layout linha mas valor estava vazio → não preenchido.
        // Não faz sentido cair em layout coluna se a tabela já tem o campo como rótulo de linha.
        if (rotuloEncontrado) return false;

        // 2) Layout coluna: qualquer alias aparece como cabeçalho de coluna.
        var spec = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["v"] = aliases
        };
        var byCol = QuadroCaracteristicas.FieldsByColumn(table, spec);
        return byCol is not null
            && byCol.TryGetValue("v", out var v)
            && !string.IsNullOrWhiteSpace(v);
    }

    private static IReadOnlyList<string> AliasesFor(DocumentContext ctx, string campo)
    {
        var configured = ctx.Profile.Get($"quadro.aliases.{campo}");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        if (DefaultAliases.TryGetValue(campo, out var def))
            return def;
        return new[] { campo };
    }
}
