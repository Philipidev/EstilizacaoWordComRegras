using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-024 4.1.2 / 4.1.3 / 4.1.4 — Tarja de emissão.
/// <para>
/// 4.1.2: revisões 0A–0Z devem exibir a tarja "Emissão para Comentários do Cliente"
/// (opcionalmente combinada com "Não é Válido para Execução").
/// 4.1.3: emissão final (00) e subsequentes (01–99) de estudo preliminar, projeto
/// conceitual e projeto básico devem exibir "Não é Válido para Execução".
/// 4.1.4: documentos traduzidos — depende de informação externa ao .docx; sempre Skipped.
/// </para>
/// A tarja é buscada no que o leitor vê: cabeçalhos e rodapés exibidos (inclusive marca
/// d'água), corpo e tabelas — exceto linhas de histórico de revisões, que apenas descrevem
/// emissões passadas. Cabeçalhos que o Word não exibe só geram aviso.
/// </summary>
public sealed class TarjaEmissaoCheck : IRuleCheck
{
    private static readonly Regex TokenRevisao = new(@"\b(0[A-Za-z]|\d{2})\b", RegexOptions.Compiled);

    private const string PadraoTarjaComentarios = "Emissão para Comentários do Cliente|Emissao para Comentarios do Cliente|Para Comentários do Cliente";
    private const string PadraoTarjaNaoValido = "Não é Válido para Execução|Nao e Valido para Execucao|Não Válido para Execução";

    private readonly string _item;

    public TarjaEmissaoCheck(ChecklistPadrao padrao = ChecklistPadrao.Cliente, string item = "4.1.2")
    {
        _item = item;
        Ref = new ChecklistRef("PS-024", item, padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        if (_item.StartsWith("4.1.4", StringComparison.Ordinal))
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Documento traduzido: exige comparar com a versão em português (fora do .docx)."));
        }

        var revisao = RevisaoDoDocumento(ctx);
        if (revisao is null)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Revisão do documento não localizada no quadro Características."));
        }

        var textoNorm = DocumentoTexto.Normalizar(TextoOndeATarjaValeComo(ctx.Structure));

        var etapaComentarios = Regex.IsMatch(revisao, "^0[A-Za-z]$");
        var violations = new List<Violation>();
        string nota;

        if (_item.StartsWith("4.1.2", StringComparison.Ordinal))
        {
            var tarjas = DocumentoTexto.Lista(ctx.Profile.Get("tarja.comentariosCliente"), PadraoTarjaComentarios);

            if (!etapaComentarios)
            {
                // Fora de 0A–0Z a tarja de comentários não pode mais existir. É o outro lado
                // da mesma regra, e o lado que de fato falha na prática: a tarja entra na 0A,
                // fica num cabeçalho de seção e ninguém a remove ao emitir a 00. Verificar só
                // a presença deixava passar — pior, o documento era lido como se ainda
                // estivesse em 0A–0Z e a tarja obsoleta era aprovada.
                var onde = LocalizarTarja(ctx, tarjas);
                if (onde is null)
                {
                    // Tarja só em cabeçalho que o Word não exibe: não aparece na impressão nem
                    // no PDF, então não reprova — mas reaparece no dia em que alguém ativar
                    // "primeira página diferente" ou "pares e ímpares diferentes" na seção.
                    var latente = LocalizarTarjaOculta(ctx, tarjas);
                    if (latente is not null)
                    {
                        violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                            $"A tarja \"{latente.Value.Texto}\" não aparece no documento, mas continua " +
                            $"gravada num {latente.Value.Origem}, que o Word não exibe. Ela reaparece " +
                            "se a seção passar a usar esse tipo de cabeçalho/rodapé; recomenda-se removê-la.",
                            new ViolationLocation(null, latente.Value.Kind, latente.Value.Secao, "Tarja de emissão")));
                        return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped, violations,
                            Note: $"Revisão '{revisao}' — tarja de comentários apenas em cabeçalho/rodapé oculto."));
                    }

                    return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                        Array.Empty<Violation>(),
                        Note: $"Revisão '{revisao}' não é etapa 0A–0Z e não há tarja de comentários."));
                }

                violations.Add(new Violation(Ref.ToString(), Severity.Error,
                    $"Revisão '{revisao}' não está mais na etapa de comentários (0A–0Z), mas a tarja " +
                    $"\"{onde.Value.Texto}\" permanece no documento ({onde.Value.Origem}). " +
                    "Ela deveria ter sido removida na emissão desta revisão.",
                    new ViolationLocation(null, onde.Value.Kind, onde.Value.Secao, "Tarja de emissão")));

                return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Failed, violations,
                    Note: $"Revisão '{revisao}' — tarja de etapa anterior remanescente."));
            }

            if (!ContemAlguma(textoNorm, tarjas))
            {
                violations.Add(new Violation(Ref.ToString(), Severity.Error,
                    $"Revisão '{revisao}' está na etapa de comentários (0A–0Z), mas não foi localizada " +
                    $"a tarja de emissão ({string.Join(" / ", tarjas)}).",
                    new ViolationLocation(null, null, null, "Tarja de emissão")));
            }
            nota = $"Revisão '{revisao}' — etapa de comentários.";
        }
        else // 4.1.3 — emissão final (00) e subsequentes
        {
            if (etapaComentarios)
            {
                return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                    Array.Empty<Violation>(),
                    Note: $"Revisão '{revisao}' ainda está em 0A–0Z; regra de emissão final não se aplica."));
            }

            // A tarja "Não é Válido para Execução" só é exigida para estudo preliminar,
            // projeto conceitual e projeto básico — o tipo do documento não é dedutível do
            // .docx, então só cobramos quando o profile do cliente declara a exigência.
            var exige = string.Equals(ctx.Profile.Get("tarja.exigeNaoValidoExecucao"), "true",
                                      StringComparison.OrdinalIgnoreCase);
            if (!exige)
            {
                return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                    Array.Empty<Violation>(),
                    Note: "Exigência da tarja 'Não é Válido para Execução' depende do tipo de projeto; " +
                          "defina 'tarja.exigeNaoValidoExecucao=true' no profile para cobrá-la."));
            }

            var tarjas = DocumentoTexto.Lista(ctx.Profile.Get("tarja.naoValidoExecucao"), PadraoTarjaNaoValido);
            if (!ContemAlguma(textoNorm, tarjas))
            {
                violations.Add(new Violation(Ref.ToString(), Severity.Error,
                    $"Revisão '{revisao}' é emissão final/subsequente, mas não foi localizada a tarja " +
                    $"({string.Join(" / ", tarjas)}).",
                    new ViolationLocation(null, null, null, "Tarja de emissão")));
            }
            nota = $"Revisão '{revisao}' — emissão final/subsequente.";
        }

        var status = violations.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations, Note: nota));
    }

    /// <summary>
    /// Onde a tarja aparece. Cabeçalhos e rodapés vêm primeiro porque é onde a tarja obsoleta
    /// costuma sobreviver — e porque só ali dá para devolver seção e tipo, que é o que permite
    /// ancorar o comentário perto do problema em vez de no início do documento.
    /// </summary>
    private static (string Texto, string Origem, string? Kind, int? Secao)? LocalizarTarja(
        DocumentContext ctx, IReadOnlyList<string> tarjas)
    {
        foreach (var hf in ctx.Structure.Headers)
        {
            var achada = Casar(hf.Text, tarjas);
            if (achada is not null)
                return (achada, DocumentoTexto.NomeDaParte("cabeçalho", hf.Kind, hf.SectionIndex), hf.Kind, hf.SectionIndex);
        }
        foreach (var hf in ctx.Structure.Footers)
        {
            var achada = Casar(hf.Text, tarjas);
            if (achada is not null)
                return (achada, DocumentoTexto.NomeDaParte("rodapé", hf.Kind, hf.SectionIndex), hf.Kind, hf.SectionIndex);
        }

        // No corpo a tarja também aparece na descrição das revisões anteriores, que é registro
        // histórico legítimo — por isso só o texto de fora do quadro conta.
        foreach (var p in ctx.Structure.Paragraphs.Where(p => !p.IsInTable))
        {
            var achada = Casar(p.Text, tarjas);
            if (achada is not null) return (achada, "corpo do documento", null, p.SectionIndex);
        }

        return null;
    }

    private static (string Texto, string Origem, string? Kind, int? Secao)? LocalizarTarjaOculta(
        DocumentContext ctx, IReadOnlyList<string> tarjas)
    {
        foreach (var hf in ctx.Structure.HiddenHeaderFooters)
        {
            var achada = Casar(hf.Text, tarjas);
            if (achada is not null)
                return (achada, DocumentoTexto.NomeDaParte("cabeçalho/rodapé", hf.Kind, hf.SectionIndex), hf.Kind, hf.SectionIndex);
        }
        return null;
    }

    private static readonly Regex Data = new(@"\b\d{1,2}/\d{1,2}/\d{2,4}\b", RegexOptions.Compiled);

    /// <summary>
    /// Texto em que a presença da tarja conta: cabeçalhos e rodapés visíveis, corpo fora de
    /// tabela e linhas de tabela que não são registro de revisão.
    /// <para>
    /// O histórico de revisões descreve emissões anteriores ("0B | 11/04/25 | … | Emissão
    /// para comentários do cliente.") e antes bastava para dar a tarja como presente: um
    /// documento em 0B sem tarja nenhuma era aprovado pela descrição da própria revisão. Uma
    /// linha com data é registro histórico, não carimbo.
    /// </para>
    /// </summary>
    private static string TextoOndeATarjaValeComo(DocumentStructure doc)
    {
        var linhasSemData = doc.Tables
            .SelectMany(t => t.Cells.GroupBy(c => c.Row))
            .Select(linha => string.Join(" ", linha.OrderBy(c => c.Column).Select(c => c.Text)))
            .Where(texto => !Data.IsMatch(texto));

        return string.Join("\n",
            doc.Headers.Select(h => h.Text)
               .Concat(doc.Footers.Select(f => f.Text))
               .Concat(doc.Paragraphs.Where(p => !p.IsInTable).Select(p => p.Text))
               .Concat(linhasSemData)
               .Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static string? Casar(string? texto, IReadOnlyList<string> tarjas)
    {
        var norm = DocumentoTexto.Normalizar(texto);
        if (norm.Length == 0) return null;

        foreach (var t in tarjas)
        {
            var alvo = DocumentoTexto.Normalizar(t);
            if (alvo.Length > 0 && norm.Contains(alvo, StringComparison.Ordinal)) return t;
        }
        return null;
    }

    private static bool ContemAlguma(string textoNormalizado, IEnumerable<string> termos) =>
        termos.Select(DocumentoTexto.Normalizar)
              .Where(t => t.Length > 0)
              .Any(t => textoNormalizado.Contains(t, StringComparison.Ordinal));

    private static string? RevisaoDoDocumento(DocumentContext ctx)
    {
        var titulosAlias = DocumentoTexto.Lista(ctx.Profile.Get("quadroCaracteristicas.aliases"),
            "Características do Documento|Quadro de Características|Características");
        var rotulos = DocumentoTexto.Lista(ctx.Profile.Get("revisao.aliasesRotulo"), "Revisão|Rev.|Rev");

        var table = QuadroCaracteristicas.Find(ctx.Structure, titulosAlias);
        if (table is null) return null;

        // Fonte primária: a última entrada do histórico de revisões. As buscas por rótulo e
        // por coluna abaixo continuam como fallback para quadros com layout diferente, mas
        // não podem vir antes — a primeira linha rotulada "Rev." de um quadro no padrão MRN
        // é o cabeçalho da grade de folhas, e ela devolve um código de coluna, não a revisão.
        var vigente = QuadroCaracteristicas.RevisaoVigente(table);
        if (!string.IsNullOrWhiteSpace(vigente)) return vigente;

        var byLabel = QuadroCaracteristicas.FieldsByLabel(table);
        var revisao = byLabel
            .Where(kv => rotulos.Any(r => kv.Key.Contains(r, StringComparison.OrdinalIgnoreCase)))
            .Select(kv => kv.Value)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        if (string.IsNullOrWhiteSpace(revisao))
        {
            var col = QuadroCaracteristicas.FieldsByColumn(table,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["revisao"] = rotulos
                });
            revisao = col?.GetValueOrDefault("revisao");
        }

        if (string.IsNullOrWhiteSpace(revisao)) return null;
        var m = TokenRevisao.Match(revisao);
        return m.Success ? m.Value.ToUpperInvariant() : null;
    }
}
