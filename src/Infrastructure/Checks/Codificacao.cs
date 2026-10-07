using System.Text.RegularExpressions;

namespace WordComplianceValidator.Infrastructure.Checks;

/// <summary>
/// Busca de codificações (PdA e Cliente) pelos regexes do profile. Concentra o que quatro
/// checks repetiam com variações — e erravam do mesmo jeito: o sufixo de revisão era
/// <c>-\d+</c>, que não reconhece "-0A", e a busca não tinha fronteira, então casava dentro de
/// códigos maiores.
/// </summary>
public static class Codificacao
{
    private const RegexOptions Opcoes = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Uma codificação encontrada: a base (o que o regex do profile descreve) e o sufixo de revisão.</summary>
    public sealed record Codigo(string Base, string? Sufixo)
    {
        public string Completo => Sufixo is null ? Base : $"{Base}-{Sufixo}";
    }

    /// <summary>
    /// Padrão de busca a partir do regex do profile: sem âncoras, com fronteira dos dois lados
    /// e um sufixo de revisão opcional — "-00"/"-0A" na PdA, "-1"/"-12" no Cliente.
    /// </summary>
    public static Regex PadraoDeBusca(string regex)
    {
        var p = regex.Trim();
        if (p.StartsWith('^')) p = p[1..];
        if (p.EndsWith('$')) p = p[..^1];
        return new Regex(@"(?<![A-Za-z0-9])(?<base>" + p + @")(?:-(?<sufixo>0[A-Za-z]|\d{1,2}))?(?![A-Za-z0-9])", Opcoes);
    }

    public static Codigo? Encontrar(string? texto, string? regex)
    {
        if (string.IsNullOrWhiteSpace(texto) || string.IsNullOrWhiteSpace(regex)) return null;
        var m = PadraoDeBusca(regex).Match(texto);
        if (!m.Success) return null;
        var sufixo = m.Groups["sufixo"].Success ? m.Groups["sufixo"].Value.ToUpperInvariant() : null;
        return new Codigo(m.Groups["base"].Value, sufixo);
    }

    public static Codigo? EncontrarEmQualquer(IEnumerable<string?> textos, string? regex) =>
        textos.Select(t => Encontrar(t, regex)).FirstOrDefault(c => c is not null);

    /// <summary>O valor inteiro é uma codificação do padrão (com ou sem sufixo de revisão).</summary>
    public static bool Casa(string? valor, string? regex)
    {
        if (string.IsNullOrWhiteSpace(valor) || string.IsNullOrWhiteSpace(regex)) return false;
        var c = Encontrar(valor.Trim(), regex);
        return c is not null && string.Equals(c.Completo, valor.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Mensagem única para "o nome do arquivo não segue a codificação". Duas regras do PS-018
    /// (4.3 e 4.3.2 a) apontam o mesmo fato; com o mesmo texto, viram um único item na tela e
    /// um único comentário no Word, citando as duas regras.
    /// </summary>
    public static string MensagemDeNomeDeArquivo(string nomeAtual, string? codigoEsperado, string? extensao) =>
        codigoEsperado is null
            ? $"O nome do arquivo '{nomeAtual}' não segue a codificação PdA nem a do Cliente."
            : $"O nome do arquivo '{nomeAtual}' não corresponde à codificação do documento. " +
              $"Pelo quadro Características, o arquivo deveria se chamar '{codigoEsperado}{extensao}'.";

    /// <summary>Só letras e dígitos, em maiúsculas: "RN-816-RL-67456" e "RN816RL67456" ficam iguais.</summary>
    public static string Compacto(string valor) =>
        new(valor.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
