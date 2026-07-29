namespace WordComplianceValidator.Cli.Terminal;

/// <summary>
/// Localiza o checklist e os profiles sem depender do diretório de trabalho.
/// <para>
/// O default antigo do <c>--checklist</c> era montado a partir de
/// <c>Directory.GetCurrentDirectory()</c>, o que só funciona quando o programa é executado da
/// raiz do repositório. Num <c>.exe</c> publicado — duplo clique, atalho no menu Iniciar,
/// arquivo arrastado de outra pasta — o diretório atual é qualquer coisa, e o checklist
/// "sumia". A busca começa pela pasta do executável e sobe a árvore, o que cobre tanto o
/// binário publicado (recursos ao lado) quanto o <c>dotnet run</c> (recursos na raiz do
/// repositório, vários níveis acima de <c>bin/</c>).
/// </para>
/// </summary>
public static class LocalizadorDeRecursos
{
    private const int NiveisAcima = 8;

    public static string? Checklist() =>
        PrimeiroArquivo(Path.Combine("templates", "checklists"), "*.xlsx")
        ?? PrimeiroArquivo("checklists", "*.xlsx");

    /// <summary>Diretório de profiles de cliente, se existir.</summary>
    public static string? PastaDeProfiles() => PrimeiroDiretorio("profiles");

    public static IReadOnlyList<string> Profiles()
    {
        var pasta = PastaDeProfiles();
        if (pasta is null) return Array.Empty<string>();

        return Directory.GetFiles(pasta, "*.json")
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Saída padrão: ao lado do documento revisado, com sufixo. Manter na mesma pasta do
    /// original evita a pergunta mais comum de quem usa ("onde foi parar o arquivo?").
    /// </summary>
    public static string SaidaPadrao(string documento)
    {
        var pasta = Path.GetDirectoryName(Path.GetFullPath(documento))!;
        var nome = Path.GetFileNameWithoutExtension(documento);
        return Path.Combine(pasta, $"{nome}-REVISADO.docx");
    }

    private static string? PrimeiroDiretorio(string relativo) =>
        Candidatos(relativo).FirstOrDefault(Directory.Exists);

    private static string? PrimeiroArquivo(string relativo, string padrao)
    {
        foreach (var dir in Candidatos(relativo))
        {
            if (!Directory.Exists(dir)) continue;
            var achado = Directory.GetFiles(dir, padrao).OrderBy(f => f).FirstOrDefault();
            if (achado is not null) return achado;
        }
        return null;
    }

    private static IEnumerable<string> Candidatos(string relativo)
    {
        foreach (var raiz in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = raiz;
            for (var i = 0; i < NiveisAcima && dir is not null; i++)
            {
                yield return Path.Combine(dir, relativo);
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }
        }
    }
}
