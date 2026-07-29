using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// Helpers de leitura de texto e estrutura compartilhados pelos checks. Concentra aqui as
/// consultas que mais de uma regra precisa (texto integral, títulos, corpo) para que os
/// checks não repitam a mesma varredura com critérios levemente diferentes.
/// </summary>
public static class DocumentoTexto
{
    /// <summary>Todo o texto do documento: parágrafos (inclusive de tabelas) + cabeçalhos + rodapés.</summary>
    public static string Integral(DocumentStructure doc) =>
        string.Join("\n",
            doc.Paragraphs.Select(p => p.Text)
               .Concat(doc.Tables.SelectMany(t => t.Cells.Select(c => c.Text)))
               .Concat(doc.Headers.Select(h => h.Text))
               .Concat(doc.Footers.Select(f => f.Text))
               .Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>Títulos do documento (parágrafos com nível de outline e texto), em ordem.</summary>
    public static IReadOnlyList<ExtractedParagraph> Titulos(DocumentStructure doc) =>
        doc.Paragraphs
           .Where(p => p.OutlineLevel is not null && !string.IsNullOrWhiteSpace(p.Text))
           .ToList();

    /// <summary>
    /// Parágrafos de corpo: fora de tabela, com texto e sem nível de outline (não são títulos).
    /// É o conjunto avaliado pelas regras de formatação do corpo do documento.
    /// </summary>
    public static IReadOnlyList<ExtractedParagraph> Corpo(DocumentStructure doc) =>
        doc.Paragraphs
           .Where(p => !p.IsInTable
                    && p.OutlineLevel is null
                    && !string.IsNullOrWhiteSpace(p.Text))
           .ToList();

    /// <summary>
    /// Entradas do índice. O sinal primário é o campo <c>PAGEREF</c>, que toda entrada de
    /// TOC/lista de figuras carrega; o estilo <c>TOC1</c>/<c>TOC2</c> é apenas um fallback,
    /// porque documentos com estilo próprio do cliente não o utilizam.
    /// </summary>
    public static IReadOnlyList<ExtractedParagraph> EntradasIndice(DocumentStructure doc) =>
        doc.Paragraphs
           .Where(p => !string.IsNullOrWhiteSpace(p.Text) && EhEntradaIndice(p))
           .ToList();

    /// <summary>true quando o parágrafo é uma entrada de índice (TOC ou lista de elementos).</summary>
    public static bool EhEntradaIndice(ExtractedParagraph p) =>
        p.FieldCodes.Any(c => c.Equals("PAGEREF", StringComparison.OrdinalIgnoreCase))
        || (p.StyleId ?? string.Empty).StartsWith("toc", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Normaliza texto para comparação tolerante: minúsculas, sem acentos, sem pontuação
    /// e com espaços colapsados. Títulos aparecem no índice com pontilhado e número de
    /// página anexados, então a comparação precisa ser por conteúdo, não literal.
    /// </summary>
    public static string Normalizar(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var decomposed = raw.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (char.IsWhiteSpace(c)) sb.Append(' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Normalização para comparar títulos com entradas de índice. Além de <see cref="Normalizar"/>,
    /// remove sequências longas de dígitos: âncoras de objetos flutuantes deixam resíduo numérico
    /// colado ao texto (ex.: "23380702078355CONTEXTUALIZAÇÃO"), que não existe no índice e faria
    /// a comparação falhar por um defeito de extração, não do documento.
    /// </summary>
    public static string NormalizarTitulo(string? raw) =>
        Normalizar(System.Text.RegularExpressions.Regex.Replace(raw ?? string.Empty, @"\d{5,}", " "));

    /// <summary>Lê um parâmetro do profile como lista separada por '|'.</summary>
    public static string[] Lista(string? valor, string padrao) =>
        (string.IsNullOrWhiteSpace(valor) ? padrao : valor)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
