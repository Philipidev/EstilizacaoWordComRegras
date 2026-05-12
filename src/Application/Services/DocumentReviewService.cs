using WordComplianceValidator.Core.Abstractions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Profile;

namespace WordComplianceValidator.Application.Services;

public sealed record DocumentReviewReport(
    string DocumentPath,
    string Cliente,
    int Passed,
    int Failed,
    int Skipped,
    int Errored,
    IReadOnlyList<RuleCheckResult> Results,
    IReadOnlyList<Violation> Violations);

public sealed class DocumentReviewService
{
    private readonly IDocxStructureExtractor _extractor;
    private readonly IChecklistRepository _checklistRepo;
    private readonly IClientProfileRepository _profileRepo;
    private readonly ChecklistEngine _engine;
    private readonly ICommentInserter _commentInserter;

    public DocumentReviewService(
        IDocxStructureExtractor extractor,
        IChecklistRepository checklistRepo,
        IClientProfileRepository profileRepo,
        ChecklistEngine engine,
        ICommentInserter commentInserter)
    {
        _extractor = extractor;
        _checklistRepo = checklistRepo;
        _profileRepo = profileRepo;
        _engine = engine;
        _commentInserter = commentInserter;
    }

    public async Task<DocumentReviewReport> ReviewAsync(
        string documentPath,
        string checklistPath,
        string profilePath,
        string outputDocxPath,
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileRepo.LoadAsync(profilePath, cancellationToken);
        var catalog = await _checklistRepo.LoadAsync(checklistPath, cancellationToken);
        var structure = _extractor.ExtractFromFile(documentPath);
        var ctx = new DocumentContext(documentPath, structure, profile);

        var results = await _engine.RunAsync(ctx, catalog, cancellationToken);
        var violations = results.SelectMany(r => r.Violations).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(outputDocxPath)!);
        if (violations.Count > 0)
            _commentInserter.InsertComments(documentPath, outputDocxPath, violations);
        else
            File.Copy(documentPath, outputDocxPath, overwrite: true);

        return new DocumentReviewReport(
            DocumentPath: documentPath,
            Cliente: profile.Cliente,
            Passed: results.Count(r => r.Status == CheckStatus.Passed),
            Failed: results.Count(r => r.Status == CheckStatus.Failed),
            Skipped: results.Count(r => r.Status == CheckStatus.Skipped),
            Errored: results.Count(r => r.Status == CheckStatus.Error),
            Results: results,
            Violations: violations);
    }
}
