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
            .Where(i => quadro is null || i != quadro.Index);

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

            var colDescricao = ColunaCom(cabecalho, "descri");
            var nomes = cabecalho
                .Where(c => DocumentoTexto.Normalizar(c.Text).Length > 0)
                .GroupBy(c => c.Column)
                .ToDictionary(g => g.Key, g => DocumentoTexto.Normalizar(g.First().Text));
            var entradas = new List<EntradaDeRevisao>();

            for (var r = i + 1; r < linhas.Count; r++)
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

        var preenchidas = celulas.Select(c => c.Text.Trim()).Where(t => t.Length > 0).ToList();
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

        return Melhor(doc.Tables.Select(t => (Tabela: t, Titulo: DocumentoTexto.Normalizar(t.FirstRowText))))
            // Documentos que põem o título fora da primeira linha: só depois que as tabelas
            // tituladas tiveram a chance de casar.
            ?? Melhor(doc.Tables.Select(t => (Tabela: t,
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
