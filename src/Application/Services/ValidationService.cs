using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Rules;
using WordComplianceValidator.Core.Validation;

namespace WordComplianceValidator.Application.Services;

public sealed class ValidationService
{
    private readonly IDocxStructureExtractor _extractor;
    private readonly IRuleSetRepository _repository;
    private readonly ICommentInserter _commentInserter;
    private readonly RuleEngine _engine;

    public ValidationService(
        IDocxStructureExtractor extractor,
        IRuleSetRepository repository,
        ICommentInserter commentInserter,
        RuleEngine? engine = null)
    {
        _extractor = extractor;
        _repository = repository;
        _commentInserter = commentInserter;
        _engine = engine ?? new RuleEngine();
    }

    public async Task<IReadOnlyList<Violation>> ValidateAsync(
        string documentPath,
        string rulesPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var ruleSet = await _repository.LoadAsync(rulesPath, cancellationToken);
        var structure = _extractor.ExtractFromFile(documentPath);
        var violations = _engine.Run(ruleSet, structure);

        if (violations.Count > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            _commentInserter.InsertComments(documentPath, outputPath, violations);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.Copy(documentPath, outputPath, overwrite: true);
        }
        return violations;
    }
}
