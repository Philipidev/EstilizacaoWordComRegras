using System.CommandLine;
using Microsoft.Extensions.Configuration;
using WordComplianceValidator.Application.Services;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Infrastructure.OpenAI;
using WordComplianceValidator.Infrastructure.OpenXml;
using WordComplianceValidator.Infrastructure.Persistence;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var openAi = new OpenAiSettings
{
    ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
             ?? config["OpenAI:ApiKey"] ?? string.Empty,
    Model = config["OpenAI:Model"] ?? "gpt-4o-mini"
};

var rulesRoot = config["Storage:RulesRoot"] ?? Path.Combine(Directory.GetCurrentDirectory(), "rules");

// --- generate-rules ---
var templateOpt = new Option<FileInfo>("--template", "Caminho do .docx modelo.") { IsRequired = true };
var rulesFileOpt = new Option<FileInfo>("--rules", "Arquivo texto com as regras descritivas.") { IsRequired = true };
var clientOpt = new Option<string>("--client", "Nome do cliente.") { IsRequired = true };
var majorOpt = new Option<bool>("--major", description: "Bump major em vez de minor.", getDefaultValue: () => false);

var generateCmd = new Command("generate-rules", "Gera/atualiza padrão JSON do cliente a partir de modelo + regras textuais.")
{
    templateOpt, rulesFileOpt, clientOpt, majorOpt
};

generateCmd.SetHandler(async (FileInfo template, FileInfo rules, string client, bool major) =>
{
    var extractor = new DocxStructureExtractor();
    var parser = new OpenAiRuleParser(openAi);
    var repo = new FileRuleSetRepository(rulesRoot);
    var svc = new RuleGenerationService(extractor, parser, repo);

    var rulesText = await File.ReadAllTextAsync(rules.FullName);
    var (ruleSet, savedPath) = await svc.GenerateAsync(template.FullName, rulesText, client, major);
    Console.WriteLine($"Padrão gerado: {savedPath}");
    Console.WriteLine($"  cliente:        {ruleSet.Cliente}");
    Console.WriteLine($"  versão:         {ruleSet.VersaoPadrao}");
    Console.WriteLine($"  estilos:        {ruleSet.Styles.Count}");
    Console.WriteLine($"  headers/footers:{ruleSet.Headers.Count}/{ruleSet.Footers.Count}");
    Console.WriteLine($"  regras:         {ruleSet.Rules.Count}");
}, templateOpt, rulesFileOpt, clientOpt, majorOpt);

// --- validate ---
var docOpt = new Option<FileInfo>("--doc", "Documento .docx a validar.") { IsRequired = true };
var rulesJsonOpt = new Option<FileInfo>("--rules", "Arquivo JSON do padrão (gerado).") { IsRequired = true };
var outOpt = new Option<FileInfo>("--out", "Caminho do .docx comentado de saída.") { IsRequired = true };

var validateCmd = new Command("validate", "Valida um documento contra um padrão JSON, gerando .docx com comentários.")
{
    docOpt, rulesJsonOpt, outOpt
};

validateCmd.SetHandler(async (FileInfo doc, FileInfo rulesJson, FileInfo outFile) =>
{
    var extractor = new DocxStructureExtractor();
    var repo = new FileRuleSetRepository(rulesRoot);
    var inserter = new CommentInserter();
    var svc = new ValidationService(extractor, repo, inserter);

    var violations = await svc.ValidateAsync(doc.FullName, rulesJson.FullName, outFile.FullName);
    Console.WriteLine($"Violações: {violations.Count}");
    foreach (var v in violations)
    {
        Console.WriteLine($"  [{v.Severity}] [{v.RuleId}] {v.Message}");
    }
    Console.WriteLine($"Saída: {outFile.FullName}");
    Environment.ExitCode = violations.Any(v => v.Severity == Severity.Error) ? 2 : 0;
}, docOpt, rulesJsonOpt, outOpt);

var root = new RootCommand("Word Compliance Validator CLI")
{
    generateCmd,
    validateCmd
};

return await root.InvokeAsync(args);
