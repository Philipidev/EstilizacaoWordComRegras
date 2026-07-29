using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.Configuration;
using WordComplianceValidator.Application.Services;
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
var checklistOpt = new Option<FileInfo>(
    "--checklist",
    description: "Caminho do checklist .xlsx (default: templates/checklists/CL-001-CL00100.xlsx).",
    getDefaultValue: () => new FileInfo(Path.Combine(Directory.GetCurrentDirectory(), "templates", "checklists", "CL-001-CL00100.xlsx")));
var profileOpt = new Option<FileInfo>("--profile", "Caminho do JSON de profile do cliente.") { IsRequired = true };
var outOpt = new Option<FileInfo>("--out", "Caminho do .docx comentado de saída.") { IsRequired = true };
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

    var extractor = new DocxStructureExtractor();
    var checklistRepo = new ExcelChecklistRepository();
    var profileRepo = new JsonClientProfileRepository();
    var inserter = new CommentInserter();

    var usarLlm = !noLlm && !string.IsNullOrWhiteSpace(openAi.ApiKey);
    var semantic = usarLlm ? new OpenAiSemanticChecker(openAi) : null;

    var checks = CheckRegistry.Deterministicos(semantic);

    // Fábrica consultada pelo engine para entradas do checklist sem check dedicado.
    Func<ChecklistEntry, DocumentContext, IRuleCheck?>? semanticFallback = null;

    if (semantic is not null)
    {
        semanticFallback = new SemanticCheckFactory(semantic).Create;
        Console.WriteLine($"[info] LLM habilitado (modelo padrão {openAi.Model}).");
    }
    else
    {
        Console.WriteLine("[info] LLM desabilitado (sem OPENAI_API_KEY ou --no-llm). Checks semânticos serão pulados.");
    }

    var engine = new ChecklistEngine(checks, honrarColunaIa: onlyIaSim, fallback: semanticFallback);
    var svc = new DocumentReviewService(extractor, checklistRepo, profileRepo, engine, inserter);

    var report = await svc.ReviewAsync(doc.FullName, checklist.FullName, profile.FullName, outFile.FullName);

    Console.WriteLine();
    Console.WriteLine($"Cliente: {report.Cliente}");
    Console.WriteLine($"Total de itens do checklist: {report.Results.Count}");
    Console.WriteLine($"  passou:    {report.Passed}");
    Console.WriteLine($"  falhou:    {report.Failed}");
    Console.WriteLine($"  pulado:    {report.Skipped}");
    Console.WriteLine($"  erro:      {report.Errored}");
    Console.WriteLine();

    var toShow = verbose ? report.Results : report.Results.Where(r => r.Status is CheckStatus.Failed or CheckStatus.Error);
    foreach (var r in toShow)
    {
        Console.WriteLine($"  [{r.Status}] {r.Ref}");
        foreach (var v in r.Violations)
            Console.WriteLine($"      → [{v.Severity}] {v.Message}");
        if (!string.IsNullOrEmpty(r.Note))
            Console.WriteLine($"      note: {r.Note}");
    }

    Console.WriteLine();
    Console.WriteLine($"Saída: {outFile.FullName}");
    ctx.ExitCode = report.Violations.Any(v => v.Severity == Severity.Error) ? 2 : 0;
});

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

var root = new RootCommand("Word Compliance Validator CLI") { reviewCmd, dumpCmd };
return await root.InvokeAsync(args);
