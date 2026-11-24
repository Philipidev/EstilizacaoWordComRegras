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
    int ImageCount,
    IReadOnlyList<string>? FieldCodes = null,
    // Alinhamento de cada parágrafo do header/footer (ex.: "Right"), na ordem do XML.
    IReadOnlyList<string>? ParagraphAlignments = null,
    // Índice da primeira seção que referencia este header/footer.
    int? SectionIndex = null)
{
    public IReadOnlyList<string> FieldCodes { get; init; } =
        FieldCodes ?? Array.Empty<string>();

    public IReadOnlyList<string> ParagraphAlignments { get; init; } =
        ParagraphAlignments ?? Array.Empty<string>();

    /// <summary>
    /// <c>false</c> quando o Word nunca exibe esta parte: 'first' numa seção sem "primeira
    /// página diferente", 'even' sem "pares e ímpares diferentes", ou parte não referenciada.
    /// </summary>
    public bool Visivel { get; init; } = true;
}

public sealed record ExtractedSection(
    int Index,
    double? PageWidth,
    double? PageHeight,
    double? MarginTop,
    double? MarginBottom,
    double? MarginLeft,
    double? MarginRight,
    // w:titlePg — "primeira página diferente": a 1ª página da seção usa o cabeçalho 'first'.
    bool TitlePage = false)
{
    /// <summary>Cabeçalhos e rodapés exibidos em alguma página da seção, herança incluída.</summary>
    public IReadOnlyList<ExtractedHeaderFooter> DisplayedHeaderFooters { get; init; } = Array.Empty<ExtractedHeaderFooter>();

    /// <summary>Cabeçalho e rodapé da primeira página da seção.</summary>
    public IReadOnlyList<ExtractedHeaderFooter> FirstPageHeaderFooters { get; init; } = Array.Empty<ExtractedHeaderFooter>();
}

public sealed record ExtractedParagraph(
    string ParagraphId,
    string? StyleId,
    string Text,
    bool EndsWithPageBreak = false,
    // Alinhamento efetivo ("Left"/"Center"/"Right"/"Both"), direto ou herdado do estilo.
    string? Alignment = null,
    // Nível de outline 0..8 (0 = Título 1). null em parágrafos de corpo.
    int? OutlineLevel = null,
    // Id da lista numerada (w:numId), quando o parágrafo pertence a uma.
    int? NumberingId = null,
    // Fonte predominante, resolvendo formatação direta do run sobre o estilo.
    string? EffectiveFont = null,
    // Tamanho predominante em pontos, resolvendo formatação direta sobre o estilo.
    double? EffectiveFontSize = null,
    // Imagens ancoradas neste parágrafo (Drawing + VML).
    int ImageCount = 0,
    // Índice da seção a que o parágrafo pertence.
    int SectionIndex = 0,
    // true quando o parágrafo está dentro de uma célula de tabela.
    bool IsInTable = false,
    // Índice da tabela (em <see cref="DocumentStructure.Tables"/>) que contém este parágrafo;
    // null fora de tabela. Permite reconstituir as linhas/colunas de origem em vez de tratar
    // cada célula como um parágrafo solto.
    int? TableIndex = null,
    // Campos OOXML do parágrafo (PAGEREF, TOC, REF, SEQ…). Entradas de índice carregam
    // PAGEREF, o que as distingue de parágrafos de corpo com texto parecido.
    IReadOnlyList<string>? FieldCodes = null)
{
    public IReadOnlyList<string> FieldCodes { get; init; } =
        FieldCodes ?? Array.Empty<string>();

    /// <summary>Quebra de página explícita depois de algum texto do parágrafo (o resto vai para a página seguinte).</summary>
    public bool PageBreakAfterText { get; init; }

    /// <summary>
    /// O parágrafo começou numa página nova na última vez que o Word o paginou
    /// (<c>w:lastRenderedPageBreak</c> antes de qualquer texto). É o único vestígio de layout
    /// que o OOXML guarda.
    /// </summary>
    public bool RenderedPageBreakAtStart { get; init; }

    /// <summary>
    /// Quantos <c>w:lastRenderedPageBreak</c> o parágrafo tem, em qualquer posição. Cada um é
    /// um início de página na última paginação do Word; somados sobre uma tabela, dizem
    /// quantas páginas ela ocupa.
    /// </summary>
    public int RenderedPageBreaks { get; init; }
}

public sealed record ExtractedTableCell(
    int Row,
    int Column,
    string Text,
    // Imagens dentro da célula (Drawing + VML). Campos de formulário preenchidos por logo ou
    // assinatura digitalizada têm texto vazio mas não estão em branco — sem esta contagem,
    // "célula sem texto" e "campo não preenchido" viram a mesma coisa.
    int ImageCount = 0)
{
    /// <summary>
    /// Coluna da grade da tabela (<c>w:tblGrid</c>) em que a célula começa. Difere de
    /// <see cref="Column"/>, que é a posição física na linha, quando há células mescladas na
    /// horizontal (<c>w:gridSpan</c>) — caso do cabeçalho do histórico do FR-115-01, em que
    /// "Iniciais" cobre as colunas de elaborador, verificador e aprovador.
    /// </summary>
    public int GridColumn { get; init; } = Column;

    // Atenção: o default acima é fixado na construção; um "with { Column = n }" não o recalcula.

    /// <summary>Quantas colunas da grade a célula ocupa (<c>w:gridSpan</c>).</summary>
    public int GridSpan { get; init; } = 1;
}

public sealed record ExtractedTable(
    int Index,
    IReadOnlyList<ExtractedTableCell> Cells,
    string? FirstRowText)
{
    /// <summary>
    /// Índices (em <see cref="DocumentStructure.Tables"/>) das tabelas que formam esta. Uma
    /// tabela comum tem só o próprio <see cref="Index"/>; o quadro "Características" do modelo
    /// FR-115-01 vem em duas tabelas consecutivas (título/codificação e apêndices/grade/
    /// histórico) que são lidas como uma só.
    /// </summary>
    public IReadOnlyList<int> Partes { get; init; } = new[] { Index };

    /// <summary>O parágrafo da tabela <paramref name="tableIndex"/> pertence a esta?</summary>
    public bool Contem(int? tableIndex) => tableIndex is { } i && Partes.Contains(i);
}

/// <summary>
/// Referência cruzada que aponta para um indicador inexistente. O Word só mostra
/// "Erro! Indicador não definido" depois de atualizar os campos; até lá o texto em cache
/// parece correto, e é isso que este registro pega.
/// </summary>
public sealed record BrokenFieldReference(string ParagraphId, string Field, string Bookmark)
{
    /// <summary>
    /// O que o Word mostra hoje no lugar do campo (resultado em cache, ex.: "Figura 92"). Nulo
    /// quando o campo não tem resultado — fica invisível até os campos serem atualizados.
    /// </summary>
    public string? DisplayedText { get; init; }

    /// <summary>Texto do parágrafo antes do campo, para dizer onde ele está.</summary>
    public string TextBefore { get; init; } = string.Empty;
}

/// <summary>Propriedades do pacote OOXML (aba "Propriedades" do Word).</summary>
public sealed record ExtractedDocumentProperties(
    string? Title,
    string? Subject,
    string? Creator,
    string? LastModifiedBy,
    string? Revision,
    DateTime? Created,
    DateTime? Modified)
{
    /// <summary>
    /// Total de páginas que o Word gravou em <c>docProps/app.xml</c> no último salvamento.
    /// Inclui todas as páginas (capa e quadro "Características" também). Nulo quando o pacote
    /// não traz a propriedade — documentos gerados por ferramenta que não pagina.
    /// </summary>
    public int? Pages { get; init; }
}

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
    bool HasUpdatedToc,
    IReadOnlyList<string>? BodyFieldCodes = null,
    ExtractedDocumentProperties? Properties = null,
    // Cabeçalhos e rodapés que existem no pacote mas o Word não exibe. Headers/Footers trazem
    // só os visíveis; estes ficam à parte para avisos sobre conteúdo latente.
    IReadOnlyList<ExtractedHeaderFooter>? HiddenHeaderFooters = null,
    // Campos REF/PAGEREF/NOTEREF do corpo cujo indicador (bookmark) não existe no documento.
    IReadOnlyList<BrokenFieldReference>? BrokenReferences = null)
{
    public IReadOnlyList<BrokenFieldReference> BrokenReferences { get; init; } =
        BrokenReferences ?? Array.Empty<BrokenFieldReference>();

    public IReadOnlyList<string> BodyFieldCodes { get; init; } =
        BodyFieldCodes ?? Array.Empty<string>();

    public IReadOnlyList<ExtractedHeaderFooter> HiddenHeaderFooters { get; init; } =
        HiddenHeaderFooters ?? Array.Empty<ExtractedHeaderFooter>();
}
