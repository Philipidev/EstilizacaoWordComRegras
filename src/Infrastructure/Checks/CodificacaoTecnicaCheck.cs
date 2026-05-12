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

        var rotulos = (ctx.Profile.Get("codificacao.aliasesRotulo")
            ?? "Codificação PdA|Codificação|Documento|Código do Documento|Código")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var violations = new List<Violation>();

        // 1) File name should match the PdA regex.
        var fileName = ctx.Structure.FileName;
        if (!string.IsNullOrEmpty(fileName))
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            if (!Regex.IsMatch(stem, pdaRx))
            {
                violations.Add(new Violation(
                    RuleId: Ref.ToString(),
                    Severity: Severity.Warning,
                    Message: $"Nome do arquivo '{stem}' não corresponde ao padrão PdA ({pdaRx}).",
                    Location: new ViolationLocation(null, null, null, "Arquivo")));
            }
        }

        // 2) Quadro "Características" should contain a value matching the PdA regex.
        var titulosAlias = (ctx.Profile.Get("quadroCaracteristicas.aliases")
            ?? "Características do Documento|Quadro de Características|Características")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var table = QuadroCaracteristicas.Find(ctx.Structure, titulosAlias);
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
            var fields = QuadroCaracteristicas.FieldsByLabel(table);
            string? codigo = null;
            foreach (var rot in rotulos)
            {
                var hit = fields.FirstOrDefault(kv => kv.Key.Contains(rot, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(hit.Value)) { codigo = hit.Value; break; }
            }

            if (string.IsNullOrWhiteSpace(codigo))
            {
                violations.Add(new Violation(
                    RuleId: Ref.ToString(),
                    Severity: Severity.Warning,
                    Message: "Não foi possível localizar campo de codificação no quadro 'Características'.",
                    Location: new ViolationLocation(null, null, null, "Quadro Características")));
            }
            else if (!Regex.IsMatch(codigo, pdaRx))
            {
                violations.Add(new Violation(
                    RuleId: Ref.ToString(),
                    Severity: Severity.Error,
                    Message: $"Codificação no quadro Características ('{codigo}') não corresponde ao padrão PdA ({pdaRx}).",
                    Location: new ViolationLocation(null, null, null, "Quadro Características")));
            }
            else if (!string.IsNullOrWhiteSpace(clienteRx) && !Regex.IsMatch(codigo, clienteRx))
            {
                violations.Add(new Violation(
                    RuleId: Ref.ToString(),
                    Severity: Severity.Warning,
                    Message: $"Codificação '{codigo}' não corresponde ao padrão Cliente ({clienteRx}).",
                    Location: new ViolationLocation(null, null, null, "Quadro Características")));
            }
        }

        var status = violations.Count == 0 ? CheckStatus.Passed
                   : violations.Any(v => v.Severity == Severity.Error) ? CheckStatus.Failed
                   : CheckStatus.Failed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }
}
