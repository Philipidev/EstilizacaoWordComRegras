namespace WordComplianceValidator.Core.Checklist;

public sealed record ChecklistRef(string Ps, string Item, ChecklistPadrao Padrao)
{
    public override string ToString() => $"{Ps}:{Item}:{Padrao}";

    public static string NormalizeItem(string raw) =>
        new string((raw ?? string.Empty)
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Replace("\t", " ")
            .Trim()
            .ToCharArray()
            .Where(c => !char.IsWhiteSpace(c) || c == ' ')
            .ToArray())
        .Replace("  ", " ");
}
