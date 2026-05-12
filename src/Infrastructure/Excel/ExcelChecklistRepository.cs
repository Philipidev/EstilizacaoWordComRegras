using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using WordComplianceValidator.Core.Checklist;

namespace WordComplianceValidator.Infrastructure.Excel;

public sealed class ExcelChecklistRepository : IChecklistRepository
{
    private const string SheetName = "WORD";

    // Column indices (1-based) for the CL-001 layout:
    // A=PS  B=Revisão  C=Título procedimento  D=Assunto  E=Item/Subitem
    // F=Título regra  G=Padrão  H=Descrição  I=IA?  K=Observação
    private const int ColPs = 1, ColRev = 2, ColProcTitle = 3, ColAssunto = 4,
                      ColItem = 5, ColTitulo = 6, ColPadrao = 7, ColDescricao = 8,
                      ColIa = 9, ColObs = 11;

    public Task<IReadOnlyList<ChecklistEntry>> LoadAsync(string source, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(source))
            throw new FileNotFoundException("Checklist .xlsx não encontrado.", source);

        using var doc = SpreadsheetDocument.Open(source, isEditable: false);
        var wbPart = doc.WorkbookPart
            ?? throw new InvalidOperationException("Workbook sem WorkbookPart.");
        var sheet = wbPart.Workbook.Sheets?.Elements<Sheet>()
                       .FirstOrDefault(s => string.Equals(s.Name?.Value, SheetName, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"Sheet '{SheetName}' não encontrada.");
        var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!);
        var sst = wbPart.SharedStringTablePart?.SharedStringTable;

        var entries = new List<ChecklistEntry>();
        foreach (var row in wsPart.Worksheet.Descendants<Row>())
        {
            if (row.RowIndex is null || row.RowIndex.Value <= 2) continue; // skip title + header rows

            var ps = GetCellString(row, ColPs, sst);
            if (string.IsNullOrWhiteSpace(ps)) continue;

            var rev = ParseInt(GetCellString(row, ColRev, sst));
            var procTitle = GetCellString(row, ColProcTitle, sst);
            var assunto = GetCellString(row, ColAssunto, sst);
            var itemRaw = GetCellString(row, ColItem, sst);
            var titulo = GetCellString(row, ColTitulo, sst);
            var padraoRaw = GetCellString(row, ColPadrao, sst);
            var descricao = GetCellString(row, ColDescricao, sst);
            var iaRaw = GetCellString(row, ColIa, sst);
            var obs = GetCellString(row, ColObs, sst);

            var padrao = ParsePadrao(padraoRaw);
            if (padrao is null) continue;

            var iaAuto = string.Equals(iaRaw?.Trim(), "Sim", StringComparison.OrdinalIgnoreCase);
            var item = ChecklistRef.NormalizeItem(itemRaw ?? string.Empty);

            entries.Add(new ChecklistEntry(
                Ref: new ChecklistRef(ps.Trim(), item, padrao.Value),
                Revisao: rev,
                ProcedimentoTitulo: (procTitle ?? string.Empty).Trim(),
                Assunto: (assunto ?? string.Empty).Trim(),
                Titulo: (titulo ?? string.Empty).Trim(),
                Descricao: (descricao ?? string.Empty).Trim(),
                IaAutomatizavel: iaAuto,
                Observacao: string.IsNullOrWhiteSpace(obs) ? null : obs.Trim()));
        }
        return Task.FromResult<IReadOnlyList<ChecklistEntry>>(entries);
    }

    private static ChecklistPadrao? ParsePadrao(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "pda" => ChecklistPadrao.Pda,
            "cliente" => ChecklistPadrao.Cliente,
            _ => null
        };

    private static int? ParseInt(string? s) =>
        int.TryParse(s, out var v) ? v : null;

    private static string? GetCellString(Row row, int columnIndex, SharedStringTable? sst)
    {
        var colLetter = ColumnLetter(columnIndex);
        var cellRef = $"{colLetter}{row.RowIndex!.Value}";
        var cell = row.Elements<Cell>().FirstOrDefault(c => c.CellReference?.Value == cellRef);
        if (cell is null) return null;

        if (cell.DataType?.Value == CellValues.SharedString && sst is not null)
        {
            if (int.TryParse(cell.CellValue?.Text, out var idx) && idx < sst.ChildElements.Count)
                return sst.ChildElements[idx].InnerText;
            return null;
        }

        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.InnerText;

        return cell.CellValue?.Text;
    }

    private static string ColumnLetter(int columnIndex)
    {
        // 1->A, 26->Z, 27->AA
        var sb = new System.Text.StringBuilder();
        while (columnIndex > 0)
        {
            int rem = (columnIndex - 1) % 26;
            sb.Insert(0, (char)('A' + rem));
            columnIndex = (columnIndex - 1) / 26;
        }
        return sb.ToString();
    }
}
