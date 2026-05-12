using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Core.Abstractions;

public interface IDocxStructureExtractor
{
    DocumentStructure ExtractFromFile(string path);
    DocumentStructure Extract(Stream docxStream, string? fileName = null);
}
