using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.3 (letra i) — Coerência e rastreabilidade das revisões: revisões PdA e Cliente
/// atualizadas, correspondentes entre si e registradas na folha de rosto e no quadro.
/// <para>
/// A PdA e o Cliente numeram revisões em sistemas diferentes (0A, 0B, 00… × 0, 1, 2…), então
/// a comparação é por <b>correspondência</b>, não por igualdade de código:
/// </para>
/// <list type="bullet">
/// <item>o sufixo da codificação PdA do quadro ("RN-816-RL-67456-<b>00</b>") é a revisão PdA
/// vigente do histórico do quadro;</item>
/// <item>o sufixo da codificação do Cliente ("…-RT-<b>1</b>") é a última revisão da folha
/// índice do Cliente;</item>
/// <item>cada emissão ao Cliente tem uma revisão PdA de mesma data, e a revisão PdA vigente
/// tem emissão correspondente na folha índice.</item>
/// </list>
/// <para>
/// A versão anterior procurava "Rev. NN" nos 30 primeiros parágrafos e comparava com o valor
/// do quadro lido por rótulo. Nos documentos de referência não achava nada e pulava — e, se
/// achasse, compararia a revisão do Cliente (1) com a da PdA (00), que nunca são iguais.
/// </para>
/// </summary>
public sealed class CoerenciaRevisoesCheck : IRuleCheck
{
    private static readonly Regex RevisionToken = new(@"\b(0[A-Za-z]|\d{2})\b", RegexOptions.Compiled);
    // Revisão em contexto explícito ("Rev. 01", "Revisão: 0B"), para o quadro sem histórico.
    private static readonly Regex RevisaoEmContexto = new(
        @"(?:rev(?:is[ãa]o)?\.?\s*[:\-]?\s*)(0[A-Za-z]|\d{2})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public CoerenciaRevisoesCheck(ChecklistPadrao padrao = ChecklistPadrao.Cliente)
    {
        Ref = new ChecklistRef("PS-002", "4.3.3 (letra i)", padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var titulosAlias = DocumentoTexto.Lista(ctx.Profile.Get("quadroCaracteristicas.aliases"),
            "Características do Documento|Quadro de Características|Características");

        var quadro = QuadroCaracteristicas.Find(ctx.Structure, titulosAlias);
        if (quadro is null)
            return Pular("Quadro 'Características do Documento' não localizado.");

        var historicoPda = QuadroCaracteristicas.HistoricoDeRevisoes(quadro);
        if (historicoPda.Count == 0)
            return QuadroSemHistorico(ctx, quadro);

        var vigente = historicoPda[^1];
        var historicoCliente = QuadroCaracteristicas.HistoricoDaFolhaDeRosto(ctx.Structure, quadro);
        var celulasQuadro = quadro.Cells.Select(c => c.Text).ToList();
        var violations = new List<Violation>();
        var verificados = new List<string>();

        // 1) Sufixo da codificação PdA × revisão PdA vigente.
        var pdaRx = ctx.Profile.Get("codificacao.pdaRegex");
        if (Codificacao.EncontrarEmQualquer(celulasQuadro, pdaRx) is { Sufixo: { } sufixoPda } codigoPda)
        {
            verificados.Add($"codificação PdA {codigoPda.Completo}");
            if (!sufixoPda.Equals(vigente.Revisao, StringComparison.OrdinalIgnoreCase))
                violations.Add(Erro(
                    $"A codificação PdA do quadro ('{codigoPda.Completo}') indica a revisão {sufixoPda}, " +
                    $"mas o histórico do quadro está na revisão {vigente.Revisao}."));
        }

        // 2) Sufixo da codificação do Cliente × última revisão da folha índice.
        var clienteRx = ctx.Profile.Get("codificacao.clienteRegex");
        if (historicoCliente.Count > 0
            && Codificacao.EncontrarEmQualquer(celulasQuadro, clienteRx) is { Sufixo: { } sufixoCliente } codigoCliente)
        {
            verificados.Add($"codificação Cliente {codigoCliente.Completo}");
            var ultimaCliente = historicoCliente[^1].Revisao;
            if (!MesmaRevisao(sufixoCliente, ultimaCliente))
                violations.Add(Erro(
                    $"A codificação do Cliente no quadro ('{codigoCliente.Completo}') indica a revisão " +
                    $"{sufixoCliente}, mas a última revisão registrada na folha de rosto é {ultimaCliente}."));
        }

        // 3) Correspondência por data entre as emissões ao Cliente e as revisões PdA.
        if (historicoCliente.Count > 0)
        {
            verificados.Add($"{historicoCliente.Count} emissão(ões) da folha de rosto × {historicoPda.Count} revisão(ões) do quadro");
            var datasPda = historicoPda.Select(e => e.DataLida).OfType<DateOnly>().ToHashSet();

            foreach (var emissao in historicoCliente)
            {
                if (emissao.DataLida is { } data && datasPda.Count > 0 && !datasPda.Contains(data))
                    violations.Add(Aviso(
                        $"A revisão {emissao.Revisao} da folha de rosto ({emissao.Data}) não tem revisão PdA " +
                        "de mesma data no quadro Características."));
            }

            var datasCliente = historicoCliente.Select(e => e.DataLida).OfType<DateOnly>().ToHashSet();
            if (vigente.DataLida is { } dataVigente && datasCliente.Count > 0 && !datasCliente.Contains(dataVigente))
                violations.Add(Aviso(
                    $"A revisão PdA vigente ({vigente.Revisao}, {vigente.Data}) não aparece na folha de rosto: " +
                    "nenhuma emissão ao Cliente tem essa data."));
        }

        if (verificados.Count == 0)
            return Pular("Sem codificação com sufixo de revisão nem folha índice do Cliente para comparar.");

        var status = violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : violations.Count > 0 ? CheckStatus.Skipped
                   : CheckStatus.Passed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations,
            Note: $"Revisão PdA vigente {vigente.Revisao}; verificados: {string.Join("; ", verificados)}."));

        Violation Erro(string m) => new(Ref.ToString(), Severity.Error, m,
            new ViolationLocation(null, null, null, "Folha de Rosto × Quadro"));
        Violation Aviso(string m) => new(Ref.ToString(), Severity.Warning, m,
            new ViolationLocation(null, null, null, "Folha de Rosto × Quadro"));
    }

    /// <summary>
    /// Quadro rótulo→valor, sem histórico: a revisão do campo "Revisão" tem de aparecer em
    /// contexto explícito ("Rev. 01") na folha de rosto.
    /// </summary>
    private Task<RuleCheckResult> QuadroSemHistorico(DocumentContext ctx, ExtractedTable quadro)
    {
        var revAliases = DocumentoTexto.Lista(ctx.Profile.Get("revisao.aliasesRotulo"), "Revisão|Rev.|Rev");
        var byLabel = QuadroCaracteristicas.FieldsByLabel(table: quadro);
        var bruto = revAliases
            .Select(a => byLabel.FirstOrDefault(kv => kv.Key.Contains(a, StringComparison.OrdinalIgnoreCase)).Value)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        var m = RevisionToken.Match(bruto ?? string.Empty);
        if (!m.Success)
            return Pular("Não foi possível extrair a revisão do quadro Características.");
        var revQuadro = m.Value.ToUpperInvariant();

        var folhaTexto = string.Join(" ", DocumentoTexto.FolhaDeRosto(ctx.Structure).Select(p => p.Text));
        var folhaRevs = RevisaoEmContexto.Matches(folhaTexto)
            .Select(x => x.Groups[1].Value.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (folhaRevs.Count == 0)
            return Pular("Nenhuma revisão localizada na folha de rosto — não foi possível comparar.");

        if (folhaRevs.Contains(revQuadro))
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Passed, Array.Empty<Violation>()));

        return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Failed,
        [
            new Violation(Ref.ToString(), Severity.Error,
                $"Revisão do quadro Características ('{revQuadro}') não consta na folha de rosto " +
                $"(revisões vistas: {string.Join(", ", folhaRevs)}).",
                new ViolationLocation(null, null, null, "Folha de Rosto × Quadro"))
        ]));
    }

    private Task<RuleCheckResult> Pular(string nota) =>
        Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped, Array.Empty<Violation>(), Note: nota));

    /// <summary>"1" e "01" são a mesma revisão do Cliente.</summary>
    private static bool MesmaRevisao(string a, string b) =>
        int.TryParse(a, out var x) && int.TryParse(b, out var y)
            ? x == y
            : a.Equals(b, StringComparison.OrdinalIgnoreCase);
}
