namespace WordComplianceValidator.Core.Checks;

/// <summary>
/// Andamento de uma revisão, reportado a cada item do checklist resolvido.
/// <para>
/// Existe porque um documento real leva minutos: são dezenas de chamadas de rede, e sem
/// sinal de progresso a interface fica indistinguível de travada. O relatório final continua
/// saindo na ordem do CL-001 — <see cref="Concluidos"/> conta itens resolvidos, não a
/// posição do item atual, já que os checks rodam concorrentemente.
/// </para>
/// </summary>
/// <param name="Concluidos">Itens já resolvidos.</param>
/// <param name="Total">Itens do checklist.</param>
/// <param name="Ref">Referência do item que acabou de ser resolvido (ex.: "PS-002:4.2:Cliente").</param>
public readonly record struct ProgressoDaRevisao(int Concluidos, int Total, string Ref);
