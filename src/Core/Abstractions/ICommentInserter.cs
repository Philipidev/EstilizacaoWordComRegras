using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Core.Abstractions;

public interface ICommentInserter
{
    /// <summary>
    /// Grava a cópia comentada e devolve quantos comentários foram inseridos — menos que o
    /// número de violações quando o mesmo achado viola vários itens e vira um comentário só.
    /// </summary>
    int InsertComments(
        string sourcePath,
        string destinationPath,
        IReadOnlyList<Violation> violations,
        string author = "Compliance Validator",
        string initials = "CV");
}
