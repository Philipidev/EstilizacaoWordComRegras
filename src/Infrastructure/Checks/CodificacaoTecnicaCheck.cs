using System.Text.RegularExpressions;
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// PS-018 4.3 Cliente — Codificação Técnica: estrutura no padrão PdA e Cliente.
/// Espera profile parameters:
///   codificacao.pdaRegex     (regex obrigatório para padrão PdA, default 15 chars)
///   codificacao.clienteRegex (opcional)
///   codificacao.aliasesRotulo (rótulos do quadro Características que contêm a codificação)
/// </summary>
public sealed class CodificacaoTecnicaCheck : IRuleCheck
{
    public ChecklistRef Ref { get; } = new("PS-018", "4.3", ChecklistPadrao.Cliente);

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken cancellationToken = default)
    {
        var pdaRx = ctx.Profile.Get("codificacao.pdaRegex");
        var clienteRx = ctx.Profile.Get("codificacao.clienteRegex");
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
        var tableValues = table?.Cells
            .Select(c => c.Text.Trim())
            .Where(s => s.Length > 0)
            .ToArray() ?? Array.Empty<string>();

        var violations = new List<Violation>();

        // 1) File name should either follow a configured code pattern or match
        // a document code present in the characteristics table without separators.
        var fileName = ctx.Structure.FileName;
        if (!string.IsNullOrEmpty(fileName))
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            if (!MatchesDocumentPattern(stem, pdaRx)
                && !MatchesDocumentPattern(stem, clienteRx)
                && !MatchesAnyDocumentCode(stem, tableValues))
            {
                // A mensagem diz o nome esperado, não o regex: quem recebe o comentário precisa
                // saber como renomear o arquivo, não ler uma expressão regular.
                var esperado = Codificacao.EncontrarEmQualquer(tableValues, pdaRx)?.Completo;
                violations.Add(new Violation(
                    RuleId: Ref.ToString(),
                    Severity: Severity.Warning,
                    Message: Codificacao.MensagemDeNomeDeArquivo(stem, esperado, Path.GetExtension(fileName)),
                    Location: new ViolationLocation(null, null, null, "Arquivo")));
            }
        }

        // 2) O quadro precisa trazer as duas codificações, cada uma no seu padrão. Antes o código
        // achado pelo regex PdA era testado contra o regex do Cliente — um código PdA correto
        // virava "não corresponde ao padrão Cliente", e um código do Cliente ausente passava.
        if (table is null)
        {
            violations.Add(new Violation(
                RuleId: Ref.ToString(),
                Severity: Severity.Warning,
                Message: "Quadro 'Características do Documento' não localizado para validar codificação.",
                Location: new ViolationLocation(null, null, null, "Quadro Características")));
        }
        else
        {
            var celulas = table.Cells.Select(c => c.Text).ToList();
            if (Codificacao.EncontrarEmQualquer(celulas, pdaRx) is null)
            {
                violations.Add(new Violation(
                    RuleId: Ref.ToString(),
                    Severity: Severity.Warning,
                    Message: $"Nenhuma codificação no padrão PdA ({pdaRx}) localizada no quadro 'Características'.",
                    Location: new ViolationLocation(null, null, null, "Quadro Características")));
            }

            if (!string.IsNullOrWhiteSpace(clienteRx) && Codificacao.EncontrarEmQualquer(celulas, clienteRx) is null)
            {
                violations.Add(new Violation(
                    RuleId: Ref.ToString(),
                    Severity: Severity.Warning,
                    Message: $"Nenhuma codificação no padrão do Cliente ({clienteRx}) localizada no quadro 'Características'.",
                    Location: new ViolationLocation(null, null, null, "Quadro Características")));
            }
        }

        var status = violations.Count == 0 ? CheckStatus.Passed
                   : violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : CheckStatus.Skipped;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }

    private static bool MatchesDocumentPattern(string? value, string? regex)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(regex))
        {
            return false;
        }

        return Codificacao.Casa(value, regex);
    }

    private static bool MatchesAnyDocumentCode(string stem, IEnumerable<string> documentValues)
    {
        var normalizedStem = NormalizeCode(stem);
        return normalizedStem.Length > 0
            && documentValues.Any(value =>
            {
                // Igual, ou igual a menos do sufixo de revisão ("-00", "-0A", "-1").
                var codigo = NormalizeCode(value);
                return codigo == normalizedStem
                    || (codigo.StartsWith(normalizedStem, StringComparison.Ordinal)
                        && codigo.Length - normalizedStem.Length <= 2
                        && value.TrimEnd().Length > 2 && value.TrimEnd()[^3..].Contains('-'));
            });
    }

    private static string NormalizeCode(string value) => Codificacao.Compacto(value);
}
