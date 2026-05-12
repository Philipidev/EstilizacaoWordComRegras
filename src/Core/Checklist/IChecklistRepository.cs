namespace WordComplianceValidator.Core.Checklist;

public interface IChecklistRepository
{
    Task<IReadOnlyList<ChecklistEntry>> LoadAsync(string source, CancellationToken cancellationToken = default);
}
