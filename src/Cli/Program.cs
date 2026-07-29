using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using Microsoft.Extensions.Configuration;
using WordComplianceValidator.Application.Services;
using WordComplianceValidator.Cli.Terminal;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Infrastructure.Checks;
using WordComplianceValidator.Infrastructure.Excel;
using WordComplianceValidator.Infrastructure.OpenAI;
using WordComplianceValidator.Infrastructure.OpenXml;
using WordComplianceValidator.Infrastructure.Profile;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var openAi = new OpenAiSettings
{
    ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? config["OpenAI:ApiKey"] ?? string.Empty,
    Model = config["OpenAI:Model"] ?? "gpt-5.6-sol"
};

var docOpt = new Option<FileInfo>("--doc", "Documento .docx a revisar.") { IsRequired = true };
docOpt.AddAlias("-d");
var checklistOpt = new Option<FileInfo>(
    "--checklist",
    description: "Caminho do checklist .xlsx (default: procurado a partir do executável).",
    // Antes o default era montado sobre Directory.GetCurrentDirectory(), o que só acerta quando
    // o programa roda da raiz do repositório. O localizador parte do executável e sobe a árvore,
    // então também funciona num binário publicado, chamado de qualquer pasta.
    getDefaultValue: () => new FileInfo(
        LocalizadorDeRecursos.Checklist()
        ?? Path.Combine(AppContext.BaseDirectory, "templates", "checklists", "CL-001-CL00100.xlsx")));
var profileOpt = new Option<FileInfo?>(
    "--profile",
    "Profile JSON do cliente. Opcional quando existe um único profile na pasta 'profiles'.");
var outOpt = new Option<FileInfo?>(
    "--out",
    "Arquivo .docx de saída. Default: <documento>-REVISADO.docx, na pasta do documento.");
var noLlmOpt = new Option<bool>("--no-llm", () => false, "Desabilita checks que usam LLM.");
var verboseOpt = new Option<bool>("--verbose", () => false, "Inclui itens pulados no output.");
var onlyIaSimOpt = new Option<bool>(
    "--only-ia-sim", () => false,
    "Executa apenas itens marcados IA=Sim no checklist (comportamento legado).");

var reviewCmd = new Command("review", "Revisa um documento .docx contra o checklist CL-001.")
{
    docOpt, checklistOpt, profileOpt, outOpt, noLlmOpt, verboseOpt, onlyIaSimOpt
};

// O handler recebe o InvocationContext em vez das opções tipadas porque precisa definir
// ctx.ExitCode: o top-level program termina com `return await root.InvokeAsync(args)`, e esse
// retorno sobrescreve qualquer Environment.ExitCode — que era como o código de saída 2 se
// perdia silenciosamente, fazendo um gate de CI aprovar documentos reprovados.
reviewCmd.SetHandler(async (InvocationContext ctx) =>
{
    var parsed = ctx.ParseResult;
    var doc = parsed.GetValueForOption(docOpt)!;
    var checklist = parsed.GetValueForOption(checklistOpt)!;
    var profile = parsed.GetValueForOption(profileOpt)!;
    var outFile = parsed.GetValueForOption(outOpt)!;
    var noLlm = parsed.GetValueForOption(noLlmOpt);
    var verbose = parsed.GetValueForOption(verboseOpt);
    var onlyIaSim = parsed.GetValueForOption(onlyIaSimOpt);

    // Validação antes de qualquer trabalho: cada mensagem diz qual opção está errada e com
    // que caminho ela ficou. Sem isso, um caminho digitado errado virava
    // "Unhandled exception: FileNotFoundException" com um stack trace no meio da tela.
    if (!doc.Exists)
    {
        Apresentacao.Erro("Documento não encontrado", doc.FullName, "Confira o caminho em --doc.");
        ctx.ExitCode = 1;
        return;
    }

    if (!doc.Extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
    {
        Apresentacao.Erro("O documento precisa ser .docx", doc.FullName,
            $"Recebi um arquivo '{doc.Extension}'. Formatos .doc antigos não são suportados.");
        ctx.ExitCode = 1;
        return;
    }

    if (!checklist.Exists)
    {
        Apresentacao.Erro("Checklist não encontrado", checklist.FullName,
            "Passe o caminho em --checklist ou deixe o .xlsx em 'templates/checklists'.");
        ctx.ExitCode = 1;
        return;
    }

    var profilePath = ResolverProfile(profile);
    if (profilePath is null) { ctx.ExitCode = 1; return; }

    var saidaPath = outFile?.FullName ?? LocalizadorDeRecursos.SaidaPadrao(doc.FullName);

    var svc = MontarServico(onlyIaSim, noLlm, logar: false);
    var perfilCarregado = await new JsonClientProfileRepository().LoadAsync(profilePath);

    Apresentacao.Cabecalho(doc.FullName, perfilCarregado.Cliente, checklist.FullName, saidaPath);

    if (noLlm || string.IsNullOrWhiteSpace(openAi.ApiKey))
        Apresentacao.Aviso("Sem LLM: ortografia e regras semânticas serão puladas.");
    else
        Apresentacao.Info($"LLM habilitado (modelo {openAi.Model}).");

    DocumentReviewReport report;
    var progresso = Apresentacao.BarraDeProgresso(out var escopoProgresso);
    try
    {
        report = await svc.ReviewAsync(
            doc.FullName, checklist.FullName, profilePath, saidaPath, CancellationToken.None, progresso);
    }
    catch (Exception ex)
    {
        escopoProgresso?.Dispose();
        Apresentacao.Erro("A revisão não foi concluída", null, ex.Message);
        ctx.ExitCode = 1;
        return;
    }
    finally
    {
        escopoProgresso?.Dispose();
    }

    Apresentacao.Resumo(report);
    Apresentacao.Detalhes(report, verbose);
    Apresentacao.Saida(saidaPath, report.Violations.Count);

    ctx.ExitCode = report.Violations.Any(v => v.Severity == Severity.Error) ? 2 : 0;
});

// Profile explícito vence; sem ele, só resolve sozinho quando a escolha é inequívoca. Havendo
// vários, listar os nomes é mais útil do que exigir que a pessoa vá procurar a pasta.
static string? ResolverProfile(FileInfo? informado)
{
    if (informado is not null)
    {
        if (informado.Exists) return informado.FullName;
        Apresentacao.Erro("Profile não encontrado", informado.FullName, "Confira o caminho em --profile.");
        return null;
    }

    var disponiveis = LocalizadorDeRecursos.Profiles();

    if (disponiveis.Count == 1) return disponiveis[0];

    if (disponiveis.Count == 0)
    {
        Apresentacao.Erro("Nenhum profile de cliente encontrado",
            LocalizadorDeRecursos.PastaDeProfiles() ?? "(pasta 'profiles' não localizada)",
            "Informe um com --profile ou coloque um .json na pasta 'profiles'.");
        return null;
    }

    Apresentacao.Erro("Existe mais de um profile; escolha um com --profile", null,
        "Disponíveis: " + string.Join(", ", disponiveis.Select(Path.GetFileName)));
    return null;
}

// ---- checklist dump (debug) ----
var dumpCmd = new Command("dump-checklist", "Lista entradas do checklist .xlsx.")
{
    checklistOpt
};
dumpCmd.SetHandler(async (FileInfo checklist) =>
{
    var repo = new ExcelChecklistRepository();
    var entries = await repo.LoadAsync(checklist.FullName);
    Console.WriteLine($"Total: {entries.Count} entradas");
    foreach (var e in entries)
        Console.WriteLine($"  {e.Ref,-40} IA={(e.IaAutomatizavel ? "Sim" : "Não")}  {e.Titulo}");
}, checklistOpt);

// Montagem única do serviço, compartilhada pelo modo com flags e pelo fluxo guiado — sem isso
// as duas portas de entrada divergiriam em silêncio na configuração do motor.
DocumentReviewService MontarServico(bool onlyIaSim, bool noLlm, bool logar)
{
    var usarLlm = !noLlm && !string.IsNullOrWhiteSpace(openAi.ApiKey);
    var semantic = usarLlm ? new OpenAiSemanticChecker(openAi) : null;

    Func<ChecklistEntry, DocumentContext, IRuleCheck?>? semanticFallback = null;
    if (semantic is not null)
    {
        semanticFallback = new SemanticCheckFactory(semantic).Create;
        if (logar) Console.WriteLine($"[info] LLM habilitado (modelo padrão {openAi.Model}).");
    }
    else if (logar)
    {
        Console.WriteLine("[info] LLM desabilitado (sem OPENAI_API_KEY ou --no-llm). Checks semânticos serão pulados.");
    }

    var engine = new ChecklistEngine(
        CheckRegistry.Deterministicos(semantic), honrarColunaIa: onlyIaSim, fallback: semanticFallback);

    return new DocumentReviewService(
        new DocxStructureExtractor(),
        new ExcelChecklistRepository(),
        new JsonClientProfileRepository(),
        engine,
        new CommentInserter());
}

var root = new RootCommand(
    "Revisa documentos técnicos .docx contra o checklist CL-001 e devolve uma cópia comentada.")
{
    reviewCmd, dumpCmd
};

// O help gerado lista as opções, mas não mostra como o comando se parece de verdade. Os
// exemplos cobrem os três usos reais: o mínimo, o de pipeline e o de diagnóstico.
var parser = new CommandLineBuilder(root)
    .UseDefaults()
    .UseHelp(ctx =>
    {
        ctx.HelpBuilder.CustomizeLayout(_ =>
            HelpBuilder.Default.GetLayout().Append(_ =>
            {
                Console.WriteLine("Exemplos:");
                Console.WriteLine();
                Console.WriteLine("  Revisão simples (usa o único profile disponível e salva ao lado do original):");
                Console.WriteLine("    Revisor review --doc \"C:\\docs\\RN-816.docx\"");
                Console.WriteLine();
                Console.WriteLine("  Escolhendo cliente e destino:");
                Console.WriteLine("    Revisor review --doc \"RN-816.docx\" --profile profiles\\mrn.json --out \"revisado.docx\"");
                Console.WriteLine();
                Console.WriteLine("  Em pipeline, sem LLM (sai com código 2 se houver não conformidade):");
                Console.WriteLine("    Revisor review --doc \"RN-816.docx\" --no-llm");
                Console.WriteLine();
                Console.WriteLine("  Vendo também o que foi pulado e por quê:");
                Console.WriteLine("    Revisor review --doc \"RN-816.docx\" --verbose");
                Console.WriteLine();
                Console.WriteLine("Códigos de saída: 0 conforme · 2 não conformidades · 1 erro de execução.");
                Console.WriteLine();
            }));
    })
    .Build();

return await parser.InvokeAsync(args);
