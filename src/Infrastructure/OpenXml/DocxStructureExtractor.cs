using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing.Wordprocessing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordComplianceValidator.Core.Abstractions;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.OpenXml;

public sealed class DocxStructureExtractor : IDocxStructureExtractor
{
    public DocumentStructure ExtractFromFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Extract(fs, Path.GetFileName(path));
    }

    public DocumentStructure Extract(Stream docxStream, string? fileName = null)
    {
        using var doc = WordprocessingDocument.Open(docxStream, isEditable: false);
        var main = doc.MainDocumentPart
            ?? throw new InvalidOperationException("Documento .docx sem MainDocumentPart.");

        var styles = ExtractStyles(main);
        var resolver = new StyleResolver(main);
        var paresImpares = PaginasParesEImparesDiferentes(main);
        // Mapeia HeaderReference/FooterReference -> (tipo, seção, visível) por relId.
        var headerKinds = ResolveHeaderFooterKinds<HeaderReference>(main, paresImpares);
        var footerKinds = ResolveHeaderFooterKinds<FooterReference>(main, paresImpares);
        // Parte sem XML raiz (pacote corrompido ou gerado por terceiros) não tem o que exibir.
        var partesCabecalho = main.HeaderParts
            .Select(h => (Id: main.GetIdOfPart(h), Raiz: (OpenXmlPartRootElement?)h.Header))
            .Where(x => x.Raiz is not null)
            .Select(x => (x.Id, Raiz: x.Raiz!))
            .ToList();
        var partesRodape = main.FooterParts
            .Select(f => (Id: main.GetIdOfPart(f), Raiz: (OpenXmlPartRootElement?)f.Footer))
            .Where(x => x.Raiz is not null)
            .Select(x => (x.Id, Raiz: x.Raiz!))
            .ToList();
        var headerIds = partesCabecalho.Select(x => x.Id).ToList();
        var footerIds = partesRodape.Select(x => x.Id).ToList();
        var todosHeaders = ExtractHeaderFooter(
            partesCabecalho.Select(x => (
                Element: x.Raiz,
                Text: TextoComSeparadores(x.Raiz),
                Binding: headerKinds.GetValueOrDefault(x.Id, NaoReferenciado))));
        var todosFooters = ExtractHeaderFooter(
            partesRodape.Select(x => (
                Element: x.Raiz,
                Text: TextoComSeparadores(x.Raiz),
                Binding: footerKinds.GetValueOrDefault(x.Id, NaoReferenciado))));
        var partePorId = new Dictionary<string, ExtractedHeaderFooter>(StringComparer.Ordinal);
        for (var i = 0; i < headerIds.Count; i++) partePorId[headerIds[i]] = todosHeaders[i];
        for (var i = 0; i < footerIds.Count; i++) partePorId[footerIds[i]] = todosFooters[i];

        // Os checks enxergam só o que o Word de fato exibe. Um cabeçalho 'first' numa seção
        // sem "primeira página diferente", ou 'even' sem "pares e ímpares diferentes", fica no
        // pacote mas nunca aparece — e era tratado como visível: o RN-816 (revisão 00) tem a
        // tarja de comentários justamente nesses cabeçalhos mortos e era reprovado por uma
        // tarja que nenhum leitor vê. Os ocultos seguem disponíveis à parte, para quem quiser
        // avisar sobre conteúdo latente.
        var headers = todosHeaders.Where(h => h.Visivel).ToList();
        var footers = todosFooters.Where(f => f.Visivel).ToList();
        var ocultos = todosHeaders.Concat(todosFooters).Where(h => !h.Visivel).ToList();
        var sections = ComPartesExibidas(ExtractSections(main), main, paresImpares, partePorId);
        var paragraphs = ExtractParagraphs(main, resolver);
        var tables = ExtractTables(main);
        var hasRevisions = HasPendingTrackChanges(main);
        var hasComments = HasOpenComments(main);
        var tocUpdated = HasUpdatedToc(main);
        var bodyFields = main.Document?.Body is { } corpoDoDocumento
            ? ExtractFieldCodes(corpoDoDocumento)
            : Array.Empty<string>();
        var properties = ExtractDocumentProperties(doc);

        return new DocumentStructure(
            FileName: fileName,
            Styles: styles,
            Headers: headers,
            Footers: footers,
            Sections: sections,
            Paragraphs: paragraphs,
            Tables: tables,
            HasPendingTrackChanges: hasRevisions,
            HasOpenComments: hasComments,
            HasUpdatedToc: tocUpdated,
            BodyFieldCodes: bodyFields,
            Properties: properties,
            HiddenHeaderFooters: ocultos,
            BrokenReferences: main.Document?.Body is { } corpo ? ReferenciasQuebradas(corpo) : null);
    }

    private static readonly HashSet<string> CamposComIndicador =
        new(StringComparer.OrdinalIgnoreCase) { "REF", "PAGEREF", "NOTEREF" };

    /// <summary>
    /// Campos REF/PAGEREF/NOTEREF cujo indicador não existe. A instrução é remontada a partir
    /// dos <c>w:instrText</c> entre o <c>fldChar begin</c> e o <c>separate</c>, porque o Word
    /// costuma partir o mesmo campo em vários runs (" PAGEREF _Toc12" + "3456 \h ").
    /// </summary>
    private static IReadOnlyList<BrokenFieldReference> ReferenciasQuebradas(Body body)
    {
        var indicadores = Exibidos<BookmarkStart>(body)
            .Select(b => b.Name?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var quebradas = new List<BrokenFieldReference>();
        foreach (var (p, id) in ParagrafosComId(body))
        {
            foreach (var (instrucao, resultado, textoAntes) in Instrucoes(p))
            {
                var partes = instrucao.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length < 2 || !CamposComIndicador.Contains(partes[0])) continue;
                var alvo = partes[1].Trim('"');
                if (alvo.StartsWith('\\') || indicadores.Contains(alvo)) continue;
                quebradas.Add(new BrokenFieldReference(id, partes[0].ToUpperInvariant(), alvo)
                {
                    DisplayedText = string.IsNullOrWhiteSpace(resultado) ? null : resultado.Trim(),
                    TextBefore = textoAntes
                });
            }
        }
        return quebradas;
    }

    private sealed class CampoAberto(string textoAntes)
    {
        public string TextoAntes { get; } = textoAntes;
        public StringBuilder Instrucao { get; } = new();
        public StringBuilder? Resultado { get; set; }
    }

    /// <summary>
    /// Campos do parágrafo: a instrução, o texto que o Word mostra no lugar do campo (o
    /// resultado em cache, ex.: "Figura 77") e o texto que vem antes dele. É o que permite dizer
    /// a quem lê <em>onde</em> está a referência quebrada, em vez de um nome interno de indicador
    /// — e há campos quebrados sem resultado nenhum, invisíveis até a atualização dos campos.
    /// </summary>
    private static IEnumerable<(string Instrucao, string? Resultado, string TextoAntes)> Instrucoes(Paragraph p)
    {
        var abertos = new Stack<CampoAberto>();
        var lido = new StringBuilder();
        foreach (var e in Exibidos(p, entrarEmCaixaDeTexto: false))
        {
            switch (e)
            {
                case SimpleField sf when !string.IsNullOrWhiteSpace(sf.Instruction?.Value):
                    yield return (sf.Instruction!.Value!, TextoPlano(sf), lido.ToString());
                    break;
                case FieldChar fc when fc.FieldCharType?.Value == FieldCharValues.Begin:
                    abertos.Push(new CampoAberto(lido.ToString()));
                    break;
                case FieldCode code when abertos.Count > 0 && abertos.Peek().Resultado is null:
                    abertos.Peek().Instrucao.Append(code.Text);
                    break;
                case FieldChar fc when fc.FieldCharType?.Value == FieldCharValues.Separate:
                    if (abertos.Count > 0) abertos.Peek().Resultado = new StringBuilder();
                    break;
                case Text t:
                    lido.Append(t.Text);
                    if (abertos.Count > 0 && abertos.Peek().Resultado is { } resultado) resultado.Append(t.Text);
                    break;
                case FieldChar fc when fc.FieldCharType?.Value == FieldCharValues.End:
                    if (abertos.Count > 0)
                    {
                        var campo = abertos.Pop();
                        yield return (campo.Instrucao.ToString(), campo.Resultado?.ToString(), campo.TextoAntes);
                    }
                    break;
            }
        }
        // Campo que começa num parágrafo e continua no seguinte (TOC): a instrução já está aqui.
        while (abertos.Count > 0)
        {
            var resto = abertos.Pop();
            if (resto.Instrucao.Length > 0)
                yield return (resto.Instrucao.ToString(), resto.Resultado?.ToString(), resto.TextoAntes);
        }
    }

    /// <summary>
    /// Descendentes como o Word os exibe: de cada <c>mc:AlternateContent</c> vale só o primeiro
    /// <c>mc:Choice</c>, e o <c>mc:Fallback</c> é ignorado.
    /// <para>
    /// Caixas de texto e formas modernas vêm gravadas duas vezes — DrawingML no Choice e uma
    /// cópia VML no Fallback, para versões antigas do Word. Percorrer as duas duplicava o
    /// texto (um cabeçalho com a tarja numa caixa de texto era lido como "EMISSÃO PARA
    /// COMENTÁRIOS DO CLIENTEEMISSÃO PARA COMENTÁRIOS DO CLIENTE…", que o avaliador semântico
    /// reportava como texto repetido) e contava a mesma imagem duas vezes.
    /// </para>
    /// </summary>
    internal static IEnumerable<OpenXmlElement> Exibidos(OpenXmlElement raiz, bool entrarEmCaixaDeTexto = true)
    {
        var pilha = new Stack<OpenXmlElement>();
        EmpilharFilhos(raiz);
        while (pilha.Count > 0)
        {
            var e = pilha.Pop();
            yield return e;
            if (!entrarEmCaixaDeTexto && e is TextBoxContent) continue;
            EmpilharFilhos(e);
        }

        void EmpilharFilhos(OpenXmlElement pai)
        {
            var filhos = pai.ChildElements;
            var choiceVisto = pai is AlternateContent && filhos.Any(f => f is AlternateContentChoice);
            for (var i = filhos.Count - 1; i >= 0; i--)
            {
                var filho = filhos[i];
                if (choiceVisto)
                {
                    if (filho is AlternateContentFallback) continue;
                    // Só o primeiro Choice vale; o Word escolhe o primeiro que entende.
                    if (filho is AlternateContentChoice
                        && !ReferenceEquals(filho, filhos.First(f => f is AlternateContentChoice))) continue;
                }
                pilha.Push(filho);
            }
        }
    }

    internal static IEnumerable<T> Exibidos<T>(OpenXmlElement raiz) where T : OpenXmlElement =>
        Exibidos(raiz).OfType<T>();

    private static int ContarImagens(OpenXmlElement e) =>
        Exibidos(e).Count(x => x is Drawing or DocumentFormat.OpenXml.Vml.ImageData);

    /// <summary>
    /// Cabeçalhos e rodapés que cada seção de fato exibe, com a herança do Word (seção que
    /// não declara um tipo usa o da anterior). É o que permite responder "a página do quadro
    /// é numerada?" e "a capa é numerada?" — o <c>SectionIndex</c> de uma parte diz só a
    /// primeira seção que a declara, e uma seção que a herda ficava sem cabeçalho nenhum.
    /// </summary>
    private static IReadOnlyList<ExtractedSection> ComPartesExibidas(
        IReadOnlyList<ExtractedSection> secoes, MainDocumentPart main, bool paresImpares,
        IReadOnlyDictionary<string, ExtractedHeaderFooter> partePorId)
    {
        var body = main.Document?.Body;
        if (body is null) return secoes;

        var vigentes = new Dictionary<(bool Cabecalho, string Tipo), string>();
        var resultado = new List<ExtractedSection>(secoes.Count);
        var indice = 0;
        foreach (var sectPr in body.Descendants<SectionProperties>())
        {
            foreach (var hr in sectPr.Elements<HeaderReference>())
                if (hr.Id?.Value is { Length: > 0 } id) vigentes[(true, ResolveKind(EnumText(hr.Type)))] = id;
            foreach (var fr in sectPr.Elements<FooterReference>())
                if (fr.Id?.Value is { Length: > 0 } id) vigentes[(false, ResolveKind(EnumText(fr.Type)))] = id;

            if (indice >= secoes.Count) break;
            var secao = secoes[indice++];

            ExtractedHeaderFooter? Parte(bool cabecalho, string tipo) =>
                vigentes.TryGetValue((cabecalho, tipo), out var id) && partePorId.TryGetValue(id, out var parte)
                    ? parte : null;

            var exibidas = new List<ExtractedHeaderFooter?>
            {
                Parte(true, "default"), Parte(false, "default")
            };
            if (secao.TitlePage) exibidas.AddRange([Parte(true, "first"), Parte(false, "first")]);
            if (paresImpares) exibidas.AddRange([Parte(true, "even"), Parte(false, "even")]);

            // Primeira página: 'first' com titlePg (mesmo vazio — sem a parte, a 1ª página fica
            // sem cabeçalho), senão o 'default'.
            var primeira = secao.TitlePage
                ? new[] { Parte(true, "first"), Parte(false, "first") }
                : new[] { Parte(true, "default"), Parte(false, "default") };

            resultado.Add(secao with
            {
                DisplayedHeaderFooters = exibidas.OfType<ExtractedHeaderFooter>().Distinct().ToList(),
                FirstPageHeaderFooters = primeira.OfType<ExtractedHeaderFooter>().ToList()
            });
        }

        // Seções sem sectPr correspondente (não deveria ocorrer) ficam como estavam.
        resultado.AddRange(secoes.Skip(resultado.Count));
        return resultado;
    }

    /// <summary>
    /// <c>outlineLvl</c> 9 é "corpo de texto" no Word, não um décimo nível de título — lido como
    /// título, gerava "salto de hierarquia" no painel de navegação.
    /// </summary>
    private static int? NivelDeEstrutura(int? bruto) => bruto is >= 0 and <= 8 ? bruto : null;

    private static bool PaginasParesEImparesDiferentes(MainDocumentPart main) =>
        main.DocumentSettingsPart?.Settings?.GetFirstChild<EvenAndOddHeaders>() is { } e
        && (e.Val?.Value ?? true);

    private static IReadOnlyDictionary<string, ExtractedStyle> ExtractStyles(MainDocumentPart main)
    {
        var styles = new Dictionary<string, ExtractedStyle>(StringComparer.OrdinalIgnoreCase);
        var stylesPart = main.StyleDefinitionsPart;
        if (stylesPart?.Styles is null) return styles;

        foreach (var style in stylesPart.Styles.Elements<Style>())
        {
            var id = style.StyleId?.Value ?? string.Empty;
            if (string.IsNullOrEmpty(id)) continue;
            var name = style.StyleName?.Val?.Value ?? id;

            var rPr = style.StyleRunProperties;
            var pPr = style.StyleParagraphProperties;

            string? font = rPr?.RunFonts?.Ascii?.Value;
            double? size = ParseHalfPoints(rPr?.FontSize?.Val?.Value);
            bool? bold = rPr?.Bold is { } b ? (b.Val?.Value ?? true) : (bool?)null;
            bool? italic = rPr?.Italic is { } i ? (i.Val?.Value ?? true) : (bool?)null;
            string? alignment = EnumText(pPr?.Justification?.Val);

            styles[id] = new ExtractedStyle(id, name, font, size, bold, italic, alignment);
        }
        return styles;
    }

    /// <summary>
    /// Valor bruto de um atributo enumerado do OOXML ("center", "right", "first", "even"…).
    /// A partir do SDK 3.x esses tipos são structs e <c>ToString()</c> devolve o nome do tipo
    /// ("JustificationValues { }"), não o valor — por isso a leitura é sempre via InnerText.
    /// </summary>
    private static string? EnumText(OpenXmlSimpleType? value)
    {
        var raw = value?.InnerText;
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToLowerInvariant();
    }

    private static double? ParseHalfPoints(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var halfPts))
            return halfPts / 2.0;
        return null;
    }

    /// <summary>
    /// Formatação efetiva de um estilo, já resolvida pela cadeia <c>w:basedOn</c> e pelos
    /// <c>docDefaults</c>. É o que permite comparar "Times New Roman 12" com o que o
    /// documento realmente aplica, e não apenas com o que a definição local do estilo diz.
    /// </summary>
    private sealed record StyleFormat(string? Font, double? Size, string? Alignment, int? OutlineLevel);

    private sealed class StyleResolver
    {
        private readonly Dictionary<string, Style> _byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StyleFormat> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly StyleFormat _defaults;
        private readonly string? _estiloPadrao;
        private readonly string? _fonteMenor;
        private readonly string? _fonteMaior;

        public StyleResolver(MainDocumentPart main)
        {
            var stylesPart = main.StyleDefinitionsPart;
            if (stylesPart?.Styles is not null)
            {
                foreach (var s in stylesPart.Styles.Elements<Style>())
                {
                    var id = s.StyleId?.Value;
                    if (!string.IsNullOrEmpty(id)) _byId[id] = s;
                    if (s.Default?.Value == true && EnumText(s.Type) == "paragraph") _estiloPadrao ??= id;
                }
            }

            var fontes = main.ThemePart?.Theme?.ThemeElements?.FontScheme;
            _fonteMenor = fontes?.MinorFont?.LatinFont?.Typeface?.Value;
            _fonteMaior = fontes?.MajorFont?.LatinFont?.Typeface?.Value;

            var docDefaults = stylesPart?.Styles?.DocDefaults;
            _defaults = new StyleFormat(
                Font: Fonte(docDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle?.RunFonts),
                Size: ParseHalfPoints(docDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle?.FontSize?.Val?.Value),
                Alignment: EnumText(docDefaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle?.Justification?.Val),
                OutlineLevel: null);
        }

        public StyleFormat Defaults => _defaults;

        /// <summary>
        /// Fonte de um <c>w:rFonts</c>: o nome explícito ou, quando a fonte vem do tema
        /// (<c>asciiTheme="minorHAnsi"</c>), o typeface do tema. Sem isso a fonte ficava nula e
        /// a regra de fonte do corpo nunca era avaliada em documento com fonte de tema.
        /// </summary>
        public string? Fonte(RunFonts? rf)
        {
            if (rf is null) return null;
            if (rf.Ascii?.Value is { Length: > 0 } explicita) return explicita;
            var tema = EnumText(rf.AsciiTheme) ?? EnumText(rf.HighAnsiTheme);
            if (tema is not null)
                return tema.StartsWith("major", StringComparison.Ordinal) ? _fonteMaior : _fonteMenor;
            return rf.HighAnsi?.Value is { Length: > 0 } hansi ? hansi : null;
        }

        public StyleFormat Resolve(string? styleId)
        {
            // Parágrafo sem pStyle usa o estilo de parágrafo padrão ("Normal"), não só os
            // docDefaults: num documento com o tamanho 12 definido no Normal e nada nos
            // docDefaults, o corpo saía com tamanho nulo e era julgado como fora do padrão.
            if (string.IsNullOrEmpty(styleId)) styleId = _estiloPadrao;
            if (string.IsNullOrEmpty(styleId)) return _defaults;
            if (_cache.TryGetValue(styleId, out var cached)) return cached;

            // Guarda contra basedOn cíclico (documentos gerados por ferramentas de terceiros).
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var chain = new List<Style>();
            var current = styleId;
            while (!string.IsNullOrEmpty(current) && visited.Add(current) && _byId.TryGetValue(current, out var style))
            {
                chain.Add(style);
                current = style.BasedOn?.Val?.Value;
            }

            // Do ancestral mais distante para o mais específico: o último a definir vence.
            string? font = _defaults.Font;
            double? size = _defaults.Size;
            string? alignment = _defaults.Alignment;
            int? outline = null;
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                var s = chain[i];
                font = Fonte(s.StyleRunProperties?.RunFonts) ?? font;
                size = ParseHalfPoints(s.StyleRunProperties?.FontSize?.Val?.Value) ?? size;
                alignment = EnumText(s.StyleParagraphProperties?.Justification?.Val) ?? alignment;
                // Declarado vale, inclusive o 9 ("corpo de texto"), que anula o nível herdado.
                if (s.StyleParagraphProperties?.OutlineLevel?.Val?.Value is { } nivel)
                    outline = NivelDeEstrutura(nivel);
            }

            outline ??= InferOutlineFromName(styleId, chain.Count > 0 ? chain[0].StyleName?.Val?.Value : null);

            var result = new StyleFormat(font, size, alignment, outline);
            _cache[styleId] = result;
            return result;
        }

        /// <summary>
        /// Estilos de título nem sempre declaram <c>w:outlineLvl</c>. O nome canônico em
        /// styles.xml é sempre inglês ("heading 1"), mas o styleId pode vir localizado
        /// ("Ttulo1") em documentos criados no Word em PT-BR.
        /// </summary>
        private static int? InferOutlineFromName(string? styleId, string? styleName)
        {
            foreach (var candidate in new[] { styleName, styleId })
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                var normalized = candidate.Trim().ToLowerInvariant().Replace(" ", "");
                foreach (var prefix in new[] { "heading", "ttulo", "titulo", "título" })
                {
                    if (!normalized.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    var rest = normalized[prefix.Length..];
                    if (int.TryParse(rest, out var level) && level is >= 1 and <= 9)
                        return level - 1; // outlineLvl é 0-based: "heading 1" -> 0
                }
            }
            return null;
        }
    }

    private sealed record HeaderFooterBinding(string Kind, int? SectionIndex, bool Visivel);

    // Parte de cabeçalho/rodapé que nenhuma seção referencia: existe no pacote, não aparece.
    private static readonly HeaderFooterBinding NaoReferenciado = new("default", null, false);

    private static IReadOnlyList<ExtractedHeaderFooter> ExtractHeaderFooter(
        IEnumerable<(DocumentFormat.OpenXml.OpenXmlPartRootElement Element, string Text, HeaderFooterBinding Binding)> parts)
    {
        var list = new List<ExtractedHeaderFooter>();
        foreach (var (element, text, binding) in parts)
        {
            var images = ContarImagens(element);
            var fieldCodes = ExtractFieldCodes(element);
            var alignments = Exibidos<Paragraph>(element)
                .Select(p => EnumText(p.ParagraphProperties?.Justification?.Val) ?? "left")
                .ToList();
            list.Add(new ExtractedHeaderFooter(
                binding.Kind, (text ?? string.Empty).Trim(), images, fieldCodes,
                alignments, binding.SectionIndex)
            {
                Visivel = binding.Visivel
            });
        }
        return list;
    }

    /// <summary>
    /// Mapeia o relId de cada HeaderReference/FooterReference para o seu tipo
    /// (default/first/even), o índice da primeira seção que o referencia e se ele chega a ser
    /// exibido, percorrendo as SectionProperties do documento na ordem em que aparecem.
    /// <para>
    /// Visibilidade segue as regras do Word: o 'default' sempre aparece; o 'first' só numa
    /// seção com <c>w:titlePg</c>; o 'even' só com <c>w:evenAndOddHeaders</c> nas
    /// configurações. Uma seção que não declara um tipo herda o da seção anterior — por isso
    /// um 'first' declarado numa seção sem <c>titlePg</c> ainda pode aparecer numa seção
    /// seguinte que ativa a opção sem declarar o seu.
    /// </para>
    /// </summary>
    private static IReadOnlyDictionary<string, HeaderFooterBinding> ResolveHeaderFooterKinds<TRef>(
        MainDocumentPart main, bool paresImpares)
        where TRef : OpenXmlElement
    {
        var primeiraRef = new Dictionary<string, (string Kind, int Secao)>(StringComparer.Ordinal);
        var visiveis = new HashSet<string>(StringComparer.Ordinal);
        var body = main.Document?.Body;
        if (body is null) return new Dictionary<string, HeaderFooterBinding>();

        var vigentePorTipo = new Dictionary<string, string>(StringComparer.Ordinal);
        var sectionIndex = 0;
        foreach (var sectPr in body.Descendants<SectionProperties>())
        {
            foreach (var refEl in sectPr.Elements<TRef>())
            {
                string? relId = null;
                string kind = "default";
                if (refEl is HeaderReference hr)
                {
                    relId = hr.Id?.Value;
                    kind = ResolveKind(EnumText(hr.Type));
                }
                else if (refEl is FooterReference fr)
                {
                    relId = fr.Id?.Value;
                    kind = ResolveKind(EnumText(fr.Type));
                }
                if (string.IsNullOrEmpty(relId)) continue;

                primeiraRef.TryAdd(relId, (kind, sectionIndex));
                vigentePorTipo[kind] = relId;
            }

            var primeiraPaginaDiferente = sectPr.GetFirstChild<TitlePage>() is { } tp && (tp.Val?.Value ?? true);
            if (vigentePorTipo.TryGetValue("default", out var d)) visiveis.Add(d);
            if (primeiraPaginaDiferente && vigentePorTipo.TryGetValue("first", out var f)) visiveis.Add(f);
            if (paresImpares && vigentePorTipo.TryGetValue("even", out var e)) visiveis.Add(e);

            sectionIndex++;
        }

        return primeiraRef.ToDictionary(
            kv => kv.Key,
            kv => new HeaderFooterBinding(kv.Value.Kind, kv.Value.Secao, visiveis.Contains(kv.Key)),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Coleta nomes de campos OOXML (PAGE, NUMPAGES, TOC, REF etc.) presentes em
    /// um SectionPartRootElement (header/footer ou body). Captura tanto SimpleField
    /// (<c>w:fldSimple w:instr="PAGE"</c>) quanto a forma estendida com FieldCode.
    /// </summary>
    private static IReadOnlyList<string> ExtractFieldCodes(DocumentFormat.OpenXml.OpenXmlElement element)
    {
        var codes = new List<string>();

        foreach (var simple in Exibidos<SimpleField>(element))
        {
            var instr = simple.Instruction?.Value;
            var name = ExtractFieldName(instr);
            if (!string.IsNullOrEmpty(name)) codes.Add(name);
        }

        foreach (var fc in Exibidos<FieldCode>(element))
        {
            var name = ExtractFieldName(fc.Text);
            if (!string.IsNullOrEmpty(name)) codes.Add(name);
        }

        return codes;
    }

    private static string? ExtractFieldName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.TrimStart();
        // Field instructions começam com o nome do campo (ex.: "PAGE \\* MERGEFORMAT").
        var space = trimmed.IndexOfAny(new[] { ' ', '\t' });
        var name = space < 0 ? trimmed : trimmed[..space];
        return name.Trim().ToUpperInvariant();
    }

    private static string ResolveKind(string? rawType) => (rawType ?? string.Empty).ToLowerInvariant() switch
    {
        "first" => "first",
        "even" => "even",
        _ => "default"
    };

    private static IReadOnlyList<ExtractedSection> ExtractSections(MainDocumentPart main)
    {
        var body = main.Document?.Body;
        if (body is null) return Array.Empty<ExtractedSection>();
        var sections = new List<ExtractedSection>();
        int idx = 0;
        foreach (var sp in body.Descendants<SectionProperties>())
        {
            var ps = sp.GetFirstChild<PageSize>();
            var pm = sp.GetFirstChild<PageMargin>();
            sections.Add(new ExtractedSection(
                Index: idx++,
                PageWidth: ToNullableDouble(ps?.Width),
                PageHeight: ToNullableDouble(ps?.Height),
                MarginTop: ToNullableDouble(pm?.Top?.Value),
                MarginBottom: ToNullableDouble(pm?.Bottom?.Value),
                MarginLeft: ToNullableDouble(pm?.Left),
                MarginRight: ToNullableDouble(pm?.Right),
                TitlePage: sp.GetFirstChild<TitlePage>() is { } tp && (tp.Val?.Value ?? true)));
        }
        return sections;
    }

    private static double? ToNullableDouble(object? raw)
    {
        if (raw is null) return null;
        return double.TryParse(raw.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>
    /// Parágrafos do corpo com um id único e estável, compartilhado com o
    /// <see cref="CommentInserter"/> — é por ele que o comentário volta ao parágrafo certo.
    /// <para>
    /// O <c>w14:paraId</c> não é confiável como chave: documentos do python-docx, LibreOffice,
    /// Google Docs e Word 2007 não o gravam, e o próprio Word repete o mesmo id na cópia de
    /// reserva de uma caixa de texto. Ambos derrubavam a revisão inteira na hora de inserir os
    /// comentários. Sem id próprio, ou com id repetido, vale a posição ordinal.
    /// </para>
    /// </summary>
    internal static IEnumerable<(Paragraph Paragrafo, string Id)> ParagrafosComId(Body body)
    {
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        foreach (var p in Exibidos<Paragraph>(body))
        {
            var id = p.ParagraphId?.Value;
            if (string.IsNullOrWhiteSpace(id) || !vistos.Add(id)) id = $"p{i:0000}";
            yield return (p, id);
            i++;
        }
    }

    /// <summary>
    /// Posição das quebras de página em relação ao texto do parágrafo. "Termina com quebra"
    /// juntava os dois casos, e um título que <em>abre</em> página nova (quebra antes do texto,
    /// o normal num capítulo) era acusado de deixar o conteúdo na página seguinte.
    /// </summary>
    private static (bool DepoisDoTexto, bool RenderizadaNoInicio) Quebras(Paragraph p)
    {
        var viuTexto = false;
        var depois = false;
        var renderizadaNoInicio = false;
        foreach (var e in Exibidos(p, entrarEmCaixaDeTexto: false))
        {
            switch (e)
            {
                case Text t when !string.IsNullOrWhiteSpace(t.Text):
                    viuTexto = true;
                    break;
                case Break b when b.Type?.Value == BreakValues.Page && viuTexto:
                    depois = true;
                    break;
                case LastRenderedPageBreak when !viuTexto:
                    renderizadaNoInicio = true;
                    break;
            }
        }
        return (depois, renderizadaNoInicio);
    }

    private static IReadOnlyList<ExtractedParagraph> ExtractParagraphs(MainDocumentPart main, StyleResolver resolver)
    {
        var body = main.Document?.Body;
        if (body is null) return Array.Empty<ExtractedParagraph>();
        var list = new List<ExtractedParagraph>();
        int sectionIndex = 0;

        // Mesma enumeração usada por ExtractTables, então os índices coincidem. Serve para
        // devolver o parágrafo à linha/coluna de origem: sem isso a folha índice chega ao
        // avaliador como uma pilha de células soltas ("REV.", "0", "EMISSÃO", "B"), e a
        // leitura natural vira um código "0/B" que o documento nunca teve.
        // OpenXmlElement não sobrescreve Equals, então a chave é identidade de referência.
        var indicePorTabela = new Dictionary<Table, int>();
        {
            int t = 0;
            foreach (var tabela in Exibidos<Table>(body)) indicePorTabela[tabela] = t++;
        }

        foreach (var (p, id) in ParagrafosComId(body))
        {
            var pPr = p.ParagraphProperties;
            var styleId = pPr?.ParagraphStyleId?.Val?.Value;
            var styleFmt = resolver.Resolve(styleId);

            // Page break: <w:br w:type="page"/> dentro do parágrafo (run-level)
            // ou no ParagraphProperties (page break before).
            var quebras = Quebras(p);
            var hasPageBreak = p.Descendants<Break>().Any(b =>
                                    b.Type?.Value == BreakValues.Page)
                            || (pPr?.PageBreakBefore is not null);

            var alignment = EnumText(pPr?.Justification?.Val) ?? styleFmt.Alignment;
            var outline = pPr?.OutlineLevel?.Val?.Value is { } nivelDireto
                ? NivelDeEstrutura(nivelDireto)
                : styleFmt.OutlineLevel;
            var numId = pPr?.NumberingProperties?.NumberingId?.Val?.Value;
            var (font, size) = ResolveEffectiveRunFormat(p, styleFmt, resolver);
            var images = ContarImagens(p);
            var inTable = p.Ancestors<TableCell>().Any();
            // Ancestral mais próximo: em tabela aninhada vale a interna, que é a que
            // ExtractTables também numera.
            var tabelaDoParagrafo = p.Ancestors<Table>().FirstOrDefault();
            var tableIndex = tabelaDoParagrafo is not null
                          && indicePorTabela.TryGetValue(tabelaDoParagrafo, out var ti)
                ? ti
                : (int?)null;

            list.Add(new ExtractedParagraph(
                ParagraphId: id,
                StyleId: styleId,
                Text: TextoComSeparadores(p),
                EndsWithPageBreak: hasPageBreak,
                Alignment: alignment,
                OutlineLevel: outline,
                NumberingId: numId,
                EffectiveFont: font,
                EffectiveFontSize: size,
                ImageCount: images,
                SectionIndex: sectionIndex,
                IsInTable: inTable,
                TableIndex: tableIndex,
                FieldCodes: ExtractFieldCodes(p))
            {
                PageBreakAfterText = quebras.DepoisDoTexto,
                RenderedPageBreakAtStart = quebras.RenderizadaNoInicio
            });

            // Um sectPr dentro do pPr encerra a seção — o próximo parágrafo já é da seguinte.
            if (pPr?.SectionProperties is not null) sectionIndex++;
        }
        return list;
    }

    /// <summary>
    /// Texto do elemento preservando os separadores que o <c>InnerText</c> descarta.
    /// <para>
    /// <c>InnerText</c> concatena apenas os nós <c>w:t</c>, então <c>w:tab</c> e <c>w:br</c>
    /// somem e o que era separado na tela vira uma palavra só: uma entrada de índice
    /// <c>'10.4' &lt;tab&gt; 'Avaliação…' &lt;tab&gt; &lt;PAGEREF→67&gt;</c> é lida como
    /// "10.4Avaliação…67". Isso produzia acusações de falta de espaço e de "numeração colada
    /// ao título" que não existem no documento.
    /// </para>
    /// O separador emitido é whitespace, que <c>DocumentoTexto.Normalizar</c> colapsa — as
    /// comparações normalizadas dos demais checks continuam valendo, apenas ganham a
    /// fronteira de palavra que faltava.
    /// </summary>
    private static string TextoComSeparadores(OpenXmlElement element)
    {
        // Numa célula ou num cabeçalho, cada parágrafo é uma linha na tela. Concatenar sem
        // separador cola a última palavra de um na primeira do seguinte — a célula de título
        // da folha de rosto virava "BARRAGEM A1ENGENHARIA", uma aglutinação que o documento
        // não tem e que o avaliador reportava como erro de redação.
        if (element is not Paragraph)
        {
            var paragrafos = Exibidos<Paragraph>(element).ToList();
            if (paragrafos.Count > 0)
                return string.Join("\n", paragrafos.Select(TextoPlano));
        }
        return TextoPlano(element);
    }

    private static string TextoPlano(OpenXmlElement element)
    {
        var sb = new StringBuilder();
        // O conteúdo de uma caixa de texto são parágrafos próprios (w:txbxContent), lidos e
        // listados por si. Incluí-los também no parágrafo âncora duplicava o texto.
        foreach (var node in Exibidos(element, entrarEmCaixaDeTexto: element is not Paragraph))
        {
            switch (node)
            {
                case Text t:
                    sb.Append(t.Text);
                    break;
                case TabChar:
                    sb.Append('\t');
                    break;
                case Break:
                    sb.Append('\n');
                    break;
                // Marca d'água do Word (Design → Marca D'água) e WordArt VML: o texto fica no
                // atributo "string" do v:textpath, não em w:t. É o formato mais comum da tarja
                // de emissão — sem isto ela era invisível para os checks.
                case DocumentFormat.OpenXml.Vml.TextPath tp when !string.IsNullOrWhiteSpace(tp.String?.Value):
                    sb.Append('\n').Append(tp.String!.Value!.Trim()).Append('\n');
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Fonte e tamanho predominantes do parágrafo, ponderados pelo comprimento do texto de
    /// cada run. A formatação direta do run tem precedência sobre o estilo — sem isso um
    /// documento com estilo "Normal/Arial" mas runs marcados Times New Roman seria julgado
    /// pelo estilo, e não pelo que o leitor vê.
    /// </summary>
    private static (string? Font, double? Size) ResolveEffectiveRunFormat(Paragraph p, StyleFormat styleFmt, StyleResolver resolver)
    {
        var fontWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sizeWeights = new Dictionary<double, int>();

        // Todos os runs do parágrafo, não só os filhos diretos: texto dentro de hyperlink,
        // controle de conteúdo, inserção rastreada ou campo simples também é corpo. Caixas de
        // texto ficam de fora — são parágrafos próprios.
        foreach (var run in Exibidos(p, entrarEmCaixaDeTexto: false).OfType<Run>())
        {
            var text = string.Concat(run.Elements<Text>().Select(t => t.Text));
            if (string.IsNullOrWhiteSpace(text)) continue;
            var weight = text.Length;

            // Precedência do Word: formatação direta > estilo de caractere > estilo de parágrafo.
            var estiloDoRun = run.RunProperties?.RunStyle?.Val?.Value is { Length: > 0 } rStyle
                ? resolver.Resolve(rStyle)
                : null;

            var font = resolver.Fonte(run.RunProperties?.RunFonts) ?? estiloDoRun?.Font ?? styleFmt.Font;
            if (!string.IsNullOrEmpty(font))
                fontWeights[font] = fontWeights.GetValueOrDefault(font) + weight;

            var size = ParseHalfPoints(run.RunProperties?.FontSize?.Val?.Value) ?? estiloDoRun?.Size ?? styleFmt.Size;
            if (size is { } s)
                sizeWeights[s] = sizeWeights.GetValueOrDefault(s) + weight;
        }

        var dominantFont = fontWeights.Count > 0
            ? fontWeights.OrderByDescending(kv => kv.Value).First().Key
            : styleFmt.Font;
        var dominantSize = sizeWeights.Count > 0
            ? sizeWeights.OrderByDescending(kv => kv.Value).First().Key
            : styleFmt.Size;

        return (dominantFont, dominantSize);
    }

    private static ExtractedDocumentProperties ExtractDocumentProperties(WordprocessingDocument doc)
    {
        var p = doc.PackageProperties;
        return new ExtractedDocumentProperties(
            Title: p.Title,
            Subject: p.Subject,
            Creator: p.Creator,
            LastModifiedBy: p.LastModifiedBy,
            Revision: p.Revision,
            Created: p.Created,
            Modified: p.Modified);
    }

    private static IReadOnlyList<ExtractedTable> ExtractTables(MainDocumentPart main)
    {
        var body = main.Document?.Body;
        if (body is null) return Array.Empty<ExtractedTable>();
        var list = new List<ExtractedTable>();
        int idx = 0;
        foreach (var t in Exibidos<Table>(body))
        {
            var cells = new List<ExtractedTableCell>();
            int rowIdx = 0;
            string? firstRowText = null;
            foreach (var row in t.Elements<TableRow>())
            {
                int colIdx = 0;
                var rowTexts = new List<string>();
                foreach (var cell in row.Elements<TableCell>())
                {
                    var text = TextoComSeparadores(cell).Trim();
                    var cellImages = ContarImagens(cell);
                    cells.Add(new ExtractedTableCell(rowIdx, colIdx, text, cellImages));
                    rowTexts.Add(text);
                    colIdx++;
                }
                if (rowIdx == 0) firstRowText = string.Join(" | ", rowTexts);
                rowIdx++;
            }
            list.Add(new ExtractedTable(idx++, cells, firstRowText));
        }
        return list;
    }

    private static bool HasPendingTrackChanges(MainDocumentPart main)
    {
        var body = main.Document?.Body;
        if (body is null) return false;
        return body.Descendants<InsertedRun>().Any()
            || body.Descendants<DeletedRun>().Any()
            || body.Descendants<ParagraphPropertiesChange>().Any()
            || body.Descendants<RunPropertiesChange>().Any();
    }

    private static bool HasOpenComments(MainDocumentPart main)
    {
        var commentsPart = main.WordprocessingCommentsPart;
        if (commentsPart?.Comments is null) return false;
        return commentsPart.Comments.Elements<Comment>().Any();
    }

    private static bool HasUpdatedToc(MainDocumentPart main)
    {
        // TOC heuristic: SDT block containing a "TOC" docPartGallery or paragraphs with "tocXX" style.
        var body = main.Document?.Body;
        if (body is null) return false;
        if (body.Descendants<SdtBlock>().Any(s => s.InnerText.Contains("Sumário", StringComparison.OrdinalIgnoreCase)
                                              || s.InnerText.Contains("Índice", StringComparison.OrdinalIgnoreCase)))
            return true;
        return body.Descendants<Paragraph>()
            .Any(p => (p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "")
                .StartsWith("toc", StringComparison.OrdinalIgnoreCase));
    }
}
