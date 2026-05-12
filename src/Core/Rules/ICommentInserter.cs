using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Core.Rules;

public interface ICommentInserter
{
    void InsertComments(
        string sourcePath,
        string destinationPath,
        IReadOnlyList<Violation> violations,
        string author = "Compliance Validator",
        string initials = "CV");
}
