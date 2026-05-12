using FluentAssertions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Infrastructure.Excel;
using Xunit;

namespace WordComplianceValidator.Infrastructure.Tests.Excel;

public class ExcelChecklistRepositoryTests
{
    private static string ChecklistPath()
    {
        // Walk up from test bin to repo root.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "templates", "checklists", "CL-001-CL00100.xlsx");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        throw new FileNotFoundException("Checklist xlsx não encontrado a partir de " + AppContext.BaseDirectory);
    }

    [Fact]
    public async Task Loads_all_86_entries_with_expected_distribution()
    {
        var repo = new ExcelChecklistRepository();
        var entries = await repo.LoadAsync(ChecklistPath());

        entries.Should().HaveCount(86);
        entries.Count(e => e.IaAutomatizavel).Should().Be(21);
        entries.Select(e => e.Ref.Ps).Distinct().Should().Contain(new[] { "PS-002", "PS-005", "PS-018", "PS-024" });
        entries.Should().Contain(e => e.Ref.Ps == "PS-002" && e.Ref.Item == "4.1"
                                   && e.Ref.Padrao == ChecklistPadrao.Cliente
                                   && e.IaAutomatizavel);
    }
}
