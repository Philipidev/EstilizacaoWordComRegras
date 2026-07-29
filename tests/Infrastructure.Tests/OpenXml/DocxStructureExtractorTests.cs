using FluentAssertions;
using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Infrastructure.OpenXml;

namespace WordComplianceValidator.Infrastructure.Tests.OpenXml;

public class DocxStructureExtractorTests
{
    private static DocumentStructure Extract(string fileName) =>
        new DocxStructureExtractor().ExtractFromFile(RepoFile("templates", fileName));

    private static readonly string[] AlinhamentosValidos =
        ["left", "center", "right", "both", "distribute", "start", "end"];

    /// <summary>
    /// Regressão do bug de enum: a partir do OpenXML SDK 3.x os atributos enumerados são
    /// structs e <c>ToString()</c> devolve "JustificationValues { }" em vez do valor. Se
    /// alguém voltar a ler esses campos via ToString(), este teste quebra.
    /// </summary>
    [Theory]
    [InlineData("RN799RL6496600.docx")]
    [InlineData("RN-816-RL-67456-00.docx")]
    public void Alignment_is_the_raw_ooxml_value_never_the_enum_type_name(string fileName)
    {
        var s = Extract(fileName);

        var alinhamentos = s.Paragraphs
            .Select(p => p.Alignment)
            .Concat(s.Headers.SelectMany(h => h.ParagraphAlignments))
            .Where(a => a is not null)
            .Distinct()
            .ToList();

        alinhamentos.Should().NotBeEmpty("o documento tem parágrafos com alinhamento explícito");
        alinhamentos.Should().OnlyContain(a => AlinhamentosValidos.Contains(a!));
    }

    [Theory]
    [InlineData("RN799RL6496600.docx")]
    [InlineData("RN-816-RL-67456-00.docx")]
    public void Body_paragraphs_resolve_effective_font_through_style_chain(string fileName)
    {
        var s = Extract(fileName);

        var corpo = s.Paragraphs
            .Where(p => !p.IsInTable && !string.IsNullOrWhiteSpace(p.Text))
            .ToList();

        corpo.Should().NotBeEmpty();
        // Ambos os documentos de referência usam Times New Roman no corpo.
        corpo.Should().Contain(p => p.EffectiveFont == "Times New Roman");
        corpo.Where(p => p.EffectiveFontSize is not null)
             .Should().Contain(p => p.EffectiveFontSize == 12);
    }

    [Theory]
    [InlineData("RN799RL6496600.docx")]
    [InlineData("RN-816-RL-67456-00.docx")]
    public void Headings_expose_outline_levels_even_with_custom_style_names(string fileName)
    {
        var s = Extract(fileName);

        // Os estilos são customizados do cliente (Ttulo1MRN, PDA-T1, Estilo1) — o nível tem
        // de vir da cadeia basedOn / outlineLvl, não do nome do estilo.
        var headings = s.Paragraphs
            .Where(p => p.OutlineLevel is not null && !string.IsNullOrWhiteSpace(p.Text))
            .ToList();

        headings.Should().HaveCountGreaterThan(10);
        headings.Should().Contain(p => p.OutlineLevel == 0, "há títulos de nível 1");
        headings.Should().Contain(p => p.OutlineLevel == 1, "há títulos de nível 2");
        headings.Should().OnlyContain(p => p.OutlineLevel >= 0 && p.OutlineLevel <= 8);
    }

    [Theory]
    [InlineData("RN799RL6496600.docx")]
    [InlineData("RN-816-RL-67456-00.docx")]
    public void Paragraphs_are_bound_to_sections_and_tables(string fileName)
    {
        var s = Extract(fileName);

        s.Paragraphs.Should().Contain(p => p.IsInTable, "o quadro Características é uma tabela");
        s.Paragraphs.Should().Contain(p => !p.IsInTable);
        s.Paragraphs.Select(p => p.SectionIndex).Distinct().Should().HaveCountGreaterThan(1,
            "os documentos têm capa e corpo em seções distintas");
        s.Paragraphs.Should().OnlyContain(p => p.SectionIndex >= 0);
    }

    [Theory]
    [InlineData("RN799RL6496600.docx")]
    [InlineData("RN-816-RL-67456-00.docx")]
    public void Headers_are_bound_to_the_section_that_references_them(string fileName)
    {
        var s = Extract(fileName);

        s.Headers.Should().NotBeEmpty();
        s.Headers.Should().Contain(h => h.SectionIndex != null);
        s.Headers.Where(h => h.SectionIndex is not null)
                 .Should().OnlyContain(h => h.SectionIndex >= 0 && h.SectionIndex < s.Sections.Count);
        s.Headers.Should().OnlyContain(h => h.Kind == "default" || h.Kind == "first" || h.Kind == "even");
    }

    [Fact]
    public void Body_images_are_counted_per_paragraph()
    {
        var s = Extract("RN799RL6496600.docx");
        s.Paragraphs.Where(p => p.ImageCount > 0).Should().NotBeEmpty(
            "o relatório tem figuras no corpo — antes só contávamos imagens de cabeçalho");
    }

    [Fact]
    public void Package_properties_are_exposed()
    {
        var s = Extract("RN799RL6496600.docx");
        s.Properties.Should().NotBeNull();
        s.Properties!.Creator.Should().NotBeNullOrWhiteSpace();
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(new[] { dir }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        throw new FileNotFoundException("Arquivo do repositório não encontrado.", Path.Combine(parts));
    }
}
