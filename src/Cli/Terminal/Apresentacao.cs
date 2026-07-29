using Spectre.Console;
using WordComplianceValidator.Application.Services;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Cli.Terminal;

/// <summary>
/// Apresentação do resultado no terminal. Fica separada do <c>Program.cs</c> porque o CLI tem
/// duas audiências com necessidades opostas: uma pessoa lendo na tela, que se orienta por cor
/// e agrupamento, e um pipeline consumindo a saída, que precisa de texto estável e sem
/// escapes ANSI. Tudo aqui degrada sozinho quando a saída está redirecionada — o Spectre
/// detecta e emite texto puro.
/// </summary>
public static class Apresentacao
{
    public static void Cabecalho(string documento, string cliente, string checklist, string saida)
    {
        AnsiConsole.Write(new Rule("[bold]Revisor[/] [dim]· CL-001[/]") { Justification = Justify.Left });
        AnsiConsole.WriteLine();

        var grade = new Grid().AddColumn(new GridColumn().NoWrap().PadRight(3)).AddColumn();
        grade.AddRow("[dim]Documento[/]", Markup.Escape(Path.GetFileName(documento)));
        grade.AddRow("[dim]Cliente[/]", Markup.Escape(cliente));
        grade.AddRow("[dim]Checklist[/]", Markup.Escape(Path.GetFileName(checklist)));
        grade.AddRow("[dim]Saída[/]", Markup.Escape(Path.GetFileName(saida)));
        AnsiConsole.Write(grade);
        AnsiConsole.WriteLine();
    }

    public static void Aviso(string mensagem) =>
        AnsiConsole.MarkupLine($"[yellow]![/] {Markup.Escape(mensagem)}");

    public static void Info(string mensagem) =>
        AnsiConsole.MarkupLine($"[dim]·[/] [dim]{Markup.Escape(mensagem)}[/]");

    /// <summary>
    /// Erro dirigido a quem digitou o comando: o que houve, onde, e o que fazer. Uma
    /// <c>FileNotFoundException</c> crua não diz qual das quatro opções de caminho estava
    /// errada, que é a única informação que importa nessa hora.
    /// </summary>
    public static void Erro(string titulo, string? caminho = null, string? sugestao = null)
    {
        AnsiConsole.MarkupLine($"[red]✗[/] [bold]{Markup.Escape(titulo)}[/]");
        if (caminho is not null)
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(caminho)}[/]");
        if (sugestao is not null)
            AnsiConsole.MarkupLine($"  {Markup.Escape(sugestao)}");
    }

    public static IProgress<ProgressoDaRevisao>? BarraDeProgresso(out IDisposable? escopo)
    {
        // Barra de progresso em saída redirecionada vira lixo no log do pipeline.
        if (Console.IsOutputRedirected || !AnsiConsole.Profile.Capabilities.Interactive)
        {
            escopo = null;
            return null;
        }

        var contexto = new ProgressoAoVivo();
        escopo = contexto;
        return contexto;
    }

    public static void Resumo(DocumentReviewReport relatorio)
    {
        AnsiConsole.WriteLine();
        // Avisos entram no resumo porque geram comentário no .docx. Sem eles, a linha dizia
        // "nenhuma não conformidade" logo acima de "7 comentários inseridos", e a pessoa
        // ficava sem saber de onde vieram os sete.
        var avisos = relatorio.Violations.Count(v => v.Severity == Severity.Warning);

        var partes = new List<string>
        {
            relatorio.Failed > 0
                ? $"[red]✗ {relatorio.Failed} não conformes[/]"
                : "[green]✓ nenhuma não conformidade[/]"
        };
        if (avisos > 0) partes.Add($"[yellow]! {avisos} aviso(s)[/]");
        partes.Add($"[green]✓ {relatorio.Passed} conformes[/]");
        partes.Add($"[dim]– {relatorio.Skipped} não aplicáveis[/]");
        if (relatorio.Errored > 0) partes.Add($"[yellow]! {relatorio.Errored} com erro de execução[/]");

        AnsiConsole.MarkupLine(string.Join("  [dim]·[/]  ", partes));
    }

    public static void Detalhes(DocumentReviewReport relatorio, bool verbose)
    {
        var mostrar = verbose
            ? relatorio.Results
            // Sem verbose, além das falhas aparecem os itens que produziram algum achado:
            // um aviso vira comentário no documento e precisa estar rastreável na tela.
            : relatorio.Results.Where(r =>
                r.Status is CheckStatus.Failed or CheckStatus.Error || r.Violations.Count > 0);

        var lista = mostrar.ToList();
        if (lista.Count == 0) return;

        AnsiConsole.WriteLine();
        foreach (var r in lista)
        {
            AnsiConsole.MarkupLine($"{Marcador(r.Status)} [bold]{Markup.Escape(r.Ref.ToString())}[/]");

            // Padder em vez de espaços no início da string: MarkupLine quebra a linha na
            // coluna 0, e mensagens longas — que são a maioria — perdiam o recuo no meio.
            foreach (var v in r.Violations)
                Recuado($"{CorDaSeveridade(v.Severity)} {Markup.Escape(v.Message)}");

            if (verbose && !string.IsNullOrEmpty(r.Note))
                Recuado($"[dim]{Markup.Escape(r.Note)}[/]");
        }
    }

    public static void Saida(string caminho, int comentarios)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(comentarios > 0
            ? $"[bold]{comentarios}[/] comentário(s) inserido(s)"
            : "Nenhum comentário a inserir");
        AnsiConsole.MarkupLine($"[blue]{Markup.Escape(Path.GetFullPath(caminho))}[/]");
    }

    private static void Recuado(string markup) =>
        AnsiConsole.Write(new Padder(new Markup(markup), new Padding(4, 0, 0, 0)));

    private static string Marcador(CheckStatus status) => status switch
    {
        CheckStatus.Failed => "[red]✗[/]",
        CheckStatus.Passed => "[green]✓[/]",
        CheckStatus.Error => "[yellow]![/]",
        _ => "[dim]–[/]"
    };

    private static string CorDaSeveridade(Severity severidade) => severidade switch
    {
        Severity.Error => "[red]•[/]",
        Severity.Warning => "[yellow]•[/]",
        _ => "[dim]•[/]"
    };

    /// <summary>
    /// Uma linha que se reescreve com o andamento. Documento real leva minutos e dezenas de
    /// chamadas de rede; sem sinal, o terminal fica indistinguível de travado.
    /// </summary>
    private sealed class ProgressoAoVivo : IProgress<ProgressoDaRevisao>, IDisposable
    {
        private readonly object _trava = new();
        private bool _escreveu;

        public void Report(ProgressoDaRevisao p)
        {
            // Os checks rodam concorrentemente: sem a trava, duas escritas se intercalam.
            lock (_trava)
            {
                var largura = Math.Max(20, Console.WindowWidth - 1);
                var texto = $"  Revisando  {p.Concluidos}/{p.Total}  {p.Ref}";
                if (texto.Length > largura) texto = texto[..largura];

                Console.Write('\r' + texto.PadRight(largura));
                _escreveu = true;
            }
        }

        public void Dispose()
        {
            lock (_trava)
            {
                if (!_escreveu) return;
                Console.Write('\r' + new string(' ', Math.Max(20, Console.WindowWidth - 1)) + '\r');
            }
        }
    }
}
