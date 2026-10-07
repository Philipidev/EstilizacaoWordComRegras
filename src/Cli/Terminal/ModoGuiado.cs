using Spectre.Console;
using WordComplianceValidator.Infrastructure.Profile;

namespace WordComplianceValidator.Cli.Terminal;

/// <summary>
/// Revisão conversada, para quem não usa terminal: abre com duplo clique no Revisor.exe, com
/// arquivos arrastados sobre ele, ou chamando o programa sem argumentos.
/// <para>
/// Antes disso, as duas formas que o <c>publicar.ps1</c> anunciava — "arraste um .docx sobre o
/// Revisor.exe, ou dê duplo clique nele" — terminavam em "Required command was not provided"
/// e a janela fechava antes de alguém conseguir ler. Aqui o programa pergunta o que precisa,
/// aceita o arquivo arrastado para a própria janela, mostra o andamento e, no fim, oferece
/// abrir o resultado no Word.
/// </para>
/// </summary>
public sealed class ModoGuiado(
    Apresentacao tela,
    ExecutorDeRevisao executor,
    string? checklist,
    IReadOnlyList<string> profiles,
    Action<string> abrirArquivo,
    Action<string> abrirPasta)
{
    private IAnsiConsole Console => tela.Console;

    /// <summary>A pessoa saiu pelo menu — o terminal pode fechar sem pedir mais nada.</summary>
    public bool EncerradoPeloMenu { get; private set; }

    public async Task<int> ExecutarAsync(IReadOnlyList<string> arrastados, string versao, CancellationToken cancellationToken)
    {
        var clienteUnico = profiles.Count == 1 ? await NomeDoCliente(profiles[0], cancellationToken) : null;
        tela.BoasVindas(versao, clienteUnico, executor.IaDisponivel ? executor.DescricaoDaIa : null);

        if (checklist is null)
        {
            tela.Erro("Checklist CL-001 não encontrado",
                sugestao: "A planilha deveria estar em 'templates\\checklists', ao lado do Revisor.exe. " +
                          "Copie a pasta inteira do programa, não só o executável.");
            return 1;
        }

        var fila = new Queue<string>(arrastados);
        string? profile = null;
        bool? semIa = null;
        var ultimoCodigo = 0;
        // Revisões do lote de arquivos arrastados, para o resumo final.
        var lote = new List<(string Documento, ResultadoDaRevisao? Resultado)>();

        if (arrastados.Count > 0)
        {
            // Sem isto a pessoa não via que arquivos o programa recebeu até a primeira revisão começar.
            Console.MarkupLine($"  [bold]{arrastados.Count} documento(s) recebido(s):[/]");
            foreach (var a in arrastados) Escolhido(Path.GetFileName(a));
        }

        while (true)
        {
            var documento = fila.Count > 0 ? fila.Dequeue() : PerguntarDocumento();
            if (documento is null) break;

            profile ??= await EscolherProfile(cancellationToken);
            if (profile is null) return 1;
            semIa ??= EscolherModo();

            var resultado = await executor.ExecutarAsync(
                new PedidoDeRevisao(documento, checklist, profile, LocalizadorDeRecursos.SaidaPadrao(documento), semIa.Value),
                tela, cancellationToken);
            if (resultado is not null) ultimoCodigo = Math.Max(ultimoCodigo, resultado.CodigoDeSaida);

            // Vários arquivos arrastados de uma vez: revisa todos em sequência, sem perguntar.
            if (fila.Count > 0)
            {
                lote.Add((documento, resultado));
                Console.WriteLine();
                continue;
            }

            if (lote.Count > 0)
            {
                lote.Add((documento, resultado));
                ResumoDoLote(lote);
                lote.Clear();
            }

            if (!ProximoPasso(resultado, documento)) break;
        }

        EncerradoPeloMenu = true;
        Console.WriteLine();
        Console.MarkupLine("  [dim]Até a próxima.[/]");
        return ultimoCodigo;
    }

    /// <summary>
    /// Pede o documento. Aceita o que o Windows cola quando se arrasta um arquivo para a janela
    /// (caminho entre aspas, ou "&amp; 'C:\…'" no PowerShell) e também uma pasta, da qual lista os
    /// .docx. Enter vazio encerra.
    /// </summary>
    private string? PerguntarDocumento()
    {
        while (true)
        {
            Console.WriteLine();
            Console.MarkupLine($"[bold {Apresentacao.Cor.Destaque}]?[/] [bold]Qual documento você quer revisar?[/]");
            Console.MarkupLine("  [dim]Arraste o arquivo .docx (ou a pasta dele) para esta janela e tecle Enter. Enter vazio sai.[/]");
            var resposta = Console.Prompt(
                new TextPrompt<string>($"  [{Apresentacao.Cor.Destaque}]›[/]").AllowEmpty());

            var caminho = LimparCaminho(resposta);
            if (caminho.Length == 0) return null;

            if (Directory.Exists(caminho))
            {
                var escolhido = EscolherDaPasta(caminho);
                if (escolhido is not null) return escolhido;
                continue;
            }

            if (Validar(caminho) is { } problema)
            {
                tela.Erro(problema.Titulo, caminho, problema.Sugestao);
                continue;
            }
            return Path.GetFullPath(caminho);
        }
    }

    private string? EscolherDaPasta(string pasta)
    {
        var documentos = Directory.GetFiles(pasta, "*.docx")
            .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))      // arquivo de bloqueio do Word
            .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith("-REVISADO", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (documentos.Count == 0)
        {
            tela.Erro("Essa pasta não tem nenhum .docx para revisar", pasta);
            return null;
        }
        if (documentos.Count == 1)
        {
            Escolhido(Path.GetFileName(documentos[0]));
            return documentos[0];
        }

        var escolhido = Console.Prompt(new SelectionPrompt<string>()
            .Title($"[bold]Qual destes?[/] [dim]({documentos.Count} documentos em {Markup.Escape(Path.GetFileName(pasta))})[/]")
            .PageSize(12)
            .MoreChoicesText("[dim](use ↑ ↓ para ver mais)[/]")
            .UseConverter(f => Markup.Escape(Path.GetFileName(f)))
            .AddChoices(documentos));
        Escolhido(Path.GetFileName(escolhido));
        return escolhido;
    }

    /// <summary>
    /// Quadro final quando vários documentos foram revisados de uma vez: os detalhes de cada um
    /// ficaram para trás na rolagem, e o que a pessoa quer saber é qual deles precisa de ajuste.
    /// </summary>
    private void ResumoDoLote(IReadOnlyList<(string Documento, ResultadoDaRevisao? Resultado)> lote)
    {
        Console.WriteLine();
        Console.Write(new Rule($"[bold]Resumo dos {lote.Count} documentos[/]") { Justification = Justify.Left, Style = new Style(Color.Grey) });
        var tabela = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey)
            .AddColumn("Documento")
            .AddColumn(new TableColumn("a corrigir").Centered())
            .AddColumn(new TableColumn("a conferir").Centered())
            .AddColumn("Situação");
        foreach (var (documento, resultado) in lote)
        {
            if (resultado is null)
            {
                tabela.AddRow(Markup.Escape(Path.GetFileName(documento)), "–", "–", $"[{Apresentacao.Cor.Aviso}]não concluída[/]");
                continue;
            }
            var erros = Apresentacao.Agrupar(resultado.Relatorio, Core.Models.Severity.Error).Count;
            var avisos = Apresentacao.Agrupar(resultado.Relatorio, Core.Models.Severity.Warning).Count;
            tabela.AddRow(
                Markup.Escape(Path.GetFileName(documento)),
                erros == 0 ? "[dim]0[/]" : $"[bold {Apresentacao.Cor.Erro}]{erros}[/]",
                avisos == 0 ? "[dim]0[/]" : $"[{Apresentacao.Cor.Aviso}]{avisos}[/]",
                erros > 0 ? $"[{Apresentacao.Cor.Erro}]✗ precisa de ajustes[/]" : $"[{Apresentacao.Cor.Ok}]✓ sem não conformidades[/]");
        }
        Console.Write(tabela);
    }

    /// <summary>
    /// Registra na tela o que foi escolhido num menu. O menu de setas some depois da escolha
    /// e, sem isto, a tela não mostrava qual documento, tipo de revisão ou cliente valeu.
    /// </summary>
    private void Escolhido(string texto) =>
        Console.MarkupLine($"  [{Apresentacao.Cor.Destaque}]›[/] {Markup.Escape(texto)}");

    /// <summary>Tira aspas e o prefixo que o PowerShell acrescenta ao colar um arquivo arrastado.</summary>
    public static string LimparCaminho(string? bruto)
    {
        var t = (bruto ?? string.Empty).Trim();
        if (t.StartsWith("& ", StringComparison.Ordinal)) t = t[2..].Trim();
        return t.Trim('"', '\'').Trim();
    }

    public static (string Titulo, string Sugestao)? Validar(string caminho)
    {
        var extensao = Path.GetExtension(caminho);
        if (extensao.Equals(".doc", StringComparison.OrdinalIgnoreCase))
            return ("Arquivos .doc (Word 97-2003) não são aceitos",
                    "Abra no Word e use Arquivo › Salvar como › Documento do Word (.docx).");
        if (!extensao.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return ("Esse arquivo não é um documento do Word (.docx)", "Arraste um arquivo .docx.");
        if (!File.Exists(caminho))
            return ("Não encontrei esse arquivo", "Confira o caminho ou arraste o arquivo de novo.");
        return null;
    }

    private async Task<string?> EscolherProfile(CancellationToken cancellationToken)
    {
        if (profiles.Count == 0)
        {
            tela.Erro("Nenhum perfil de cliente encontrado",
                sugestao: "A pasta 'profiles', com um arquivo .json por cliente, deveria estar ao lado do Revisor.exe.");
            return null;
        }
        if (profiles.Count == 1) return profiles[0];

        var nomes = new Dictionary<string, string>();
        foreach (var p in profiles) nomes[p] = await NomeDoCliente(p, cancellationToken) ?? Path.GetFileNameWithoutExtension(p);

        Console.WriteLine();
        var perfil = Console.Prompt(new SelectionPrompt<string>()
            .Title($"[bold {Apresentacao.Cor.Destaque}]?[/] [bold]De qual cliente é o documento?[/]")
            .UseConverter(p => $"{Markup.Escape(nomes[p])} [dim]{Markup.Escape(Path.GetFileName(p))}[/]")
            .AddChoices(profiles));
        Escolhido($"Cliente: {nomes[perfil]}");
        return perfil;
    }

    private bool EscolherModo()
    {
        if (!executor.IaDisponivel)
        {
            tela.Info("A IA não está configurada: a revisão confere só as regras automáticas.");
            return true;
        }

        const string Completa = "completa";
        Console.WriteLine();
        var escolha = Console.Prompt(new SelectionPrompt<string>()
            .Title($"[bold {Apresentacao.Cor.Destaque}]?[/] [bold]Que tipo de revisão?[/]")
            .UseConverter(o => o == Completa
                ? "Completa  [dim]regras automáticas + revisão de texto pela IA · ~40 s · ~US$ 0,08[/]"
                : "Rápida    [dim]só regras automáticas · poucos segundos · sem custo[/]")
            .AddChoices(Completa, "rapida"));
        Escolhido(escolha == Completa ? "Revisão completa (com IA)" : "Revisão rápida (só regras automáticas)");
        return escolha != Completa;
    }

    /// <summary>Menu do fim. Devolve <c>true</c> para revisar outro documento.</summary>
    private bool ProximoPasso(ResultadoDaRevisao? resultado, string documento)
    {
        const string AbrirDoc = "abrir", AbrirPasta = "pasta", Outro = "outro", Sair = "sair";
        while (true)
        {
            Console.WriteLine();
            var opcoes = new List<string>();
            if (resultado is not null && File.Exists(resultado.Saida)) opcoes.Add(AbrirDoc);
            opcoes.AddRange([AbrirPasta, Outro, Sair]);

            var escolha = Console.Prompt(new SelectionPrompt<string>()
                .Title($"[bold {Apresentacao.Cor.Destaque}]?[/] [bold]E agora?[/]")
                .UseConverter(o => o switch
                {
                    AbrirDoc => "Abrir o documento revisado no Word",
                    AbrirPasta => "Abrir a pasta do documento",
                    Outro => "Revisar outro documento",
                    _ => "Sair"
                })
                .AddChoices(opcoes));

            Escolhido(escolha switch
            {
                AbrirDoc => "Abrir o documento revisado no Word",
                AbrirPasta => "Abrir a pasta do documento",
                Outro => "Revisar outro documento",
                _ => "Sair"
            });

            switch (escolha)
            {
                case AbrirDoc:
                    abrirArquivo(resultado!.Saida);
                    tela.Info("Abrindo no Word… os comentários ficam no painel Revisão › Comentários.");
                    break;
                case AbrirPasta:
                    abrirPasta(resultado?.Saida is { } s && File.Exists(s) ? s : documento);
                    break;
                case Outro:
                    return true;
                default:
                    return false;
            }
        }
    }

    private static async Task<string?> NomeDoCliente(string profile, CancellationToken cancellationToken)
    {
        try { return (await new JsonClientProfileRepository().LoadAsync(profile, cancellationToken)).Cliente; }
        catch (Exception) { return null; } // profile inválido aparece com a mensagem completa na hora da revisão
    }
}
