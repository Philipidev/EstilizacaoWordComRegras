using System.Text;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// Pacote de evidências padrão: um retrato estruturado do documento (folha de rosto, quadro
/// Características, estrutura de títulos, cabeçalhos, índice e amostra do corpo) dentro de um
/// orçamento de caracteres.
/// <para>
/// O corpo é amostrado, não enviado inteiro: relatórios reais têm milhares de parágrafos e o
/// custo cresce linearmente com eles. As seções estruturais — que são onde quase todas as
/// regras do CL-001 olham — vão completas.
/// </para>
/// </summary>
public sealed class DefaultEvidenceSelector : IEvidenceSelector
{
    private const int OrcamentoCaracteresDefault = 24_000;
    // Vale só para os parágrafos fora de tabela da capa; as tabelas têm o teto próprio abaixo.
    private const int ParagrafosFolhaRosto = 120;
    private const int OrcamentoFolhaRosto = 6_000;

    private readonly int _orcamento;

    // Este seletor produz o mesmo retrato para todas as regras, então o pacote é montado uma
    // vez por documento em vez de uma vez por regra — são milhares de parágrafos varridos.
    // A tabela é fraca: some junto com o DocumentContext, sem prender o documento em memória.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DocumentContext, string> _cache = new();

    public DefaultEvidenceSelector(int orcamentoCaracteres = OrcamentoCaracteresDefault)
    {
        _orcamento = orcamentoCaracteres;
    }

    /// <summary>
    /// Instância compartilhada pelo motor genérico e pelos checks dedicados que consultam o
    /// LLM. Um pacote idêntico em todas as chamadas é o que faz o cache de prompt acertar.
    /// </summary>
    public static DefaultEvidenceSelector Compartilhado { get; } = new();

    public string Build(DocumentContext ctx, ChecklistEntry entry) =>
        _cache.GetValue(ctx, Montar);

    /// <summary>
    /// Como ler o pacote. Vai junto da evidência — e não na instrução de cada regra — porque é
    /// igual para todas as regras e fica no trecho reaproveitado pelo cache de prompt.
    /// <para>
    /// O primeiro parágrafo existe por um falso positivo recorrente: o avaliador comparava a
    /// revisão do Cliente da folha índice (0, 1, 2) com a revisão PdA do quadro (0A, 0B, 00) e
    /// acusava divergência em documentos conformes, nos quais as emissões casavam por data.
    /// </para>
    /// </summary>
    private const string ConvencoesDeLeitura =
        """
        ## Como ler este pacote
        - Revisões: a PdA (projetista) e o Cliente numeram revisões cada um no seu sistema — a PdA
          usa 0A, 0B… na etapa de comentários e 00, 01… depois da emissão final; o Cliente costuma
          usar números (0, 1, 2…) com uma letra de tipo de emissão, cuja legenda aparece na própria
          folha. A revisão do Cliente também pode aparecer como sufixo da codificação do Cliente
          (ex.: "…-RT-2" = revisão 2). Códigos diferentes NÃO são divergência por si: a
          correspondência entre as duas numerações se verifica pela ordem, pela data e pela
          descrição de cada emissão. Ao apontar falta de correspondência, cite as datas.
        - Cabeçalhos e rodapés listados são apenas os que o Word exibe; partes gravadas no arquivo
          mas nunca exibidas foram omitidas. Seções são numeradas a partir de 1.
        - "[imagem]" marca conteúdo gráfico (logotipo, assinatura digitalizada): o campo não está
          em branco. Linhas de tabela vêm com as colunas separadas por "|".
        - O corpo do documento é uma amostra, não o texto integral: ausência de algo na amostra não
          prova ausência no documento.

        """;

    private string Montar(DocumentContext ctx)
    {
        var sb = new StringBuilder();
        MontarRetrato(sb, ctx);
        // Inserida depois para não consumir o orçamento da amostra do corpo.
        sb.Insert(0, ConvencoesDeLeitura);
        return sb.ToString();
    }

    private void MontarRetrato(StringBuilder sb, DocumentContext ctx)
    {
        var doc = ctx.Structure;

        sb.AppendLine("## Metadados");
        sb.AppendLine($"- Arquivo: {doc.FileName}");
        sb.AppendLine($"- Seções: {doc.Sections.Count} | Parágrafos: {doc.Paragraphs.Count} | Tabelas: {doc.Tables.Count}");
        sb.AppendLine($"- Alterações pendentes (controle de alterações): {(doc.HasPendingTrackChanges ? "sim" : "não")}");
        sb.AppendLine($"- Comentários abertos: {(doc.HasOpenComments ? "sim" : "não")}");
        sb.AppendLine($"- Campos no corpo: {string.Join(", ", doc.BodyFieldCodes.Distinct().Take(15))}");
        if (doc.Properties is { } props)
            sb.AppendLine($"- Propriedades: autor={props.Creator}; modificado por={props.LastModifiedBy}");
        sb.AppendLine();

        AppendFolhaRosto(sb, doc);
        AppendQuadroCaracteristicas(sb, ctx);
        AppendCabecalhosRodapes(sb, doc);
        AppendEstruturaTitulos(sb, doc);
        AppendIndice(sb, doc);
        AppendTabelas(sb, doc);
        AppendAmostraCorpo(sb, doc, RestanteOrcamento(sb));
    }

    private int RestanteOrcamento(StringBuilder sb) => Math.Max(0, _orcamento - sb.Length);

    private static void AppendFolhaRosto(StringBuilder sb, DocumentStructure doc)
    {
        // O corte por número de parágrafos vale só para o texto solto. Contar células de
        // tabela junto esgotava a cota antes de chegar à folha de rosto: a folha índice do
        // cliente sozinha ocupa ~200 parágrafos de célula, e a folha de rosto — onde estão
        // código, título e cliente — vem depois dela. O avaliador então concluía, corretamente
        // para o que recebia e erradamente para o documento, que esses campos não existiam.
        var cabeca = DocumentoTexto.FolhaDeRosto(doc);

        if (cabeca.Count == 0) return;
        sb.AppendLine("## Folha de rosto / capa (conteúdo anterior ao primeiro título)");

        // As tabelas da capa saem linha a linha, com as colunas preservadas. Achatá-las em
        // bullets soltos destrói a informação que mais importa aqui: a folha índice tem
        // "REV." e "EMISSÃO" em colunas distintas, e lida como células soltas ela vira um
        // código de revisão "0/B" inexistente — origem de acusações de divergência entre a
        // capa e o quadro "Características" que o documento não tem.
        // Teto próprio para a capa: sem título nenhum, TakeWhile devolve o documento inteiro,
        // e a capa comeria o orçamento das demais seções da evidência.
        var gasto = 0;
        foreach (var idx in cabeca.Where(p => p.TableIndex is not null)
                                  .Select(p => p.TableIndex!.Value)
                                  .Distinct())
        {
            if (gasto >= OrcamentoFolhaRosto) break;
            var tabela = doc.Tables.FirstOrDefault(t => t.Index == idx);
            if (tabela is null) continue;

            // A observação sobre o layout é descritiva, não uma instrução de julgamento: em
            // formulário de capa o valor costuma vir na linha seguinte, na mesma coluna do
            // rótulo ("Nº DOC. Nº PROJETISTA:" numa linha, o código na próxima), e sem isso
            // o rótulo isolado é lido como campo em branco.
            var titulo = $"### Tabela {idx} da capa (linha a linha, colunas separadas por |; " +
                         "é um formulário: o valor de um rótulo pode estar na linha seguinte, " +
                         "na mesma coluna)";
            sb.AppendLine(titulo);
            gasto += titulo.Length;
            foreach (var linha in LinhasDaTabela(tabela))
            {
                if (gasto >= OrcamentoFolhaRosto) break;
                sb.AppendLine($"- {linha}");
                gasto += linha.Length;
            }
        }

        var soltos = cabeca.Where(p => p.TableIndex is null)
                           .Select(p => Conteudo(p.Text, p.ImageCount))
                           .Where(t => t.Length > 0)
                           .Take(ParagrafosFolhaRosto)
                           .ToList();
        if (soltos.Count > 0)
        {
            sb.AppendLine("### Parágrafos fora de tabela");
            foreach (var t in soltos) sb.AppendLine($"- {t}");
        }
        sb.AppendLine();
    }

    /// <summary>
    /// Linhas do quadro com as colunas preservadas, resumindo a grade de controle de folhas.
    /// <para>
    /// A projeção rótulo→valor achatava as duas colunas do quadro ("Pimenta de Ávila" e
    /// "Cliente"), e o par <c>RN-816-RL-67456-00 | QD5-PDA-26-04-095-RT-1</c> era lido como um
    /// único registro — daí a acusação de que a revisão PdA "-1" brigava com o histórico "00",
    /// quando o "-1" é a revisão do Cliente e corresponde à revisão 1 da folha índice.
    /// </para>
    /// A grade de folhas é resumida porque ocupa a maior parte das linhas do quadro e não
    /// carrega informação por linha — e porque, servida crua, era confundida com numeração
    /// da página do quadro.
    /// </summary>
    private static IReadOnlyList<string> LinhasDoQuadro(ExtractedTable quadro, int maxLinhas = 40)
    {
        var saida = new List<string>();
        var grade = new List<IReadOnlyList<ExtractedTableCell>>();

        void FecharGrade()
        {
            if (grade.Count == 0) return;

            var folhas = grade.SelectMany(l => l)
                .Select(c => c.Text.Trim())
                .Where(t => int.TryParse(t, out _))
                .Select(int.Parse)
                .ToList();
            var marcas = grade.SelectMany(l => l)
                .Count(c => c.Text.Trim().Equals("x", StringComparison.OrdinalIgnoreCase));
            var faixa = folhas.Count > 0 ? $", folhas {folhas.Min()} a {folhas.Max()}" : string.Empty;

            saida.Add($"[grade de controle de folhas: {grade.Count} linhas{faixa}, {marcas} marcações " +
                      "\"x\" — indica quais folhas mudaram em cada revisão; não é a numeração da " +
                      "página do quadro]");
            grade.Clear();
        }

        // Cabeçalhos da grade ("Rev. | 0A | 0B | 00 …" e "Pag. | | | …") ficam pendentes até
        // se saber se vem grade depois. Deixá-los soltos enquanto as linhas de dados são
        // resumidas mostra um "Pag." seguido de células vazias, que o avaliador reportou como
        // formulário não preenchido — sendo que os dados existem, no resumo logo abaixo.
        var pendentes = new List<IReadOnlyList<ExtractedTableCell>>();

        void EmitirPendentes()
        {
            foreach (var p in pendentes)
                saida.Add(string.Join(" | ", p.Select(c => Conteudo(c.Text, c.ImageCount))));
            pendentes.Clear();
        }

        foreach (var linha in quadro.Cells.GroupBy(c => c.Row).OrderBy(g => g.Key))
        {
            var celulas = linha.OrderBy(c => c.Column).ToList();

            if (QuadroCaracteristicas.EhLinhaDeGradeDeFolhas(celulas))
            {
                grade.AddRange(pendentes);
                pendentes.Clear();
                grade.Add(celulas);
                continue;
            }

            if (EhCabecalhoDeGrade(celulas)) { pendentes.Add(celulas); continue; }

            FecharGrade();
            EmitirPendentes();

            var texto = string.Join(" | ", celulas.Select(c => Conteudo(c.Text, c.ImageCount)));
            if (!texto.Any(char.IsLetterOrDigit)) continue;

            if (saida.Count >= maxLinhas) { saida.Add("… (linhas restantes omitidas)"); return saida; }
            saida.Add(texto);
        }

        FecharGrade();
        EmitirPendentes();
        return saida;
    }

    /// <summary>
    /// Cabeçalho da grade de folhas: primeira célula "Rev."/"Pag." e as demais vazias ou
    /// códigos de revisão. O cabeçalho do histórico ("Rev. | Data | Emissor | …") não casa,
    /// porque suas demais células são palavras — e ele precisa continuar saindo inteiro.
    /// </summary>
    internal static bool EhCabecalhoDeGrade(IReadOnlyList<ExtractedTableCell> celulas)
    {
        if (celulas.Count < 4) return false;

        var primeira = celulas[0].Text.Trim().TrimEnd('.');
        if (!primeira.Equals("Rev", StringComparison.OrdinalIgnoreCase)
         && !primeira.Equals("Pag", StringComparison.OrdinalIgnoreCase)) return false;

        return celulas.Skip(1)
            .Select(c => c.Text.Trim().Replace(" ", string.Empty))
            .Where(t => t.Length > 0)
            .All(t => CodigoDeRevisao.IsMatch(t));
    }

    private static readonly System.Text.RegularExpressions.Regex CodigoDeRevisao =
        new(@"^(0[A-Za-z]|\d{2}|Rev\.?|Pag\.?)$",
            System.Text.RegularExpressions.RegexOptions.Compiled
          | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Conteúdo de uma célula ou parágrafo para a evidência. Imagens são declaradas em vez de
    /// desaparecerem: na folha de rosto os campos "CONTRATANTE"/"CONTRATADA" são preenchidos
    /// com logo e a assinatura do aprovador com imagem digitalizada — sem a marca, um campo
    /// preenchido chega ao avaliador indistinguível de um campo em branco.
    /// </summary>
    private static string Conteudo(string? texto, int imagens)
    {
        var t = Resumo(texto, 120);
        if (imagens <= 0) return t;
        var marca = imagens == 1 ? "[imagem]" : $"[{imagens} imagens]";
        return t.Length == 0 ? marca : $"{t} {marca}";
    }

    /// <summary>
    /// Linhas de uma tabela com as colunas preservadas. Linhas sem texto e sem imagem são
    /// descartadas: formulários de capa trazem dezenas de linhas em branco reservadas para
    /// revisões futuras, e elas consomem orçamento sem informar nada.
    /// </summary>
    private static IEnumerable<string> LinhasDaTabela(ExtractedTable tabela, int maxLinhas = 40)
    {
        var linhas = tabela.Cells
            .GroupBy(c => c.Row)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(c => c.Column)
                          .Select(c => Conteudo(c.Text, c.ImageCount))
                          .ToList())
            .Where(celulas => celulas.Any(c => c.Length > 0))
            .Select(celulas => string.Join(" | ", celulas))
            .ToList();

        foreach (var l in linhas.Take(maxLinhas)) yield return l;
        if (linhas.Count > maxLinhas) yield return $"… (+{linhas.Count - maxLinhas} linhas)";
    }

    private static void AppendQuadroCaracteristicas(StringBuilder sb, DocumentContext ctx)
    {
        var aliases = DocumentoTexto.Lista(ctx.Profile.Get("quadroCaracteristicas.aliases"),
            "Características do Documento|Quadro de Características|Características");
        var quadro = QuadroCaracteristicas.Find(ctx.Structure, aliases);
        if (quadro is null) return;

        sb.AppendLine("## Quadro \"Características do Documento\" (linha a linha, colunas separadas por |)");
        foreach (var linha in LinhasDoQuadro(quadro)) sb.AppendLine($"- {linha}");
        sb.AppendLine();
    }

    private static void AppendCabecalhosRodapes(StringBuilder sb, DocumentStructure doc)
    {
        if (doc.Headers.Count == 0 && doc.Footers.Count == 0) return;
        sb.AppendLine("## Cabeçalhos e rodapés exibidos");
        AppendPartes(sb, "cabeçalho", doc.Headers);
        AppendPartes(sb, "rodapé", doc.Footers);
        sb.AppendLine();
    }

    /// <summary>
    /// Partes iguais são agrupadas numa linha com as seções que as usam. Relatórios reais
    /// repetem o mesmo cabeçalho em quase todas as seções; listado parte a parte, ele ocupava
    /// boa parte do pacote e escondia a única seção que destoava.
    /// </summary>
    private static void AppendPartes(StringBuilder sb, string rotulo, IReadOnlyList<ExtractedHeaderFooter> partes)
    {
        var grupos = partes
            .GroupBy(p => (p.Kind, Texto: Resumo(p.Text, 300), p.ImageCount,
                           Campos: string.Join("/", p.FieldCodes.Distinct())))
            .Select(g => (g.Key, Secoes: g.Select(p => p.SectionIndex).OfType<int>().Order().ToList()))
            .OrderBy(g => g.Secoes.Count > 0 ? g.Secoes[0] : int.MaxValue)
            .ThenBy(g => g.Key.Kind, StringComparer.Ordinal);

        foreach (var (chave, secoes) in grupos)
        {
            var onde = secoes.Count switch
            {
                0 => "seção ?",
                1 => $"seção {secoes[0] + 1}",
                _ => $"seções {string.Join(", ", secoes.Select(s => s + 1))}"
            };
            var texto = chave.Texto.Length == 0 ? "(sem texto)" : chave.Texto;
            sb.AppendLine($"- [{rotulo} tipo={chave.Kind} {onde} imagens={chave.ImageCount} " +
                          $"campos={chave.Campos}] {texto}");
        }
    }

    private static void AppendEstruturaTitulos(StringBuilder sb, DocumentStructure doc)
    {
        var titulos = DocumentoTexto.Titulos(doc);
        if (titulos.Count == 0) return;

        sb.AppendLine("## Estrutura de títulos (painel de navegação)");
        foreach (var t in titulos.Take(120))
            sb.AppendLine($"{new string(' ', (t.OutlineLevel ?? 0) * 2)}- (nível {t.OutlineLevel + 1}) {Resumo(t.Text, 160)}");
        if (titulos.Count > 120) sb.AppendLine($"  … (+{titulos.Count - 120} títulos)");
        sb.AppendLine();
    }

    private static void AppendIndice(StringBuilder sb, DocumentStructure doc)
    {
        var entradas = DocumentoTexto.EntradasIndice(doc);
        if (entradas.Count == 0)
        {
            sb.AppendLine("## Índice");
            sb.AppendLine("- Nenhuma entrada de índice localizada no documento.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"## Índice ({entradas.Count} entradas)");
        foreach (var e in entradas.Take(80))
            sb.AppendLine($"- {Resumo(e.Text, 160)}");
        if (entradas.Count > 80) sb.AppendLine($"- … (+{entradas.Count - 80} entradas)");
        sb.AppendLine();
    }

    private static void AppendTabelas(StringBuilder sb, DocumentStructure doc)
    {
        if (doc.Tables.Count == 0) return;
        sb.AppendLine($"## Tabelas ({doc.Tables.Count})");
        foreach (var t in doc.Tables.Take(20))
            sb.AppendLine($"- Tabela {t.Index}: {Resumo(t.FirstRowText ?? "(sem cabeçalho)", 200)}");
        if (doc.Tables.Count > 20) sb.AppendLine($"- … (+{doc.Tables.Count - 20} tabelas)");
        sb.AppendLine();
    }

    private static void AppendAmostraCorpo(StringBuilder sb, DocumentStructure doc, int orcamento)
    {
        if (orcamento <= 500) return;

        var corpo = DocumentoTexto.Corpo(doc);
        if (corpo.Count == 0) return;

        sb.AppendLine("## Amostra do corpo do documento");
        var usados = 0;
        var incluidos = 0;
        foreach (var p in corpo)
        {
            var linha = $"- [{p.EffectiveFont} {p.EffectiveFontSize:0.#}pt, {p.Alignment}] {Resumo(p.Text, 400)}";
            if (usados + linha.Length > orcamento) break;
            sb.AppendLine(linha);
            usados += linha.Length;
            incluidos++;
        }
        if (incluidos < corpo.Count)
            sb.AppendLine($"- … (+{corpo.Count - incluidos} parágrafos de corpo omitidos por orçamento)");
        sb.AppendLine();
    }

    private static string Resumo(string? texto, int max)
    {
        var t = (texto ?? string.Empty).Trim().Replace("\n", " ").Replace("\r", " ");
        return t.Length <= max ? t : t[..max] + "…";
    }
}
