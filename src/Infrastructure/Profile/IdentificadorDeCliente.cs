using WordComplianceValidator.Core.Models;
using WordComplianceValidator.Infrastructure.Checks;
using WordComplianceValidator.Infrastructure.OpenXml;

namespace WordComplianceValidator.Infrastructure.Profile;

/// <summary>Cliente reconhecido no documento: o profile e o código que o identificou.</summary>
public sealed record ClienteIdentificado(string Profile, string Cliente, string Codigo);

/// <summary>
/// Descobre de qual cliente é o documento pela codificação do cliente.
/// <para>
/// Cada profile descreve o formato do código do cliente (<c>codificacao.clienteRegex</c>): MRN
/// <c>QD5-PDA-26-04-016-SP</c>, Vale <c>RL-1850PI-X-54747</c>. O código aparece no quadro
/// "Características do Documento" e nos cabeçalhos — é ali que se procura, e não no corpo, onde
/// o documento pode citar documentos de outros clientes. Só há resposta quando exatamente um
/// profile casa; com nenhum ou mais de um, quem chama pergunta à pessoa.
/// </para>
/// </summary>
public static class IdentificadorDeCliente
{
    public static async Task<ClienteIdentificado?> IdentificarAsync(
        string documento, IReadOnlyList<string> profiles, CancellationToken cancellationToken = default)
    {
        if (profiles.Count == 0) return null;

        DocumentStructure estrutura;
        try { estrutura = new DocxStructureExtractor().ExtractFromFile(documento); }
        catch (Exception) { return null; } // arquivo ilegível: a revisão mostra o erro completo

        return await IdentificarAsync(estrutura, profiles, cancellationToken);
    }

    public static async Task<ClienteIdentificado?> IdentificarAsync(
        DocumentStructure estrutura, IReadOnlyList<string> profiles, CancellationToken cancellationToken = default)
    {
        var textos = estrutura.Headers.Concat(estrutura.Footers).Select(h => h.Text)
            .Concat(estrutura.Tables.SelectMany(t => t.Cells).Select(c => c.Text))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        var candidatos = new List<ClienteIdentificado>();
        foreach (var caminho in profiles)
        {
            Core.Profile.ClientProfile profile;
            try { profile = await new JsonClientProfileRepository().LoadAsync(caminho, cancellationToken); }
            catch (Exception) { continue; } // profile inválido: aparece com a mensagem completa se escolhido

            var regex = profile.Get("codificacao.clienteRegex");
            if (string.IsNullOrWhiteSpace(regex)) continue;
            if (Codificacao.EncontrarEmQualquer(textos, regex) is { } codigo)
                candidatos.Add(new ClienteIdentificado(caminho, profile.Cliente, codigo.Completo));
        }

        return candidatos.Count == 1 ? candidatos[0] : null;
    }
}
