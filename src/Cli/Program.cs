using System.CommandLine;
using Microsoft.Extensions.Configuration;
using WordComplianceValidator.Application.Services;
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
    Model = config["OpenAI:Model"] ?? "gpt-5.4-mini"
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

var reviewCmd = new Command("review", "Revisa um documento .docx contra o checklist CL-001.")
{
    docOpt, checklistOpt, profileOpt, outOpt, noLlmOpt, verboseOpt
};

reviewCmd.SetHandler(async (FileInfo doc, FileInfo checklist, FileInfo profile, FileInfo outFile, bool noLlm, bool verbose) =>
{
    var extractor = new DocxStructureExtractor();
    var checklistRepo = new ExcelChecklistRepository();
    var profileRepo = new JsonClientProfileRepository();
    var inserter = new CommentInserter();

    var checks = new List<IRuleCheck>
    {
        new LogomarcasNoHeaderCheck(),
        new IniciaisDistintasCheck(),
        new CodificacaoTecnicaCheck()
    };

    if (!noLlm && !string.IsNullOrWhiteSpace(openAi.ApiKey))
    {
        var semantic = new OpenAiSemanticChecker(openAi);
        checks.Add(new FolhaRostoVsCaracteristicasCheck(semantic));
        Console.WriteLine($"[info] LLM habilitado (modelo {openAi.Model}).");
    }
    else
    {
        Console.WriteLine("[info] LLM desabilitado (sem OPENAI_API_KEY ou --no-llm). Checks semânticos serão pulados.");
    }

    var engine = new ChecklistEngine(checks);
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
    Environment.ExitCode = report.Violations.Any(v => v.Severity == Severity.Error) ? 2 : 0;
}, docOpt, checklistOpt, profileOpt, outOpt, noLlmOpt, verboseOpt);

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
