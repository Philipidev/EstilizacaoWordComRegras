using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Core.Profile;

namespace WordComplianceValidator.Core.Checks;

public sealed class DocumentContext
{
    public string SourcePath { get; }
    public DocumentStructure Structure { get; }
    public ClientProfile Profile { get; }

    public DocumentContext(string sourcePath, DocumentStructure structure, ClientProfile profile)
    {
        SourcePath = sourcePath;
        Structure = structure;
        Profile = profile;
    }
}
