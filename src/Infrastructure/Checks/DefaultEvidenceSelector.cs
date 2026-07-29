using System.Text;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// Pacote de evidências padrão: um retrato estruturado do documento (folha de rosto, quadro
/// Características, estrutura de títulos, cabeçalhos, índice e amostra do corpo) dentro de um
/// orçamento de caracteres.
/// <para>
/// O corpo é amostrado, não enviado inteiro: relatórios reais têm milhares de parágrafos e o
/// custo cresce linearmente com eles. As seções estruturais — que são onde quase todas as
/// regras do CL-001 olham — vão completas.
/// </para>
/// </summary>
public sealed class DefaultEvidenceSelector : IEvidenceSelector
{
    private const int OrcamentoCaracteresDefault = 24_000;
    private const int ParagrafosFolhaRosto = 60;

    private readonly int _orcamento;

    // Este seletor produz o mesmo retrato para todas as regras, então o pacote é montado uma
    // vez por documento em vez de uma vez por regra — são milhares de parágrafos varridos.
    // A tabela é fraca: some junto com o DocumentContext, sem prender o documento em memória.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DocumentContext, string> _cache = new();

    public DefaultEvidenceSelector(int orcamentoCaracteres = OrcamentoCaracteresDefault)
    {
        _orcamento = orcamentoCaracteres;
    }

    public string Build(DocumentContext ctx, ChecklistEntry entry) =>
        _cache.GetValue(ctx, Montar);

    private string Montar(DocumentContext ctx)
    {
        var doc = ctx.Structure;
        var sb = new StringBuilder();

        sb.AppendLine("## Metadados");
        sb.AppendLine($"- Arquivo: {doc.FileName}");
        sb.AppendLine($"- Seções: {doc.Sections.Count} | Parágrafos: {doc.Paragraphs.Count} | Tabelas: {doc.Tables.Count}");
        sb.AppendLine($"- Alterações pendentes (controle de alterações): {(doc.HasPendingTrackChanges ? "sim" : "não")}");
        sb.AppendLine($"- Comentários abertos: {(doc.HasOpenComments ? "sim" : "não")}");
        sb.AppendLine($"- Campos no corpo: {string.Join(", ", doc.BodyFieldCodes.Distinct().Take(15))}");
        if (doc.Properties is { } props)
            sb.AppendLine($"- Propriedades: autor={props.Creator}; modificado por={props.LastModifiedBy}");
        sb.AppendLine();

        AppendFolhaRosto(sb, doc);
        AppendQuadroCaracteristicas(sb, ctx);
        AppendCabecalhosRodapes(sb, doc);
        AppendEstruturaTitulos(sb, doc);
        AppendIndice(sb, doc);
        AppendTabelas(sb, doc);
        AppendAmostraCorpo(sb, doc, RestanteOrcamento(sb));

        return sb.ToString();
    }

    private int RestanteOrcamento(StringBuilder sb) => Math.Max(0, _orcamento - sb.Length);

    private static void AppendFolhaRosto(StringBuilder sb, DocumentStructure doc)
    {
        var folha = doc.Paragraphs
            .TakeWhile(p => p.OutlineLevel is not 0)
            .Take(ParagrafosFolhaRosto)
            .Where(p => !string.IsNullOrWhiteSpace(p.Text))
            .Select(p => p.Text.Trim())
            .ToList();

        if (folha.Count == 0) return;
        sb.AppendLine("## Folha de rosto / capa (parágrafos anteriores ao primeiro título)");
        foreach (var t in folha) sb.AppendLine($"- {t}");
        sb.AppendLine();
    }

    private static void AppendQuadroCaracteristicas(StringBuilder sb, DocumentContext ctx)
    {
        var aliases = DocumentoTexto.Lista(ctx.Profile.Get("quadroCaracteristicas.aliases"),
            "Características do Documento|Quadro de Características|Características");
        var quadro = QuadroCaracteristicas.Find(ctx.Structure, aliases);
        if (quadro is null) return;

        sb.AppendLine("## Quadro \"Características do Documento\"");
        foreach (var (label, valor) in QuadroCaracteristicas.FieldsByLabel(quadro))
            sb.AppendLine($"- {label}: {valor}");
        sb.AppendLine();
    }

    private static void AppendCabecalhosRodapes(StringBuilder sb, DocumentStructure doc)
    {
        if (doc.Headers.Count == 0 && doc.Footers.Count == 0) return;
        sb.AppendLine("## Cabeçalhos e rodapés");
        foreach (var h in doc.Headers)
            sb.AppendLine($"- [cabeçalho tipo={h.Kind} seção={h.SectionIndex} imagens={h.ImageCount} " +
                          $"campos={string.Join("/", h.FieldCodes.Distinct())}] {Resumo(h.Text, 300)}");
        foreach (var f in doc.Footers)
            sb.AppendLine($"- [rodapé tipo={f.Kind} seção={f.SectionIndex} imagens={f.ImageCount} " +
                          $"campos={string.Join("/", f.FieldCodes.Distinct())}] {Resumo(f.Text, 300)}");
        sb.AppendLine();
    }

    private static void AppendEstruturaTitulos(StringBuilder sb, DocumentStructure doc)
    {
        var titulos = DocumentoTexto.Titulos(doc);
        if (titulos.Count == 0) return;

        sb.AppendLine("## Estrutura de títulos (painel de navegação)");
        foreach (var t in titulos.Take(120))
            sb.AppendLine($"{new string(' ', (t.OutlineLevel ?? 0) * 2)}- (nível {t.OutlineLevel + 1}) {Resumo(t.Text, 160)}");
        if (titulos.Count > 120) sb.AppendLine($"  … (+{titulos.Count - 120} títulos)");
        sb.AppendLine();
    }

    private static void AppendIndice(StringBuilder sb, DocumentStructure doc)
    {
        var entradas = DocumentoTexto.EntradasIndice(doc);
        if (entradas.Count == 0)
        {
            sb.AppendLine("## Índice");
            sb.AppendLine("- Nenhuma entrada de índice localizada no documento.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"## Índice ({entradas.Count} entradas)");
        foreach (var e in entradas.Take(80))
            sb.AppendLine($"- {Resumo(e.Text, 160)}");
        if (entradas.Count > 80) sb.AppendLine($"- … (+{entradas.Count - 80} entradas)");
        sb.AppendLine();
    }

    private static void AppendTabelas(StringBuilder sb, DocumentStructure doc)
    {
        if (doc.Tables.Count == 0) return;
        sb.AppendLine($"## Tabelas ({doc.Tables.Count})");
        foreach (var t in doc.Tables.Take(20))
            sb.AppendLine($"- Tabela {t.Index}: {Resumo(t.FirstRowText ?? "(sem cabeçalho)", 200)}");
        if (doc.Tables.Count > 20) sb.AppendLine($"- … (+{doc.Tables.Count - 20} tabelas)");
        sb.AppendLine();
    }

    private static void AppendAmostraCorpo(StringBuilder sb, DocumentStructure doc, int orcamento)
    {
        if (orcamento <= 500) return;

        var corpo = DocumentoTexto.Corpo(doc);
        if (corpo.Count == 0) return;

        sb.AppendLine("## Amostra do corpo do documento");
        var usados = 0;
        var incluidos = 0;
        foreach (var p in corpo)
        {
            var linha = $"- [{p.EffectiveFont} {p.EffectiveFontSize:0.#}pt, {p.Alignment}] {Resumo(p.Text, 400)}";
            if (usados + linha.Length > orcamento) break;
            sb.AppendLine(linha);
            usados += linha.Length;
            incluidos++;
        }
        if (incluidos < corpo.Count)
            sb.AppendLine($"- … (+{corpo.Count - incluidos} parágrafos de corpo omitidos por orçamento)");
        sb.AppendLine();
    }

    private static string Resumo(string? texto, int max)
    {
        var t = (texto ?? string.Empty).Trim().Replace("\n", " ").Replace("\r", " ");
        return t.Length <= max ? t : t[..max] + "…";
    }
}
