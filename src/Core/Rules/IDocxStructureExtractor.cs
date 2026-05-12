using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Core.Rules;

public interface IDocxStructureExtractor
{
    DocumentStructure Extract(Stream docxStream);
    DocumentStructure ExtractFromFile(string path);
}
