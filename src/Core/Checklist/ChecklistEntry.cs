namespace WordComplianceValidator.Core.Checklist;

public sealed record ChecklistEntry(
    ChecklistRef Ref,
    int? Revisao,
    string ProcedimentoTitulo,
    string Assunto,
    string Titulo,
    string Descricao,
    bool IaAutomatizavel,
    string? Observacao);
