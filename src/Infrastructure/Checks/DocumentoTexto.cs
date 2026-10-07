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

    /// <summary>
    /// Parágrafos da folha de rosto / capa: do início do documento até o primeiro título de
    /// nível 1 ou a primeira entrada de índice, o que vier antes. Inclui os parágrafos de
    /// célula das tabelas da capa.
    /// <para>
    /// Antes cada check cortava num número fixo de parágrafos (30 ou 60). Nos documentos de
    /// referência a folha índice do Cliente sozinha ocupa ~200 parágrafos de célula, e a folha
    /// de rosto da PdA — com código, título e data — começa depois deles: os checks não a
    /// viam e "passavam" encontrando a codificação nos cabeçalhos. O limite agora só vale para
    /// documento sem título nem índice, onde não há fronteira a seguir.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ExtractedParagraph> FolhaDeRosto(DocumentStructure doc, int limiteSemFronteira = 600)
    {
        var lista = new List<ExtractedParagraph>();
        foreach (var p in doc.Paragraphs)
        {
            // Título vazio não é fronteira: o RN799 tem dois parágrafos vazios com nível 1 no
            // meio da capa, e cortar ali escondia a codificação da folha de rosto.
            var titulo = p.OutlineLevel == 0 && !string.IsNullOrWhiteSpace(p.Text);
            if (titulo || EhEntradaIndice(p)) return lista;
            lista.Add(p);
        }
        // Sem título nem índice o "fim da capa" não é observável: vale o limite.
        return lista.Take(Math.Max(1, limiteSemFronteira)).ToList();
    }

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

    /// <summary>
    /// Nome do alinhamento como aparece no Word ("justificado"), não o valor do OOXML ("both").
    /// As mensagens viram comentários lidos por quem edita o documento.
    /// </summary>
    public static string NomeDoAlinhamento(string? ooxml) => (ooxml ?? string.Empty).ToLowerInvariant() switch
    {
        "both" => "justificado",
        "center" => "centralizado",
        "right" or "end" => "à direita",
        "left" or "start" or "" => "à esquerda",
        "distribute" => "distribuído",
        var outro => outro
    };

    /// <summary>
    /// "cabeçalho das páginas pares da seção 3" a partir do tipo OOXML ("default", "first",
    /// "even"). A mensagem vai para quem edita no Word, que não conhece esses nomes.
    /// </summary>
    public static string NomeDaParte(string parte, string? tipo, int? secao)
    {
        var qual = (tipo ?? string.Empty).ToLowerInvariant() switch
        {
            "first" => " da primeira página",
            "even" => " das páginas pares",
            _ => ""
        };
        return secao is null ? parte + qual : $"{parte}{qual} da seção {secao + 1}";
    }

    /// <summary>Lê um parâmetro do profile como lista separada por '|'.</summary>
    public static string[] Lista(string? valor, string padrao) =>
        (string.IsNullOrWhiteSpace(valor) ? padrao : valor)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
