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
    /// <summary>
    /// Uma entrada de um histórico de revisões. <see cref="Colunas"/> traz todas as células
    /// da linha, indexadas pelo texto normalizado do cabeçalho da coluna.
    /// </summary>
    public sealed record EntradaDeRevisao(string Revisao, string? Data, string? Descricao)
    {
        public IReadOnlyDictionary<string, string> Colunas { get; init; } =
            new Dictionary<string, string>();

        /// <summary>Valor da primeira coluna cujo cabeçalho normalizado contém algum dos termos.</summary>
        public string? Coluna(IEnumerable<string> termos)
        {
            foreach (var termo in termos.Select(DocumentoTexto.Normalizar).Where(t => t.Length > 0))
            {
                var hit = Colunas.FirstOrDefault(kv => kv.Key.Contains(termo, StringComparison.Ordinal));
                if (hit.Key is not null) return hit.Value;
            }
            return null;
        }

        public DateOnly? DataLida => LerData(Data);
    }

    // PdA: 0A–0Z, 00–99. Cliente: 0, 1, 2… — a folha índice do Cliente usa um dígito só.
    private static readonly System.Text.RegularExpressions.Regex TokenRevisao =
        new(@"^(0[A-Za-z]|\d{1,2})$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>"11/03/26" e "11/03/2026" viram a mesma data; o resto, null.</summary>
    public static DateOnly? LerData(string? texto)
    {
        var m = System.Text.RegularExpressions.Regex.Match(texto ?? string.Empty, @"\b(\d{1,2})/(\d{1,2})/(\d{2}|\d{4})\b");
        if (!m.Success) return null;
        var ano = int.Parse(m.Groups[3].Value);
        if (ano < 100) ano += 2000;
        try { return new DateOnly(ano, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>
    /// Ordem de uma revisão PdA: 0A &lt; 0B &lt; … &lt; 0Z &lt; 00 &lt; 01 &lt; … Null para o que não é
    /// código PdA.
    /// </summary>
    public static int? OrdemDaRevisaoPda(string? revisao)
    {
        var r = (revisao ?? string.Empty).Trim().ToUpperInvariant();
        if (r.Length == 2 && r[0] == '0' && char.IsAsciiLetterUpper(r[1])) return r[1] - 'A';
        if (r.Length == 2 && int.TryParse(r, out var n)) return 100 + n;
        return null;
    }

    /// <summary>
    /// Histórico de revisões da folha índice do Cliente: a primeira tabela da folha de rosto
    /// (fora o quadro) que tem cabeçalho com revisão e data.
    /// </summary>
    public static IReadOnlyList<EntradaDeRevisao> HistoricoDaFolhaDeRosto(DocumentStructure doc, ExtractedTable? quadro)
    {
        var tabelas = DocumentoTexto.FolhaDeRosto(doc)
            .Select(p => p.TableIndex)
            .OfType<int>()
            .Distinct()
            .Where(i => quadro is null || !quadro.Contem(i));

        foreach (var indice in tabelas)
        {
            var tabela = doc.Tables.FirstOrDefault(t => t.Index == indice);
            if (tabela is null) continue;
            var historico = HistoricoDeRevisoes(tabela);
            if (historico.Count > 0) return historico;
        }
        return Array.Empty<EntradaDeRevisao>();
    }

    private static readonly System.Text.RegularExpressions.Regex DataCurta =
        new(@"^\d{1,2}/\d{1,2}/\d{2,4}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Histórico de revisões do quadro, na ordem do documento — a última entrada é a revisão
    /// vigente.
    /// <para>
    /// O bloco é localizado pelo cabeçalho que tem <b>ao mesmo tempo</b> uma coluna de revisão
    /// e uma de data. Essa exigência é o que separa o histórico do cabeçalho da grade de
    /// folhas, que também começa com "Rev." mas segue com códigos de revisão como colunas.
    /// Confundir os dois fazia a revisão vigente ser lida como "0B" num documento em "00" —
    /// e, com isso, a regra de tarja concluía que o documento ainda estava na etapa de
    /// comentários e aprovava a tarja obsoleta.
    /// </para>
    /// </summary>
    public static IReadOnlyList<EntradaDeRevisao> HistoricoDeRevisoes(ExtractedTable quadro)
    {
        var linhas = quadro.Cells.GroupBy(c => c.Row).OrderBy(g => g.Key)
            .Select(g => g.OrderBy(c => c.Column).ToList())
            .ToList();

        for (var i = 0; i < linhas.Count - 1; i++)
        {
            var cabecalho = linhas[i];
            var colRev = ColunaCom(cabecalho, "rev");
            var colData = ColunaCom(cabecalho, "data");
            if (colRev is null || colData is null) continue;

            // Cabeçalho em duas linhas (FR-115-01): "Histórico de Revisões | Data da Emissão |
            // Iniciais | Descrição" e, embaixo, "Elaborador | V. Técnico | Aprovador" com as
            // demais células mescladas na vertical (vazias). As linhas de dados seguem a
            // estrutura da linha de baixo, então ela vira o cabeçalho, com os nomes de cima.
            var primeiraDeDados = i + 1;
            if (!linhas[i + 1].Any(c => c.Column == colRev && TokenRevisao.IsMatch(c.Text.Trim()))
                && i + 2 < linhas.Count
                && Compor(cabecalho, linhas[i + 1]) is var composto
                && ColunaCom(composto, "rev") is { } revComposto
                && ColunaCom(composto, "data") is { } dataComposto)
            {
                cabecalho = composto;
                colRev = revComposto;
                colData = dataComposto;
                primeiraDeDados = i + 2;
            }

            var colDescricao = ColunaCom(cabecalho, "descri");
            var nomes = cabecalho
                .Where(c => DocumentoTexto.Normalizar(c.Text).Length > 0)
                .GroupBy(c => c.Column)
                .ToDictionary(g => g.Key, g => DocumentoTexto.Normalizar(g.First().Text));
            var entradas = new List<EntradaDeRevisao>();

            for (var r = primeiraDeDados; r < linhas.Count; r++)
            {
                var celulas = linhas[r].GroupBy(c => c.Column).ToDictionary(g => g.Key, g => g.First().Text.Trim());
                if (!celulas.TryGetValue(colRev.Value, out var rev) || !TokenRevisao.IsMatch(rev)) break;

                celulas.TryGetValue(colData.Value, out var data);
                string? descricao = null;
                if (colDescricao is { } cd) celulas.TryGetValue(cd, out descricao);

                var colunas = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (coluna, nome) in nomes)
                    colunas.TryAdd(nome, celulas.GetValueOrDefault(coluna) ?? string.Empty);

                entradas.Add(new EntradaDeRevisao(rev.ToUpperInvariant(), data, descricao) { Colunas = colunas });
            }

            if (entradas.Count > 0) return entradas;
        }

        return Array.Empty<EntradaDeRevisao>();
    }

    /// <summary>
    /// Cabeçalho de duas linhas lido pela linha de baixo: cada célula vazia (continuação de
    /// mesclagem vertical) recebe o texto da célula de cima que cobre a mesma coluna da grade.
    /// </summary>
    private static List<ExtractedTableCell> Compor(
        IReadOnlyList<ExtractedTableCell> superior, IReadOnlyList<ExtractedTableCell> inferior) =>
        inferior.Select(c =>
        {
            if (c.Text.Trim().Length > 0) return c;
            var acima = superior.FirstOrDefault(s =>
                c.GridColumn >= s.GridColumn && c.GridColumn < s.GridColumn + s.GridSpan);
            return acima is null ? c : c with { Text = acima.Text };
        }).ToList();

    /// <summary>Revisão vigente: a última entrada do histórico.</summary>
    public static string? RevisaoVigente(ExtractedTable quadro) =>
        HistoricoDeRevisoes(quadro).LastOrDefault()?.Revisao;

    private static int? ColunaCom(IReadOnlyList<ExtractedTableCell> cabecalho, string termo)
    {
        foreach (var c in cabecalho)
        {
            var texto = c.Text.Trim();
            // O cabeçalho da grade traz datas? Não — mas traz códigos de revisão nas demais
            // colunas, e é por isso que a coluna de data é o discriminador.
            if (texto.Contains(termo, StringComparison.OrdinalIgnoreCase) && !DataCurta.IsMatch(texto))
                return c.Column;
        }
        return null;
    }

    /// <summary>
    /// Linha da grade de controle de folhas do quadro: toda célula preenchida é um número de
    /// folha ou uma marcação "x". As linhas de histórico (que trazem datas e iniciais) e as
    /// de identificação não casam.
    /// </summary>
    public static bool EhLinhaDeGradeDeFolhas(IReadOnlyList<ExtractedTableCell> celulas)
    {
        if (celulas.Count < 4) return false;

        // O modelo FR-115-01 abre cada grupo com o rótulo vertical "PÁGINAS" (uma letra por
        // linha na célula mesclada). Ele não é folha nem marcação e fica de fora.
        var preenchidas = celulas.Select(c => c.Text.Trim())
            .Where(t => t.Length > 0 && !EhRotuloDeFolhas(t))
            .ToList();
        if (preenchidas.Count == 0) return false;

        return preenchidas.All(t => int.TryParse(t, out _)
                                 || t.Equals("x", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Maior número de folha <b>marcado</b> na grade de controle — o total de folhas que o
    /// quadro efetivamente declara.
    /// <para>
    /// A grade vem impressa até um número redondo e sobram linhas sem marcação: no RN-816 ela
    /// vai até 100, mas a folha 100 não tem "x" em revisão nenhuma, enquanto 1 a 99 têm. Ler
    /// o maior número impresso, e não o maior marcado, transforma slot vazio de formulário em
    /// divergência de paginação.
    /// </para>
    /// Cada linha traz vários grupos lado a lado (folha, depois suas colunas de revisão), então
    /// o agrupamento é sequencial: um número inteiro abre um grupo e o encerra o anterior.
    /// </summary>
    public static int? MaiorFolhaMarcadaNaGrade(ExtractedTable quadro)
    {
        int? maior = null;

        foreach (var linha in quadro.Cells.GroupBy(c => c.Row).OrderBy(g => g.Key))
        {
            var celulas = linha.OrderBy(c => c.Column).ToList();
            if (!EhLinhaDeGradeDeFolhas(celulas)) continue;

            int? folhaAtual = null;
            var marcada = false;

            void Fechar()
            {
                if (folhaAtual is { } f && marcada && (maior is null || f > maior)) maior = f;
            }

            foreach (var texto in celulas.Select(c => c.Text.Trim()))
            {
                if (int.TryParse(texto, out var numero))
                {
                    Fechar();
                    folhaAtual = numero;
                    marcada = false;
                }
                else if (texto.Equals("x", StringComparison.OrdinalIgnoreCase))
                {
                    marcada = true;
                }
            }
            Fechar();
        }

        return maior;
    }

    /// <summary>
    /// Localiza o quadro entre as tabelas cujo título casa algum alias.
    /// <para>
    /// Antes valia a primeira tabela do documento com o alias na primeira linha — e o alias
    /// genérico "Características" casa "Características geométricas da barragem", tabela que
    /// num relatório vem muito antes do quadro (que fica no fim). Esse quadro errado gerava
    /// "campo obrigatório ausente" em série e escondia as iniciais do quadro verdadeiro. Agora,
    /// entre os candidatos, vence quem tem histórico de revisões (assinatura estrutural do
    /// quadro), depois o título igual ao alias, depois o alias mais específico e, por fim, a
    /// última tabela.
    /// </para>
    /// </summary>
    public static ExtractedTable? Find(DocumentStructure doc, IEnumerable<string> tituloAliases)
    {
        var aliases = tituloAliases
            .Select(DocumentoTexto.Normalizar)
            .Where(a => a.Length > 0)
            .Distinct()
            .ToList();
        if (aliases.Count == 0) return null;

        // Cada candidata já vem completada com a continuação (modelo FR-115-01, em duas
        // tabelas): sem isso a tabela do título, que não tem histórico, perderia para qualquer
        // outra e, vencendo, deixaria revisão, datas e iniciais "ausentes".
        return Melhor(doc.Tables.Select(t => (Tabela: Completar(doc, t), Titulo: DocumentoTexto.Normalizar(t.FirstRowText))))
            // Documentos que põem o título fora da primeira linha: só depois que as tabelas
            // tituladas tiveram a chance de casar.
            ?? Melhor(doc.Tables.Select(t => (Tabela: Completar(doc, t),
                   Titulo: DocumentoTexto.Normalizar(string.Join(" ", t.Cells.Select(c => c.Text))))));

        ExtractedTable? Melhor(IEnumerable<(ExtractedTable Tabela, string Titulo)> candidatas) =>
            candidatas
                .Select(c => (c.Tabela, c.Titulo,
                              Alias: aliases.Where(a => c.Titulo.Contains(a, StringComparison.Ordinal))
                                            .OrderByDescending(a => a.Length)
                                            .FirstOrDefault()))
                .Where(c => c.Alias is not null)
                .OrderByDescending(c => HistoricoDeRevisoes(c.Tabela).Count > 0)
                .ThenByDescending(c => aliases.Contains(c.Titulo))
                .ThenByDescending(c => c.Alias!.Length)
                .ThenByDescending(c => c.Tabela.Index)
                .Select(c => c.Tabela)
                .FirstOrDefault();
    }

    /// <summary>
    /// Rótulo de grupo da grade de folhas ("PÁGINAS", "FOLHAS"), inclusive escrito na
    /// vertical — "P/Á/G/I/N/A/S", uma letra por parágrafo.
    /// </summary>
    internal static bool EhRotuloDeFolhas(string texto)
    {
        var letras = new string(DocumentoTexto.Normalizar(texto).Where(char.IsLetter).ToArray());
        return letras is "paginas" or "pagina" or "pags" or "pag" or "folhas" or "folha" or "fls" or "fl";
    }

    /// <summary>
    /// Junta ao quadro as tabelas que o continuam.
    /// <para>
    /// O modelo antigo é uma tabela só. O FR-115-01 separa o quadro em duas tabelas
    /// consecutivas: a primeira com o título "QUADRO CARACTERÍSTICAS DO DOCUMENTO", o título e
    /// as codificações; a segunda com a relação de apêndices e anexos, a grade de páginas
    /// alteradas e o histórico de revisões. Lida sozinha, a primeira não tem revisão, datas
    /// nem iniciais. A continuação só é aceita quando traz o histórico de revisões (é ele que
    /// identifica o quadro) e quando nada além de parágrafos vazios ou curtos — nenhum título
    /// de seção — separa as tabelas.
    /// </para>
    /// </summary>
    internal static ExtractedTable Completar(DocumentStructure doc, ExtractedTable tabela)
    {
        // Só a tabela do título do FR-115-01 é completada: a que já traz cabeçalho de histórico
        // (modelo antigo, mesmo com histórico vazio) é o quadro inteiro, e juntá-la colaria
        // nela a tabela que viesse depois — a folha índice do Cliente, por exemplo.
        if (TemCabecalhoDeHistorico(tabela)) return tabela;

        var partes = new List<ExtractedTable> { tabela };
        var aninhadas = new List<int>();
        var tentativas = 0;
        foreach (var seguinte in doc.Tables.Where(t => t.Index > tabela.Index).OrderBy(t => t.Index))
        {
            var posicao = Posicao(doc, partes[^1].Index, seguinte.Index);
            // Tabela dentro da anterior (logo numa célula, por exemplo): é parte do quadro, mas
            // seu texto já está na célula que a contém.
            if (posicao == PosicaoDaSeguinte.Dentro) { aninhadas.Add(seguinte.Index); continue; }
            if (posicao != PosicaoDaSeguinte.Adjacente || ++tentativas > 2) return tabela;

            partes.Add(seguinte);
            // A continuação se reconhece pela estrutura — cabeçalho do histórico ou grade de
            // páginas — e não por ter revisões preenchidas: o formulário em branco e o rascunho
            // também precisam ser lidos como um quadro só.
            if (TemCabecalhoDeHistorico(seguinte) || TemGradeDeFolhas(seguinte))
                return Juntar(partes, aninhadas);
        }
        return tabela;
    }

    /// <summary>Alguma linha tem ao mesmo tempo coluna de revisão e de data.</summary>
    private static bool TemCabecalhoDeHistorico(ExtractedTable tabela) =>
        Linhas(tabela).Any(l => ColunaCom(l, "rev") is not null && ColunaCom(l, "data") is not null);

    private static bool TemGradeDeFolhas(ExtractedTable tabela) =>
        Linhas(tabela).Any(EhLinhaDeGradeDeFolhas);

    private static IEnumerable<List<ExtractedTableCell>> Linhas(ExtractedTable tabela) =>
        tabela.Cells.GroupBy(c => c.Row).OrderBy(g => g.Key).Select(g => g.OrderBy(c => c.Column).ToList());

    private static ExtractedTable Juntar(IReadOnlyList<ExtractedTable> partes, IReadOnlyList<int> aninhadas)
    {
        var celulas = new List<ExtractedTableCell>();
        var deslocamento = 0;
        foreach (var parte in partes)
        {
            celulas.AddRange(parte.Cells.Select(c => c with { Row = c.Row + deslocamento }));
            if (parte.Cells.Count > 0) deslocamento += parte.Cells.Max(c => c.Row) + 1;
        }
        return new ExtractedTable(partes[0].Index, celulas, partes[0].FirstRowText)
        {
            Partes = partes.Select(p => p.Index).Concat(aninhadas).ToList()
        };
    }

    /// <summary>
    /// Nada relevante entre as tabelas <paramref name="anterior"/> e <paramref name="seguinte"/>:
    /// sem título de seção e com no máximo 200 caracteres de texto solto (o modelo deixa um
    /// parágrafo vazio entre elas). Estrutura sem parágrafos (montada à mão nos testes): adjacentes.
    /// </summary>
    private enum PosicaoDaSeguinte { Adjacente, Dentro, Separada }

    private static PosicaoDaSeguinte Posicao(DocumentStructure doc, int anterior, int seguinte)
    {
        var paragrafos = doc.Paragraphs;
        var fim = -1;
        var inicio = -1;
        for (var i = 0; i < paragrafos.Count; i++)
        {
            if (paragrafos[i].TableIndex == anterior) fim = i;
            if (paragrafos[i].TableIndex == seguinte && inicio < 0) inicio = i;
        }
        if (fim < 0 && inicio < 0) return PosicaoDaSeguinte.Adjacente;
        if (fim < 0 || inicio < 0) return PosicaoDaSeguinte.Separada;
        if (inicio < fim) return PosicaoDaSeguinte.Dentro;

        var entre = paragrafos.Skip(fim + 1).Take(inicio - fim - 1).Where(p => p.TableIndex is null).ToList();
        return entre.All(p => p.OutlineLevel is null) && entre.Sum(p => p.Text.Trim().Length) <= 200
            ? PosicaoDaSeguinte.Adjacente
            : PosicaoDaSeguinte.Separada;
    }

    /// <summary>
    /// Páginas que o quadro ocupou na última paginação do Word: uma, mais cada
    /// <c>w:lastRenderedPageBreak</c> dentro das tabelas do quadro (fora a quebra que abre o
    /// quadro numa página nova). Nulo quando o quadro não tem parágrafos mapeados.
    /// </summary>
    public static int? PaginasDoQuadro(DocumentStructure doc, ExtractedTable quadro)
    {
        var paragrafos = doc.Paragraphs.Where(p => quadro.Contem(p.TableIndex)).ToList();
        if (paragrafos.Count == 0) return null;

        var quebras = paragrafos.Sum(p => p.RenderedPageBreaks);
        if (paragrafos[0].RenderedPageBreakAtStart && quebras > 0) quebras--;
        return quebras + 1;
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
