using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-018 4.3.2 (letra a) Cliente — Localização da Codificação Técnica.
/// Verifica se a codificação (padrão PdA) está localizada e consistente nos três
/// pontos: nome do arquivo eletrônico, folha de rosto/capa e quadro Características.
/// </summary>
public sealed class LocalizacaoCodificacaoCheck : IRuleCheck
{

    public LocalizacaoCodificacaoCheck(ChecklistPadrao padrao = ChecklistPadrao.Cliente)
    {
        Ref = new ChecklistRef("PS-018", "4.3.2 (letra a)", padrao);
    }

    public ChecklistRef Ref { get; }

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var pdaRx = ctx.Profile.Get("codificacao.pdaRegex");
        if (string.IsNullOrWhiteSpace(pdaRx))
        {
            return Task.FromResult(new RuleCheckResult(Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(),
                Note: "Profile sem 'codificacao.pdaRegex'."));
        }

        var titulosAlias = (ctx.Profile.Get("quadroCaracteristicas.aliases")
            ?? "Características do Documento|Quadro de Características|Características")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var table = QuadroCaracteristicas.Find(ctx.Structure, titulosAlias);
        var quadroCodigo = Codificacao.EncontrarEmQualquer(
            table?.Cells.Select(c => c.Text) ?? Array.Empty<string>(), pdaRx)?.Completo;

        // Folha de rosto = capa até o primeiro título. Os cabeçalhos ficavam juntos e eram
        // eles que "achavam" a codificação: a folha de rosto real nem chegava a ser lida.
        var folhaCodigo = Codificacao.EncontrarEmQualquer(
            DocumentoTexto.FolhaDeRosto(ctx.Structure).Select(p => p.Text), pdaRx)?.Completo;

        var stem = string.IsNullOrEmpty(ctx.Structure.FileName)
            ? null : Path.GetFileNameWithoutExtension(ctx.Structure.FileName);

        var violations = new List<Violation>();

        if (string.IsNullOrEmpty(quadroCodigo))
        {
            violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                "Codificação não encontrada no quadro 'Características do Documento'.",
                new ViolationLocation(null, null, null, "Quadro Características")));
        }

        if (string.IsNullOrEmpty(folhaCodigo))
        {
            violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                "Codificação não encontrada na folha de rosto / capa.",
                new ViolationLocation(null, null, null, "Folha de Rosto")));
        }

        if (!string.IsNullOrEmpty(quadroCodigo)
            && !string.IsNullOrEmpty(folhaCodigo)
            && !Codificacao.Compacto(Codificacao.Encontrar(quadroCodigo, pdaRx)!.Base)
                .Equals(Codificacao.Compacto(Codificacao.Encontrar(folhaCodigo, pdaRx)!.Base), StringComparison.Ordinal))
        {
            violations.Add(new Violation(Ref.ToString(), Severity.Error,
                $"Codificação diverge entre folha de rosto ('{folhaCodigo}') e quadro Características ('{quadroCodigo}').",
                new ViolationLocation(null, null, null, "Folha de Rosto × Quadro")));
        }

        if (!string.IsNullOrEmpty(stem) && !string.IsNullOrEmpty(quadroCodigo))
        {
            var stemNorm = Codificacao.Compacto(stem);
            var codeNorm = Codificacao.Compacto(Codificacao.Encontrar(quadroCodigo, pdaRx)!.Base);

            // Direct match (com/sem revisão) ou contenção mútua.
            var direct = stemNorm.Contains(codeNorm, StringComparison.Ordinal)
                         || codeNorm.Contains(stemNorm, StringComparison.Ordinal);

            // Codificação alternativa: o stem do arquivo aparece literalmente em alguma
            // outra célula do quadro Características (típico em projetos com código
            // magnético/Meridian distinto do código PdA).
            var alternative = table is not null && table.Cells.Any(c =>
                !string.IsNullOrWhiteSpace(c.Text)
                && Codificacao.Compacto(c.Text).Contains(stemNorm, StringComparison.Ordinal));

            if (!direct && !alternative)
            {
                violations.Add(new Violation(Ref.ToString(), Severity.Warning,
                    Codificacao.MensagemDeNomeDeArquivo(stem, quadroCodigo, Path.GetExtension(ctx.Structure.FileName)),
                    new ViolationLocation(null, null, null, "Arquivo")));
            }
        }

        var status = violations.Count == 0 ? CheckStatus.Passed
                   : violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : CheckStatus.Skipped;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }
}
