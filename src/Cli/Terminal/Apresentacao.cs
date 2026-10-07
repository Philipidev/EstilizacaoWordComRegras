using Spectre.Console;
using Spectre.Console.Rendering;
using WordComplianceValidator.Application.Services;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Infrastructure.OpenAI;

namespace WordComplianceValidator.Cli.Terminal;

/// <summary>
/// Tudo o que o CLI desenha no terminal.
/// <para>
/// Duas audiências com necessidades opostas: uma pessoa olhando a tela — que se orienta por
/// cor, agrupamento e frases curtas, e muitas vezes nunca abriu um terminal — e um pipeline
/// consumindo a saída, que precisa de texto estável e sem escapes ANSI. O Spectre detecta a
/// saída redirecionada e emite texto puro; a barra de progresso, que não faz sentido num log,
/// só aparece quando há uma pessoa do outro lado.
/// </para>
/// <para>
/// Recebe o <see cref="IAnsiConsole"/> em vez de usar o estático para que os testes possam
/// desenhar num console de mentira e conferir o que a pessoa veria.
/// </para>
/// </summary>
public sealed class Apresentacao(IAnsiConsole console)
{
    public IAnsiConsole Console { get; } = console;

    /// <summary>Há uma pessoa interagindo (terminal de verdade, saída não redirecionada).</summary>
    public bool Interativa => Console.Profile.Capabilities.Interactive && !System.Console.IsOutputRedirected;

    // Paleta única: cada cor tem um significado e só ele.
    internal static class Cor
    {
        public const string Destaque = "deepskyblue1";
        public const string Erro = "red";
        public const string Aviso = "yellow";
        public const string Ok = "green";
        public const string Neutro = "grey";
    }

    // ------------------------------------------------------------------ boas-vindas

    /// <summary>Cartão de abertura do modo guiado, no espírito dos CLIs de assistente.</summary>
    public void BoasVindas(string versao, string? cliente, string? modeloIa)
    {
        var dados = new Grid().AddColumn(new GridColumn().NoWrap().PadRight(2)).AddColumn();
        if (cliente is not null)
            dados.AddRow(new Markup("[dim]cliente[/]"), new Markup(Markup.Escape(cliente)));
        dados.AddRow(new Markup("[dim]IA[/]"), modeloIa is null
            ? new Markup($"[{Cor.Aviso}]desligada[/] [dim]— só as regras automáticas serão conferidas[/]")
            : new Markup($"[{Cor.Ok}]disponível[/] [dim]({Markup.Escape(modeloIa)})[/]"));

        var corpo = new Rows(
            new Markup($"[bold {Cor.Destaque}]✻[/] [bold]Revisor de documentos técnicos[/] [dim]· CL-001 · v{Markup.Escape(versao)}[/]"),
            Text.Empty,
            new Markup(
                "Confere o seu [bold].docx[/] contra o checklist da qualidade e devolve uma cópia\n" +
                "com [bold]comentários do Word[/] em cada ponto que precisa de atenção.\n" +
                "[dim]O arquivo original não é alterado.[/]"),
            Text.Empty,
            dados);

        Console.WriteLine();
        Console.Write(new Panel(corpo)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.FromInt32(39)),
            Padding = new Padding(2, 1, 2, 1)
        });
        Console.MarkupLine($"  [dim]Dica: você pode arrastar o arquivo para esta janela. Ctrl+C cancela a qualquer momento.[/]");
        Console.WriteLine();
    }

    // ------------------------------------------------------------------ cabeçalho

    public void Cabecalho(string documento, string cliente, string checklist, string saida, string modo)
    {
        Console.WriteLine();
        Console.Write(new Rule($"[bold]Revisando[/] [{Cor.Destaque}]{Markup.Escape(Path.GetFileName(documento))}[/]")
        {
            Justification = Justify.Left,
            Style = new Style(Color.Grey)
        });

        var grade = new Grid().AddColumn(new GridColumn().NoWrap().PadRight(3)).AddColumn();
        grade.AddRow("[dim]cliente[/]", Markup.Escape(cliente));
        grade.AddRow("[dim]revisão[/]", Markup.Escape(modo));
        grade.AddRow("[dim]checklist[/]", Markup.Escape(Path.GetFileName(checklist)));
        grade.AddRow("[dim]salvar em[/]", Markup.Escape(Path.GetFileName(saida)));
        Console.Write(new Padder(grade, new Padding(2, 0, 0, 0)));
        Console.WriteLine();
    }

    public void Aviso(string mensagem) =>
        Console.MarkupLine($"  [{Cor.Aviso}]![/] {Markup.Escape(mensagem)}");

    public void Info(string mensagem) =>
        Console.MarkupLine($"  [dim]· {Markup.Escape(mensagem)}[/]");

    /// <summary>
    /// Erro dirigido a quem digitou o comando: o que houve, onde, e o que fazer. Uma
    /// <c>FileNotFoundException</c> crua não diz qual das opções de caminho estava errada,
    /// que é a única informação que importa nessa hora.
    /// </summary>
    public void Erro(string titulo, string? caminho = null, string? sugestao = null)
    {
        Console.MarkupLine($"  [{Cor.Erro}]✗[/] [bold]{Markup.Escape(titulo)}[/]");
        if (caminho is not null)
            Console.MarkupLine($"    [dim]{Markup.Escape(caminho)}[/]");
        if (sugestao is not null)
            Console.MarkupLine($"    {Markup.Escape(sugestao)}");
    }

    // ------------------------------------------------------------------ progresso

    /// <summary>
    /// Executa a revisão mostrando o andamento: lendo o documento → conferindo as regras →
    /// gravando os comentários. Um documento real leva de segundos a um minuto; sem sinal, a
    /// janela parece travada e a pessoa fecha. Sem terminal interativo, roda sem desenhar.
    /// </summary>
    public async Task<T> ComProgressoAsync<T>(Func<IProgress<ProgressoDaRevisao>?, Task<T>> trabalho)
    {
        if (!Interativa) return await trabalho(null);

        T resultado = default!;
        await Console.Progress()
            .AutoClear(true)
            .HideCompleted(true)
            .Columns(
                new SpinnerColumn(Spinner.Known.Dots) { Style = new Style(Color.FromInt32(39)) },
                new TaskDescriptionColumn { Alignment = Justify.Left },
                new ProgressBarColumn { CompletedStyle = new Style(Color.FromInt32(39)), FinishedStyle = new Style(Color.Green) },
                new PercentageColumn(),
                new ElapsedTimeColumn { Style = new Style(Color.Grey) })
            .StartAsync(async ctx =>
            {
                var tarefa = ctx.AddTask("Lendo o documento…", maxValue: 1);
                tarefa.IsIndeterminate = true;
                resultado = await trabalho(new ProgressoAoVivo(tarefa));
                tarefa.Value = tarefa.MaxValue;
            });
        return resultado;
    }

    private sealed class ProgressoAoVivo(ProgressTask tarefa) : IProgress<ProgressoDaRevisao>
    {
        private readonly object _trava = new();

        public void Report(ProgressoDaRevisao p)
        {
            // Os checks rodam concorrentemente: sem a trava, duas atualizações se intercalam.
            lock (_trava)
            {
                if (tarefa.IsIndeterminate)
                {
                    tarefa.IsIndeterminate = false;
                    tarefa.MaxValue = p.Total;
                }
                tarefa.Value = p.Concluidos;
                tarefa.Description = p.Concluidos >= p.Total
                    ? "Gravando os comentários no documento…"
                    : $"Conferindo regras [dim]{p.Concluidos}/{p.Total} · {Markup.Escape(p.Ref)}[/]";
            }
        }
    }

    // ------------------------------------------------------------------ resultado

    public void Resumo(DocumentReviewReport relatorio)
    {
        var erros = Agrupar(relatorio, Severity.Error).Count;
        var avisos = Agrupar(relatorio, Severity.Warning).Count;

        Console.WriteLine();
        Console.Write(new Rule("[bold]Resultado[/]") { Justification = Justify.Left, Style = new Style(Color.Grey) });
        Console.WriteLine();

        // Os dois primeiros contam pontos do documento (o que a pessoa vai fazer), não regras do
        // checklist: um mesmo defeito reprova a variante PdA e a Cliente, e "2 não conformes" ao
        // lado de "4 pontos a corrigir" parecia contradição.
        Console.Write(new Padder(new Columns(
            Cartao(erros, "a corrigir", Cor.Erro),
            Cartao(avisos, "a conferir", Cor.Aviso),
            Cartao(relatorio.Passed, "regras ok", Cor.Ok),
            Cartao(relatorio.Skipped, "não se aplicam", Cor.Neutro)).Collapse(), new Padding(2, 0, 0, 0)));

        if (relatorio.Errored > 0)
            Aviso($"{relatorio.Errored} regra(s) não puderam ser avaliadas (erro de execução) — veja com --verbose.");

        Console.WriteLine();
        Console.MarkupLine(erros > 0
            ? $"  [bold {Cor.Erro}]✗ O documento precisa de ajustes.[/] [dim]{erros} ponto(s) a corrigir{(avisos > 0 ? $" e {avisos} a conferir" : "")}.[/]"
            : avisos > 0
                ? $"  [bold {Cor.Ok}]✓ Nenhuma não conformidade.[/] [dim]{avisos} ponto(s) merecem uma conferida.[/]"
                : $"  [bold {Cor.Ok}]✓ Nenhuma não conformidade encontrada.[/]");
    }

    private static IRenderable Cartao(int valor, string rotulo, string cor) =>
        new Panel(new Markup($"[bold {(valor == 0 && cor != Cor.Ok ? Cor.Neutro : cor)}]{valor}[/]\n[dim]{rotulo}[/]").Centered())
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(valor == 0 && cor != Cor.Ok ? Color.Grey23 : Style.Parse(cor).Foreground),
            Padding = new Padding(2, 0, 2, 0)
        };

    /// <summary>
    /// Os achados, agrupados como a pessoa age sobre eles: o que precisa corrigir e o que vale
    /// conferir. O mesmo fato costuma violar vários itens do checklist (o total de páginas fixo
    /// cai em três); aqui, como no .docx, ele aparece uma vez só, citando os itens.
    /// </summary>
    public void Detalhes(DocumentReviewReport relatorio, bool verbose)
    {
        Secao("Precisa corrigir", Cor.Erro, "✗", Agrupar(relatorio, Severity.Error));
        Secao("Vale conferir", Cor.Aviso, "!", Agrupar(relatorio, Severity.Warning));

        if (relatorio.Errored > 0 || verbose)
        {
            var comErro = relatorio.Results.Where(r => r.Status == CheckStatus.Error).ToList();
            if (comErro.Count > 0)
            {
                Console.WriteLine();
                Console.MarkupLine($"  [bold {Cor.Aviso}]Não avaliadas[/]");
                foreach (var r in comErro)
                    Item("!", Cor.Aviso, r.Note ?? "Erro de execução.", Rotulo([r.Ref.ToString()]));
            }
        }

        if (!verbose) return;

        Console.WriteLine();
        var tabela = new Table().Border(TableBorder.Simple).BorderColor(Color.Grey)
            .AddColumn("").AddColumn("Regra").AddColumn("Observação");
        foreach (var r in relatorio.Results)
            tabela.AddRow(Marcador(r.Status), Markup.Escape(r.Ref.ToString()), $"[dim]{Markup.Escape(r.Note ?? "")}[/]");
        Console.Write(tabela);
    }

    private void Secao(string titulo, string cor, string icone, IReadOnlyList<(string Mensagem, IReadOnlyList<string> Regras)> itens)
    {
        if (itens.Count == 0) return;
        Console.WriteLine();
        Console.MarkupLine($"  [bold {cor}]{titulo}[/] [dim]({itens.Count})[/]");
        foreach (var (mensagem, regras) in itens)
            Item(icone, cor, mensagem, Rotulo(regras));
    }

    private void Item(string icone, string cor, string mensagem, string rotulo)
    {
        var grade = new Grid().AddColumn(new GridColumn().NoWrap().PadRight(1)).AddColumn();
        grade.AddRow(new Markup($"[{cor}]{icone}[/]"),
                     new Markup($"{Markup.Escape(mensagem)}\n[dim]{Markup.Escape(rotulo)}[/]"));
        Console.Write(new Padder(grade, new Padding(4, 0, 0, 0)));
    }

    /// <summary>"PS-002:4.3.3 (letra h):Cliente" + ":Pda" → "PS-002 4.3.3 (letra h) · Cliente, PdA".</summary>
    internal static string Rotulo(IEnumerable<string> regras) =>
        string.Join("  ·  ", regras
            .Select(r => r.Split(':'))
            .Where(p => p.Length == 3)
            .GroupBy(p => $"{p[0]} {p[1]}")
            .Select(g => $"{g.Key} ({string.Join(", ", g.Select(p => p[2] == "Pda" ? "PdA" : p[2]).Distinct())})"));

    public static IReadOnlyList<(string Mensagem, IReadOnlyList<string> Regras)> Agrupar(
        DocumentReviewReport relatorio, Severity severidade) =>
        relatorio.Violations
            .Where(v => v.Severity == severidade)
            .GroupBy(v => v.Message.Trim())
            .Select(g => (g.Key, (IReadOnlyList<string>)g.Select(v => v.RuleId).Distinct().ToList()))
            .ToList();

    public void Saida(string caminho, int comentarios)
    {
        var completo = Path.GetFullPath(caminho);
        Console.WriteLine();
        var texto = comentarios > 0
            ? $"[bold]{comentarios}[/] comentário(s) inserido(s) — abra no Word e veja o painel [bold]Revisão › Comentários[/]."
            : "Nenhum comentário a inserir: o arquivo salvo é uma cópia do original.";
        // Link clicável nos terminais que suportam (Windows Terminal, VS Code); nos demais
        // aparece como texto comum.
        var corpo = new Markup(
            $"{texto}\n[link={Markup.Escape(new Uri(completo).AbsoluteUri)}][{Cor.Destaque}]{Markup.Escape(completo)}[/][/]");
        Console.Write(new Panel(corpo)
        {
            Header = new PanelHeader(" Documento revisado "),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey),
            Padding = new Padding(1, 0, 1, 0)
        });
    }

    /// <summary>
    /// Consumo do LLM. Para quem usa, uma linha basta (quanto custou); a tabela por modelo —
    /// que interessa a quem calibra — só com --verbose.
    /// </summary>
    public void Consumo(IReadOnlyList<ConsumoDeModelo> consumo, IReadOnlyDictionary<string, PrecoDeModelo> precos, bool detalhado)
    {
        if (consumo.Count == 0) return;

        decimal total = 0;
        var semPreco = false;
        foreach (var c in consumo)
        {
            precos.TryGetValue(c.Modelo, out var preco);
            if (c.CustoUsd(preco) is { } custo) total += custo; else semPreco = true;
        }

        var chamadas = consumo.Sum(c => c.Chamadas);
        var entrada = consumo.Sum(c => c.Entrada);
        var cache = entrada > 0 ? 100.0 * consumo.Sum(c => c.EntradaEmCache) / entrada : 0;
        Console.MarkupLine(
            $"  [dim]IA: {chamadas} consultas · {cache:0}% servido do cache · custo estimado " +
            $"US$ {total:0.000}{(semPreco ? " (há modelo sem tarifa em OpenAI:Precos)" : "")}[/]");

        if (!detalhado) return;

        var tabela = new Table().Border(TableBorder.Simple).BorderColor(Color.Grey)
            .AddColumn(new TableColumn("Modelo").NoWrap())
            .AddColumn(new TableColumn("Chamadas").RightAligned())
            .AddColumn(new TableColumn("Entrada").RightAligned())
            .AddColumn(new TableColumn("em cache").RightAligned())
            .AddColumn(new TableColumn("Saída").RightAligned())
            .AddColumn(new TableColumn("raciocínio").RightAligned())
            .AddColumn(new TableColumn("Custo (US$)").RightAligned());
        foreach (var c in consumo)
        {
            precos.TryGetValue(c.Modelo, out var preco);
            var custo = c.CustoUsd(preco);
            tabela.AddRow(Markup.Escape(c.Modelo), c.Chamadas.ToString("N0"), c.Entrada.ToString("N0"),
                c.EntradaEmCache.ToString("N0"), c.Saida.ToString("N0"), c.Raciocinio.ToString("N0"),
                custo is null ? "[dim]sem tarifa[/]" : custo.Value.ToString("0.0000"));
        }
        Console.Write(tabela);
    }

    private static string Marcador(CheckStatus status) => status switch
    {
        CheckStatus.Failed => $"[{Cor.Erro}]✗[/]",
        CheckStatus.Passed => $"[{Cor.Ok}]✓[/]",
        CheckStatus.Error => $"[{Cor.Aviso}]![/]",
        _ => "[dim]–[/]"
    };
}
