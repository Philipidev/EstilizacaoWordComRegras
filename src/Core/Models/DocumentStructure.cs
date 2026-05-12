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
    string Text);

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

public sealed record DocumentStructure(
    IReadOnlyDictionary<string, ExtractedStyle> Styles,
    IReadOnlyList<ExtractedHeaderFooter> Headers,
    IReadOnlyList<ExtractedHeaderFooter> Footers,
    IReadOnlyList<ExtractedSection> Sections,
    IReadOnlyList<ExtractedParagraph> Paragraphs,
    int TableCount,
    bool HasPendingTrackChanges,
    bool HasOpenComments);
