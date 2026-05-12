using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// Helper que tenta localizar e ler o quadro "Características do Documento" e
/// extrair valores por rótulo. Estratégia: localizar a tabela cuja primeira linha
/// (ou qualquer linha de cabeçalho) contenha o termo configurado e mapear pares
/// rótulo→valor (rótulo na primeira célula, valor nas demais).
/// </summary>
public static class QuadroCaracteristicas
{
    public static ExtractedTable? Find(DocumentStructure doc, IEnumerable<string> tituloAliases)
    {
        var aliases = tituloAliases.Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
        foreach (var t in doc.Tables)
        {
            var firstRow = t.FirstRowText ?? string.Empty;
            if (aliases.Any(a => firstRow.Contains(a, StringComparison.OrdinalIgnoreCase)))
                return t;
        }

        // Fallback: documents may place the title outside the first row, but this
        // should only run after titled tables had a chance to match.
        foreach (var t in doc.Tables)
        {
            if (aliases.Any(a => t.Cells.Any(c => c.Text.Contains(a, StringComparison.OrdinalIgnoreCase))))
                return t;
        }

        return null;
    }

    public static IReadOnlyDictionary<string, string> FieldsByLabel(ExtractedTable table)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rows = table.Cells.GroupBy(c => c.Row).OrderBy(g => g.Key).ToList();
        foreach (var row in rows)
        {
            var ordered = row.OrderBy(c => c.Column).ToList();
            if (ordered.Count < 2) continue;
            var label = ordered[0].Text.TrimEnd(':').Trim();
            var value = string.Join(" ", ordered.Skip(1).Select(c => c.Text.Trim()).Where(s => s.Length > 0));
            if (label.Length > 0 && !dict.ContainsKey(label))
                dict[label] = value;
        }
        return dict;
    }

    /// <summary>
    /// Detects a column-oriented sub-section inside the quadro whose header row contains
    /// the given column-name aliases. Returns the values from the LAST data row keyed by
    /// matched column alias. Used for revision-history layouts where "Emissor"/"Verificador"
    /// are columns and each row is a revision.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? FieldsByColumn(
        ExtractedTable table,
        IReadOnlyDictionary<string, IReadOnlyList<string>> columnAliases)
    {
        var rows = table.Cells.GroupBy(c => c.Row).OrderBy(g => g.Key).ToList();
        if (rows.Count < 2) return null;

        for (int i = 0; i < rows.Count - 1; i++)
        {
            var headerCells = rows[i].OrderBy(c => c.Column).ToList();
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var aliasSet in columnAliases)
            {
                foreach (var hc in headerCells)
                {
                    if (aliasSet.Value.Any(a => hc.Text.Contains(a, StringComparison.OrdinalIgnoreCase)))
                    {
                        headerMap[aliasSet.Key] = hc.Column;
                        break;
                    }
                }
            }
            if (headerMap.Count < columnAliases.Count) continue;

            // Walk data rows below; pick the LAST row that has non-empty values in all target columns.
            Dictionary<string, string>? best = null;
            for (int r = i + 1; r < rows.Count; r++)
            {
                var dataCells = rows[r].ToDictionary(c => c.Column, c => c.Text);
                var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                bool complete = true;
                foreach (var (key, colIdx) in headerMap)
                {
                    if (!dataCells.TryGetValue(colIdx, out var text) || string.IsNullOrWhiteSpace(text))
                    {
                        complete = false;
                        break;
                    }
                    snapshot[key] = text.Trim();
                }
                if (complete) best = snapshot;
            }
            if (best is not null) return best;
        }
        return null;
    }
}
