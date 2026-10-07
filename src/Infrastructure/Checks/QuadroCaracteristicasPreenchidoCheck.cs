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

        var vigente = QuadroCaracteristicas.HistoricoDeRevisoes(table).LastOrDefault();

        var violations = new List<Violation>();
        foreach (var campo in camposObrigatorios)
        {
            var aliases = AliasesFor(ctx, campo);
            if (CampoPreenchido(ctx, table, campo, aliases, vigente)) continue;

            var onde = vigente?.Coluna(aliases) is not null
                ? $"na linha da revisão vigente ({vigente.Revisao}) do histórico do quadro"
                : "no quadro 'Características do Documento'";
            violations.Add(new Violation(
                RuleId: Ref.ToString(),
                Severity: Severity.Error,
                Message: $"Campo obrigatório '{campo}' está ausente ou vazio {onde}.",
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

        // O que a seção do quadro exibe, herança incluída: uma seção que não declara cabeçalho
        // usa o da anterior, e antes isso passava como "sem numeração". Quando o quadro abre a
        // seção e ela tem "primeira página diferente", vale o cabeçalho da primeira página.
        var secaoInfo = ctx.Structure.Sections.FirstOrDefault(s => s.Index == secao);
        IEnumerable<ExtractedHeaderFooter> partes;
        if (secaoInfo is not null && (secaoInfo.DisplayedHeaderFooters.Count > 0 || secaoInfo.FirstPageHeaderFooters.Count > 0))
        {
            var primeiroDaSecao = ctx.Structure.Paragraphs.FirstOrDefault(p => p.SectionIndex == secao);
            var quadroAbreASecao = primeiroDaSecao?.TableIndex == table.Index;
            partes = secaoInfo.TitlePage && quadroAbreASecao
                ? secaoInfo.FirstPageHeaderFooters
                : secaoInfo.DisplayedHeaderFooters;
        }
        else
        {
            // Estrutura sem o mapa por seção (montada à mão): só as partes declaradas na seção.
            partes = ctx.Structure.Headers.Concat(ctx.Structure.Footers).Where(hf => hf.SectionIndex == secao);
        }
        var numerados = partes.Where(TemCampoPagina).ToList();

        if (numerados.Count == 0)
            // "como exigido": no --verbose esta nota aparece ao lado de um ✓ e, sozinha,
            // "sem campo de numeração" soava como defeito.
            return (null, $"Página do quadro (seção {secao + 1}) sem numeração, como exigido.");

        return (new Violation(
            RuleId: Ref.ToString(),
            Severity: Severity.Error,
            Message: "A página do quadro 'Características do Documento' contém numeração de páginas.",
            Location: new ViolationLocation(null, numerados[0].Kind, secao, "Quadro Características")),
            $"Página do quadro (seção {secao + 1}) com campo de numeração em {numerados.Count} cabeçalho/rodapé.");
    }

    private static bool TemCampoPagina(ExtractedHeaderFooter hf) =>
        hf.FieldCodes.Any(c => c.Equals("PAGE", StringComparison.OrdinalIgnoreCase)
                            || c.Equals("NUMPAGES", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Um campo do quadro está preenchido? Tenta, em ordem, as formas como os formulários reais
    /// guardam o valor:
    /// <list type="number">
    /// <item>coluna do histórico de revisões — o valor que vale é o da revisão <b>vigente</b>
    /// (última linha), não "qualquer linha preenchida";</item>
    /// <item>codificação: um código no padrão do profile em qualquer célula do quadro;</item>
    /// <item>"Rótulo: valor" na mesma célula ("Título do Documento: UP-26…");</item>
    /// <item>rótulo numa célula e valor à direita, na mesma linha;</item>
    /// <item>linha de cabeçalho ("Nome do Aprovador | Assinatura do Aprovador") com os valores
    /// na linha de baixo, mesma coluna.</item>
    /// </list>
    /// A versão anterior casava rótulos por substring: "Revisão" era satisfeito pela linha "Rev."
    /// da grade de folhas, "Aprovado por" pelo par "Nome do Aprovador → Assinatura do Aprovador"
    /// (o rótulo vizinho contava como valor) e "Codificação" por "ADENDO I - Documentos…".
    /// Título e aprovador vazios passavam.
    /// </summary>
    private static bool CampoPreenchido(
        DocumentContext ctx,
        ExtractedTable table,
        string campo,
        IReadOnlyList<string> aliases,
        QuadroCaracteristicas.EntradaDeRevisao? vigente)
    {
        if (vigente?.Coluna(aliases) is { } doHistorico)
            return !string.IsNullOrWhiteSpace(doHistorico);

        var celulasTexto = table.Cells.Select(c => c.Text).ToList();
        if (DocumentoTexto.Normalizar(campo).StartsWith("codific", StringComparison.Ordinal)
            && (Codificacao.EncontrarEmQualquer(celulasTexto, ctx.Profile.Get("codificacao.pdaRegex")) is not null
                || Codificacao.EncontrarEmQualquer(celulasTexto, ctx.Profile.Get("codificacao.clienteRegex")) is not null))
            return true;

        var aliasesNorm = aliases.Select(DocumentoTexto.Normalizar).Where(a => a.Length > 0).ToList();
        bool EhRotulo(ExtractedTableCell c)
        {
            var norm = DocumentoTexto.Normalizar(c.Text);
            if (norm.Length == 0) return false;
            return aliasesNorm.Any(a => norm == a
                || (norm.Length <= a.Length + 30
                    && System.Text.RegularExpressions.Regex.IsMatch(norm, $@"\b{System.Text.RegularExpressions.Regex.Escape(a)}\b")));
        }
        bool TemValor(ExtractedTableCell c) => !string.IsNullOrWhiteSpace(c.Text) || c.ImageCount > 0;

        var linhas = table.Cells.GroupBy(c => c.Row).OrderBy(g => g.Key)
            .Select(g => g.OrderBy(c => c.Column).ToList())
            .Where(l => !QuadroCaracteristicas.EhLinhaDeGradeDeFolhas(l) && !DefaultEvidenceSelector.EhCabecalhoDeGrade(l))
            .ToList();

        for (var r = 0; r < linhas.Count; r++)
        {
            foreach (var celula in linhas[r])
            {
                // "Título do Documento: UP-26…" — o rótulo começa a célula e o valor vem depois do ":".
                var doisPontos = celula.Text.IndexOf(':');
                if (doisPontos > 0)
                {
                    var rotulo = DocumentoTexto.Normalizar(celula.Text[..doisPontos]);
                    if (aliasesNorm.Any(a => rotulo == a || rotulo.StartsWith(a + " ", StringComparison.Ordinal))
                        && celula.Text[(doisPontos + 1)..].Trim().Length > 0)
                        return true;
                }

                if (!EhRotulo(celula)) continue;

                var resto = linhas[r].Where(c => c.Column > celula.Column).ToList();

                // "Rótulo:" ocupando a linha inteira é campo de valor na própria célula; vazio
                // depois do ":" é campo vazio. Procurar na linha de baixo pegaria o subtítulo
                // seguinte do formulário ("DENOMINAÇÃO MAGNÉTICA") como se fosse o valor.
                if (doisPontos > 0 && linhas[r].Count == 1) continue;
                if (resto.Any(c => TemValor(c) && !EhRotulo(c))) return true;

                // Linha de cabeçalho: os vizinhos também são rótulos (ou não há vizinhos) — o
                // valor está na linha de baixo, mesma coluna.
                var linhaDeCabecalho = resto.Count == 0 || resto.All(c => TemValor(c) && EhRotulo(c));
                if (linhaDeCabecalho && r + 1 < linhas.Count
                    && linhas[r + 1].FirstOrDefault(c => c.Column == celula.Column) is { } abaixo
                    && TemValor(abaixo) && !EhRotulo(abaixo))
                    return true;
            }
        }
        return false;
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
