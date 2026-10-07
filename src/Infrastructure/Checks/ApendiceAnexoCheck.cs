using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-002 4.3.8 / 4.3.9 — Apêndices e anexos devem estar referenciados no quadro
/// "Características do Documento" do documento principal.
/// <para>
/// Quando o documento não possui apêndice nem anexo a regra não se aplica e o resultado é
/// Skipped — é o caso da maioria dos relatórios.
/// </para>
/// </summary>
public sealed class ApendiceAnexoCheck : IRuleCheck
{
    // Palavra-chave sem distinção de caixa; identificador em maiúsculas (romano, letra ou
    // número). Antes só letra única ou número: "ANEXO II", "ANEXO IV"… viravam "Anexo I" e
    // "Anexo V" ou sumiam, e com IgnoreCase no identificador "anexo a planilha" virava "Anexo A".
    private static readonly Regex TituloApendiceAnexo = new(
        @"^\s*(?i:(ap[êe]ndice|anexo|adendo))\s+([IVXLC]{1,6}|[A-Z]|\d{1,3})(?=$|[\s\-–—:.)])",
        RegexOptions.Compiled);

    public ApendiceAnexoCheck(ChecklistPadrao padrao = ChecklistPadrao.Cliente, string item = "4.3.8")
    {
        Ref = new ChecklistRef("PS-002", item, padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var titulosAlias = DocumentoTexto.Lista(ctx.Profile.Get("quadroCaracteristicas.aliases"),
            "Características do Documento|Quadro de Características|Características");
        var quadro = QuadroCaracteristicas.Find(ctx.Structure, titulosAlias);

        // As linhas do próprio quadro não contam como apêndice/anexo do documento: lidas como
        // títulos, elas se referenciavam a si mesmas e qualquer anexo listado "existia".
        var titulos = ctx.Structure.Paragraphs
            .Where(p => quadro is null || p.TableIndex != quadro.Index)
            .Where(p => !string.IsNullOrWhiteSpace(p.Text) && TituloApendiceAnexo.IsMatch(p.Text))
            .GroupBy(p => Rotulo(p.Text))
            .Select(g => (Rotulo: g.Key, Paragrafo: g.First()))
            .ToList();

        if (titulos.Count == 0)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Documento não possui apêndice ou anexo."));
        }

        if (quadro is null)
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Quadro 'Características do Documento' não localizado."));
        }

        var quadroNorm = DocumentoTexto.Normalizar(
            string.Join(" ", quadro.Cells.Select(c => c.Text)));

        // Fronteira de palavra: "anexo i" não pode casar dentro de "anexo ii".
        var naoReferenciados = titulos
            .Where(t => !Regex.IsMatch(quadroNorm, $@"\b{Regex.Escape(DocumentoTexto.Normalizar(t.Rotulo))}\b"))
            .ToList();

        var violations = naoReferenciados.Select(t => new Violation(
            Ref.ToString(), Severity.Error,
            $"'{t.Rotulo}' não está referenciado no quadro 'Características do Documento'.",
            new ViolationLocation(t.Paragrafo.ParagraphId, null, t.Paragrafo.SectionIndex,
                                  "Apêndice/Anexo"))).ToList();

        var status = violations.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations,
            Note: $"{titulos.Count} apêndice(s)/anexo(s) localizado(s): " +
                  string.Join(", ", titulos.Select(t => t.Rotulo))));
    }

    /// <summary>Extrai "Apêndice A" / "Anexo 2" do início do parágrafo, sem o restante do título.</summary>
    private static string Rotulo(string texto)
    {
        var m = TituloApendiceAnexo.Match(texto);
        return m.Success
            ? $"{Capitalizar(m.Groups[1].Value)} {m.Groups[2].Value.ToUpperInvariant()}"
            : texto.Trim();
    }

    private static string Capitalizar(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
}
