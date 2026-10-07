using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using WordComplianceValidator.Cli.Terminal;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Infrastructure.Checks;
using WordComplianceValidator.Infrastructure.Excel;
using WordComplianceValidator.Infrastructure.OpenAI;
using WordComplianceValidator.Infrastructure.OpenXml;
using WordComplianceValidator.Infrastructure.Profile;

// Acentos, "✓" e as bordas arredondadas dependem de UTF-8; o console do Windows ainda nasce
// numa code page legada.
if (!Console.IsOutputRedirected) Console.OutputEncoding = Encoding.UTF8;
else
{
    // Redirecionada (arquivo, pipeline de CI), a saída seguia a code page do console: "—", "…"
    // e "✓" viravam "?" e os acentos só sobreviviam por sorte. Escreve UTF-8 direto no stream,
    // sem trocar a code page de quem chamou.
    Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
}
if (Console.IsErrorRedirected)
    Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });

// O programa fala português. Num Windows com interface em inglês, os textos do próprio
// System.CommandLine ("Usage:", "Show help and usage information", "Required command was not
// provided") saíam em inglês no meio da ajuda em português.
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("pt-BR");
System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("pt-BR");

var construtor = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true);
// Só existe no pacote gerado por "publicar.ps1 -EmbutirChave" (ver o csproj): é como a equipe
// recebe o Revisor com a IA pronta, sem configurar nada. Arquivo local e variável de ambiente,
// adicionados depois, continuam vencendo.
using var embutida = Assembly.GetExecutingAssembly().GetManifestResourceStream("Revisor.ConfiguracaoEmbutida.json");
if (embutida is not null) construtor.AddJsonStream(embutida);
var config = construtor
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var openAi = new OpenAiSettings();
config.GetSection("OpenAI").Bind(openAi);
// A variável de ambiente vence o arquivo: é o canal recomendado para a chave.
if (Environment.GetEnvironmentVariable("OPENAI_API_KEY") is { Length: > 0 } chaveDoAmbiente)
    openAi.ApiKey = chaveDoAmbiente;
if (string.IsNullOrWhiteSpace(openAi.Model)) openAi.Model = OpenAiSettings.ModeloPadrao;

var executor = new ExecutorDeRevisao(openAi);
var tela = new Apresentacao(AnsiConsole.Console);
var versao = Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

// ---- modo guiado ----
// Sem argumentos (duplo clique) ou só com .docx (arquivos arrastados sobre o Revisor.exe), e com
// uma pessoa do outro lado: conversa em vez de exigir a sintaxe de linha de comando. Num
// pipeline (saída redirecionada) cai no comportamento de sempre, que mostra a ajuda.
var soDocumentos = args.Length > 0 && args.All(a =>
    a.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) && File.Exists(a));
if ((args.Length == 0 || soDocumentos) && tela.Interativa)
{
    using var cancelamento = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        // Primeiro Ctrl+C cancela a revisão em andamento; o segundo encerra de vez.
        if (cancelamento.IsCancellationRequested) return;
        e.Cancel = true;
        cancelamento.Cancel();
    };

    var guiado = new ModoGuiado(tela, executor, LocalizadorDeRecursos.Checklist(),
        LocalizadorDeRecursos.Profiles(), Janela.AbrirArquivo, Janela.AbrirPasta);
    int codigo;
    try
    {
        codigo = await guiado.ExecutarAsync(args.Select(Path.GetFullPath).ToList(), versao, cancelamento.Token);
    }
    catch (OperationCanceledException)
    {
        codigo = 1;
    }

    // Janela aberta só para este processo: sem a pausa, uma mensagem de erro some na hora.
    if (!guiado.EncerradoPeloMenu && Janela.Propria())
    {
        AnsiConsole.MarkupLine("\n  [dim]Pressione qualquer tecla para fechar.[/]");
        Console.ReadKey(intercept: true);
    }
    return codigo;
}

// ---- comandos (scripts, CI e quem prefere linha de comando) ----
var docOpt = new Option<FileInfo>("--doc", "-d")
{
    Description = "Documento .docx a revisar.",
    Required = true
};
var checklistOpt = new Option<FileInfo>("--checklist")
{
    Description = "Caminho do checklist .xlsx. Sem ele, usa o que acompanha o programa.",
    // Antes o default era montado sobre Directory.GetCurrentDirectory(), o que só acerta quando
    // o programa roda da raiz do repositório. O localizador parte do executável e sobe a árvore,
    // então também funciona num binário publicado, chamado de qualquer pasta.
    DefaultValueFactory = _ => new FileInfo(
        LocalizadorDeRecursos.Checklist()
        ?? Path.Combine(AppContext.BaseDirectory, "templates", "checklists", "CL-001-CL00100.xlsx"))
};
var profileOpt = new Option<FileInfo?>("--profile")
{
    Description = "Profile JSON do cliente. Opcional quando existe um único profile na pasta 'profiles'."
};
var outOpt = new Option<FileInfo?>("--out")
{
    Description = "Arquivo .docx de saída. Sem ele: <documento>-REVISADO.docx, na pasta do documento."
};
var noLlmOpt = new Option<bool>("--no-llm") { Description = "Revisão rápida: sem IA, só as regras automáticas." };
var verboseOpt = new Option<bool>("--verbose") { Description = "Mostra também os itens conformes/não aplicáveis e o consumo detalhado da IA." };
var onlyIaSimOpt = new Option<bool>("--only-ia-sim")
{
    Description = "Executa apenas itens marcados IA=Sim no checklist (comportamento legado)."
};

var reviewCmd = new Command("review", "Revisa um documento .docx contra o checklist CL-001.")
{
    docOpt, checklistOpt, profileOpt, outOpt, noLlmOpt, verboseOpt, onlyIaSimOpt
};

// A ação devolve o código de saída, e é ele que o processo devolve. O código 2 já se perdeu
// uma vez — Environment.ExitCode era sobrescrito pelo retorno da invocação —, e um gate de CI
// aprovava documentos reprovados. CliExitCodeTests guarda isso no nível do processo.
reviewCmd.SetAction(async (parsed, cancellationToken) =>
{
    var doc = parsed.GetRequiredValue(docOpt);
    var checklist = parsed.GetRequiredValue(checklistOpt);

    // Validação antes de qualquer trabalho: cada mensagem diz qual opção está errada e com
    // que caminho ela ficou.
    if (ModoGuiado.Validar(doc.FullName) is { } problema)
    {
        tela.Erro(problema.Titulo, doc.FullName, problema.Sugestao);
        return 1;
    }

    if (!checklist.Exists)
    {
        tela.Erro("Checklist não encontrado", checklist.FullName,
            "Passe o caminho em --checklist ou deixe o .xlsx em 'templates/checklists'.");
        return 1;
    }

    var profilePath = ResolverProfile(parsed.GetValue(profileOpt), tela);
    if (profilePath is null) return 1;

    var pedido = new PedidoDeRevisao(
        doc.FullName, checklist.FullName, profilePath,
        parsed.GetValue(outOpt)?.FullName ?? LocalizadorDeRecursos.SaidaPadrao(doc.FullName),
        SemIa: parsed.GetValue(noLlmOpt),
        SoIaSim: parsed.GetValue(onlyIaSimOpt),
        Detalhado: parsed.GetValue(verboseOpt));

    var resultado = await executor.ExecutarAsync(pedido, tela, cancellationToken);
    return resultado?.CodigoDeSaida ?? 1;
});

// Profile explícito vence; sem ele, só resolve sozinho quando a escolha é inequívoca. Havendo
// vários, listar os nomes é mais útil do que exigir que a pessoa vá procurar a pasta.
static string? ResolverProfile(FileInfo? informado, Apresentacao tela)
{
    if (informado is not null)
    {
        if (informado.Exists) return informado.FullName;
        tela.Erro("Profile não encontrado", informado.FullName, "Confira o caminho em --profile.");
        return null;
    }

    var disponiveis = LocalizadorDeRecursos.Profiles();

    if (disponiveis.Count == 1) return disponiveis[0];

    if (disponiveis.Count == 0)
    {
        tela.Erro("Nenhum profile de cliente encontrado",
            LocalizadorDeRecursos.PastaDeProfiles() ?? "(pasta 'profiles' não localizada)",
            "Informe um com --profile ou coloque um .json na pasta 'profiles'.");
        return null;
    }

    tela.Erro("Existe mais de um profile; escolha um com --profile", null,
        "Disponíveis: " + string.Join(", ", disponiveis.Select(Path.GetFileName)));
    return null;
}

// ---- checklist dump (debug) ----
var dumpCmd = new Command("dump-checklist", "Lista entradas do checklist .xlsx.")
{
    checklistOpt
};
dumpCmd.SetAction(async (parsed, cancellationToken) =>
{
    var checklist = parsed.GetRequiredValue(checklistOpt);
    if (!checklist.Exists)
    {
        tela.Erro("Checklist não encontrado", checklist.FullName, "Passe o caminho em --checklist.");
        return 1;
    }
    var entries = await new ExcelChecklistRepository().LoadAsync(checklist.FullName, cancellationToken);
    Console.WriteLine($"Total: {entries.Count} entradas");
    foreach (var e in entries)
        Console.WriteLine($"  {e.Ref,-40} IA={(e.IaAutomatizavel ? "Sim" : "Não")}  {e.Titulo}");
    return 0;
});

// ---- pacote de evidências (debug) ----
// Mostra exatamente o que o motor semântico lê. Sem isso, calibrar um falso positivo do LLM
// era adivinhar se o problema estava no modelo ou no recorte enviado a ele.
var evidenciaDocOpt = new Option<FileInfo>("--doc", "-d") { Description = "Documento .docx.", Required = true };
var evidenciaProfileOpt = new Option<FileInfo?>("--profile") { Description = "Profile JSON do cliente." };
var evidenciaCmd = new Command("dump-evidencia", "Mostra o pacote de evidências enviado ao LLM.")
{
    evidenciaDocOpt, evidenciaProfileOpt
};
evidenciaCmd.SetAction(async (parsed, cancellationToken) =>
{
    var doc = parsed.GetRequiredValue(evidenciaDocOpt);
    // Antes um --doc inexistente saía com 1 sem dizer nada.
    if (ModoGuiado.Validar(doc.FullName) is { } problema)
    {
        tela.Erro(problema.Titulo, doc.FullName, problema.Sugestao);
        return 1;
    }
    var profilePath = ResolverProfile(parsed.GetValue(evidenciaProfileOpt), tela);
    if (profilePath is null) return 1;

    var perfil = await new JsonClientProfileRepository().LoadAsync(profilePath, cancellationToken);
    var estrutura = new DocxStructureExtractor().ExtractFromFile(doc.FullName);
    var contexto = new DocumentContext(doc.FullName, estrutura, perfil);
    var entrada = new ChecklistEntry(new ChecklistRef("debug", "debug", ChecklistPadrao.Cliente),
        null, "", "", "", "", false, null);
    Console.WriteLine(DefaultEvidenceSelector.Compartilhado.Build(contexto, entrada));
    return 0;
});

var root = new RootCommand(
    "Revisa documentos técnicos .docx contra o checklist CL-001 e devolve uma cópia comentada.\n" +
    "Sem argumentos, abre o modo guiado.")
{
    reviewCmd, dumpCmd, evidenciaCmd
};

// O help gerado lista as opções, mas não mostra como o comando se parece de verdade. Os
// exemplos cobrem os usos reais: o guiado, o mínimo, o de pipeline e o de diagnóstico.
foreach (var ajuda in root.Options.OfType<HelpOption>())
{
    if (ajuda.Action is SynchronousCommandLineAction padrao)
        ajuda.Action = new AjudaComExemplos(padrao);
}

var analisado = root.Parse(args);

// Erros de digitação ("Option '--doc' is required.", "'xyz' was not matched…") vêm do
// System.CommandLine em inglês, seguidos da ajuda inteira. Aqui viram uma frase em português e
// a indicação de onde ver as opções.
if (analisado.Errors.Count > 0 && !args.Any(a => a is "-h" or "-?" or "--help"))
{
    var mensagens = analisado.Errors.Select(e => ErrosDeLinhaDeComando.Traduzir(e.Message)).Distinct().ToList();
    // "Não reconheci 'xyz'" já diz tudo; "informe um comando" ao lado só confunde.
    if (mensagens.Count > 1)
        mensagens.RemoveAll(m => m.Contains("comando necessário", StringComparison.OrdinalIgnoreCase)
                              || m.StartsWith("Informe um comando", StringComparison.Ordinal));
    foreach (var m in mensagens) tela.Erro(m);
    var comando = analisado.CommandResult.Command is RootCommand ? "" : analisado.CommandResult.Command.Name + " ";
    tela.Info($"Ajuda: Revisor {comando}--help  ·  Modo guiado: Revisor");
    return 1;
}

return await analisado.InvokeAsync();

/// <summary>Mensagens de erro de linha de comando do System.CommandLine, em português.</summary>
static class ErrosDeLinhaDeComando
{
    private static readonly (System.Text.RegularExpressions.Regex Padrao, string Mensagem)[] Regras =
    [
        (new(@"^Option '(?<a>[^']+)' is required\.$"), "Falta a opção obrigatória {a}."),
        (new(@"^Required command was not provided\.$"), "Informe um comando: review, dump-checklist ou dump-evidencia."),
        (new(@"^'(?<a>[^']+)' was not matched\..*", System.Text.RegularExpressions.RegexOptions.Singleline), "Não reconheci '{a}'."),
        (new(@"^Unrecognized command or argument '(?<a>[^']+)'\.$"), "Não reconheci '{a}'."),
        (new(@"^Required argument missing for option: '(?<a>[^']+)'\.$"), "A opção {a} precisa de um valor."),
        (new(@"^Cannot parse argument '(?<v>[^']*)' for option '(?<a>[^']+)'.*", System.Text.RegularExpressions.RegexOptions.Singleline), "Valor inválido '{v}' para {a}.")
    ];

    public static string Traduzir(string mensagem)
    {
        foreach (var (padrao, texto) in Regras)
        {
            var m = padrao.Match(mensagem.Trim());
            if (!m.Success) continue;
            var resultado = texto;
            foreach (var nome in new[] { "a", "v" })
                if (m.Groups[nome].Success) resultado = resultado.Replace("{" + nome + "}", m.Groups[nome].Value);
            return resultado;
        }
        return mensagem;
    }
}

/// <summary>Ajuda padrão do System.CommandLine seguida dos exemplos de uso.</summary>
sealed class AjudaComExemplos(SynchronousCommandLineAction padrao) : SynchronousCommandLineAction
{
    // A tradução pt-BR do System.CommandLine cobre "Uso", "Opções" e "Comandos", mas deixa
    // estas em inglês no meio da ajuda.
    private static readonly (string De, string Para)[] Traducoes =
    [
        ("Description:", "Descrição:"),
        ("[command] [options]", "[comando] [opções]"),
        ("[options]", "[opções]"),
        ("Show help and usage information", "Mostra esta ajuda"),
        // O satélite pt-BR usa infinitivo ("Mostrar"); o resto da ajuda está no presente.
        ("Mostrar informações de versão", "Mostra a versão do programa"),
        ("(REQUIRED)", "(exigido) "),   // mesmo comprimento: mantém a coluna da descrição alinhada
        ("[default: ", "[padrão: ")
    ];

    public override int Invoke(ParseResult parseResult)
    {
        var configuracao = parseResult.InvocationConfiguration;
        var o = configuracao.Output;
        var capturado = new StringWriter();
        configuracao.Output = capturado;
        int codigo;
        try { codigo = padrao.Invoke(parseResult); }
        finally { configuracao.Output = o; }

        var texto = capturado.ToString();
        foreach (var (de, para) in Traducoes) texto = texto.Replace(de, para);
        o.Write(texto);

        // Os comandos de diagnóstico ganham só o próprio exemplo: os de revisão na ajuda do
        // dump-evidencia pareciam dizer que era ali que se revisava.
        switch (parseResult.CommandResult.Command.Name)
        {
            case "dump-checklist":
                o.WriteLine("Exemplo:");
                o.WriteLine("  Revisor dump-checklist");
                o.WriteLine();
                return codigo;
            case "dump-evidencia":
                o.WriteLine("Exemplo:");
                o.WriteLine("  Revisor dump-evidencia --doc \"RN-816.docx\" > evidencia.txt");
                o.WriteLine();
                return codigo;
        }

        o.WriteLine("Exemplos:");
        o.WriteLine();
        o.WriteLine("  Modo guiado (pergunta o documento, o tipo de revisão e o cliente):");
        o.WriteLine("    Revisor");
        o.WriteLine("    (ou dê duplo clique no Revisor.exe / arraste um .docx sobre ele)");
        o.WriteLine();
        o.WriteLine("  Revisão direta (usa o único profile disponível e salva ao lado do original):");
        o.WriteLine("    Revisor review --doc \"C:\\docs\\RN-816.docx\"");
        o.WriteLine();
        o.WriteLine("  Escolhendo cliente e destino:");
        o.WriteLine("    Revisor review --doc \"RN-816.docx\" --profile profiles\\mrn.json --out \"revisado.docx\"");
        o.WriteLine();
        o.WriteLine("  Em pipeline, sem IA (sai com código 2 se houver não conformidade):");
        o.WriteLine("    Revisor review --doc \"RN-816.docx\" --no-llm");
        o.WriteLine();
        o.WriteLine("  Vendo também o que passou, o que não se aplica e por quê:");
        o.WriteLine("    Revisor review --doc \"RN-816.docx\" --verbose");
        o.WriteLine();
        o.WriteLine("Códigos de saída: 0 conforme · 2 não conformidades · 1 erro de execução.");
        o.WriteLine();
        return codigo;
    }
}
