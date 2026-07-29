using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Models;

namespace WordComplianceValidator.Core.Checks;

public sealed class ChecklistEngine
{
    private readonly IReadOnlyDictionary<ChecklistRef, IRuleCheck> _checks;
    private readonly bool _honrarColunaIa;
    private readonly Func<ChecklistEntry, DocumentContext, IRuleCheck?>? _fallback;
    private readonly int _maxParalelismo;

    /// <param name="checks">Checks registrados, indexados pelo <see cref="ChecklistRef"/>.</param>
    /// <param name="honrarColunaIa">
    /// Quando <c>true</c>, reproduz o comportamento legado: entradas com <c>IA=Não</c> na
    /// planilha são puladas mesmo que exista um check registrado. O padrão é <c>false</c> —
    /// a coluna <c>IA?</c> do CL-001 é conservadora e não deve limitar o que já foi
    /// automatizado; um check registrado é a evidência de que a regra é automatizável.
    /// </param>
    /// <param name="fallback">
    /// Fábrica consultada para entradas sem check dedicado. É por aqui que o motor semântico
    /// genérico entra: recebe a entrada e o contexto (que carrega o profile do cliente) e
    /// decide se cria um check para ela. Devolver <c>null</c> mantém a entrada como Skipped.
    /// </param>
    /// <param name="maxParalelismo">
    /// Checks executados simultaneamente. Os determinísticos são rápidos, mas cada check
    /// semântico é uma chamada de rede de dezenas de segundos — em série, um documento com
    /// 20+ regras semânticas levaria muitos minutos. Os checks são funções puras sobre o
    /// <see cref="DocumentContext"/>, então podem rodar concorrentemente com segurança.
    /// </param>
    public ChecklistEngine(
        IEnumerable<IRuleCheck> checks,
        bool honrarColunaIa = false,
        Func<ChecklistEntry, DocumentContext, IRuleCheck?>? fallback = null,
        int maxParalelismo = 6)
    {
        _checks = BuildIndex(checks);
        _honrarColunaIa = honrarColunaIa;
        _fallback = fallback;
        _maxParalelismo = Math.Max(1, maxParalelismo);
    }

    private static IReadOnlyDictionary<ChecklistRef, IRuleCheck> BuildIndex(IEnumerable<IRuleCheck> checks)
    {
        var index = new Dictionary<ChecklistRef, IRuleCheck>();
        foreach (var check in checks)
        {
            if (index.TryGetValue(check.Ref, out var existing))
                throw new InvalidOperationException(
                    $"Mais de um IRuleCheck registrado para o mesmo Ref '{check.Ref}': " +
                    $"{existing.GetType().Name} e {check.GetType().Name}. " +
                    "Cada entrada do checklist aceita apenas um check.");
            index.Add(check.Ref, check);
        }
        return index;
    }

    public async Task<IReadOnlyList<RuleCheckResult>> RunAsync(
        DocumentContext ctx,
        IReadOnlyList<ChecklistEntry> catalog,
        CancellationToken cancellationToken = default,
        IProgress<ProgressoDaRevisao>? progresso = null)
    {
        // Posicional: os checks rodam concorrentemente, mas o relatório sai na ordem do CL-001.
        var results = new RuleCheckResult[catalog.Count];
        var pendentes = new List<Task>();
        using var gate = new SemaphoreSlim(_maxParalelismo);

        // Incrementado de vários threads: os checks rodam concorrentemente.
        var concluidos = 0;
        void Reportar(ChecklistRef reference) =>
            progresso?.Report(new ProgressoDaRevisao(
                Interlocked.Increment(ref concluidos), catalog.Count, reference.ToString()));

        for (var i = 0; i < catalog.Count; i++)
        {
            var indice = i;
            var entry = catalog[i];

            // Um check registrado tem precedência sobre a coluna IA? da planilha: se alguém
            // escreveu a regra, ela é automatizável — independente do que o CL-001 previa.
            _checks.TryGetValue(entry.Ref, out var check);
            check ??= _fallback?.Invoke(entry, ctx);

            if (check is not null && !(_honrarColunaIa && !entry.IaAutomatizavel))
            {
                pendentes.Add(ExecutarAsync(check, entry, indice));
                continue;
            }

            var motivo = check is not null
                ? "Check disponível, mas pulado por --only-ia-sim (IA=Não no checklist)."
                : entry.IaAutomatizavel
                    ? "Check ainda não implementado."
                    : "Revisão manual (IA=Não).";

            results[indice] = new RuleCheckResult(entry.Ref, CheckStatus.Skipped,
                Array.Empty<Violation>(), Note: motivo);
            Reportar(entry.Ref);
        }

        await Task.WhenAll(pendentes);
        return results;

        async Task ExecutarAsync(IRuleCheck check, ChecklistEntry entry, int indice)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                results[indice] = await check.RunAsync(ctx, cancellationToken);
            }
            catch (Exception ex)
            {
                results[indice] = new RuleCheckResult(entry.Ref, CheckStatus.Error,
                    Array.Empty<Violation>(),
                    Note: $"Erro ao executar check: {ex.Message}");
            }
            finally
            {
                gate.Release();
                Reportar(entry.Ref);
            }
        }
    }
}
