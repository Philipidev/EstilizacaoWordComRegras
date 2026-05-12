using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Rules;

namespace WordComplianceValidator.Application.Services;

public sealed class RuleGenerationService
{
    private readonly IDocxStructureExtractor _extractor;
    private readonly IRuleParser _parser;
    private readonly IRuleSetRepository _repository;

    public RuleGenerationService(
        IDocxStructureExtractor extractor,
        IRuleParser parser,
        IRuleSetRepository repository)
    {
        _extractor = extractor;
        _parser = parser;
        _repository = repository;
    }

    public async Task<(RuleSet RuleSet, string SavedPath)> GenerateAsync(
        string templatePath,
        string rulesText,
        string clientName,
        bool majorBump,
        CancellationToken cancellationToken = default)
    {
        var structure = _extractor.ExtractFromFile(templatePath);
        var version = await _repository.NextVersionAsync(clientName, majorBump, cancellationToken);
        var ruleSet = await _parser.ParseAsync(clientName, version, rulesText, structure, cancellationToken);

        // Force trustworthy cliente/versao on what came from the model.
        ruleSet = ruleSet with { Cliente = clientName, VersaoPadrao = version };

        var savedPath = await _repository.SaveAsync(ruleSet, cancellationToken);
        return (ruleSet, savedPath);
    }
}
