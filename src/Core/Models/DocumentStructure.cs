namespace WordComplianceValidator.Core.Models;

public sealed record ExtractedStyle(
    string StyleId,
    string Name,
    string? Font,
    double? Size,
    bool? Bold,
    bool? Italic,
    string? Alignment);

public sealed record ExtractedHeaderFooter(
    string Kind,
    string Text,
    int ImageCount);

public sealed record ExtractedSection(
    int Index,
    double? PageWidth,
    double? PageHeight,
    double? MarginTop,
    double? MarginBottom,
    double? MarginLeft,
    double? MarginRight);

public sealed record ExtractedParagraph(
    string ParagraphId,
    string? StyleId,
    string Text);

public sealed record ExtractedTableCell(
    int Row,
    int Column,
    string Text);

public sealed record ExtractedTable(
    int Index,
    IReadOnlyList<ExtractedTableCell> Cells,
    string? FirstRowText);

public sealed record DocumentStructure(
    string? FileName,
    IReadOnlyDictionary<string, ExtractedStyle> Styles,
    IReadOnlyList<ExtractedHeaderFooter> Headers,
    IReadOnlyList<ExtractedHeaderFooter> Footers,
    IReadOnlyList<ExtractedSection> Sections,
    IReadOnlyList<ExtractedParagraph> Paragraphs,
    IReadOnlyList<ExtractedTable> Tables,
    bool HasPendingTrackChanges,
    bool HasOpenComments,
    bool HasUpdatedToc);
