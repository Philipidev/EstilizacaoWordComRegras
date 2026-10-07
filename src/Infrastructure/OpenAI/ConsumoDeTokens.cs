using System.Collections.Concurrent;

namespace WordComplianceValidator.Infrastructure.OpenAI;

/// <summary>Tokens gastos com um modelo ao longo de uma revisão.</summary>
/// <param name="EntradaEmCache">Parte de <paramref name="Entrada"/> servida do cache de prompt.</param>
/// <param name="EscritaEmCache">Parte de <paramref name="Entrada"/> gravada no cache — cobrada acima da tarifa cheia.</param>
/// <param name="Raciocinio">Parte de <paramref name="Saida"/> gasta em raciocínio interno.</param>
public sealed record ConsumoDeModelo(
    string Modelo,
    int Chamadas,
    long Entrada,
    long EntradaEmCache,
    long EscritaEmCache,
    long Saida,
    long Raciocinio)
{
    /// <summary>
    /// Custo estimado em US$. Tokens de raciocínio já estão contidos em <see cref="Saida"/> e
    /// são cobrados como saída; tokens em cache são cobrados à tarifa de cache, não à cheia.
    /// </summary>
    public decimal? CustoUsd(PrecoDeModelo? preco)
    {
        if (preco is null) return null;
        var entradaCheia = Math.Max(0, Entrada - EntradaEmCache - EscritaEmCache);
        return (entradaCheia * preco.Entrada
              + EntradaEmCache * preco.EntradaEmCache
              + EscritaEmCache * (preco.EscritaEmCache ?? preco.Entrada)
              + Saida * preco.Saida) / 1_000_000m;
    }
}

/// <summary>Tarifa em US$ por 1 milhão de tokens.</summary>
public sealed class PrecoDeModelo
{
    public decimal Entrada { get; set; }
    public decimal EntradaEmCache { get; set; }
    /// <summary>Tarifa de gravação no cache; sem valor, cobrada como entrada cheia.</summary>
    public decimal? EscritaEmCache { get; set; }
    public decimal Saida { get; set; }
}

/// <summary>
/// Acumulador de consumo por modelo, seguro para os checks que rodam concorrentemente.
/// <para>
/// Existe porque o custo de uma revisão não era observável: a escolha do modelo de cada regra
/// era feita no escuro, e uma regressão que quebrasse o cache de prompt multiplicaria o custo
/// sem que ninguém notasse.
/// </para>
/// </summary>
public sealed class ConsumoDeTokens
{
    private readonly ConcurrentDictionary<string, Contador> _porModelo = new(StringComparer.OrdinalIgnoreCase);

    public void Registrar(string modelo, long entrada, long entradaEmCache, long escritaEmCache,
        long saida, long raciocinio)
    {
        var c = _porModelo.GetOrAdd(modelo, _ => new Contador());
        Interlocked.Increment(ref c.Chamadas);
        Interlocked.Add(ref c.Entrada, entrada);
        Interlocked.Add(ref c.EntradaEmCache, entradaEmCache);
        Interlocked.Add(ref c.EscritaEmCache, escritaEmCache);
        Interlocked.Add(ref c.Saida, saida);
        Interlocked.Add(ref c.Raciocinio, raciocinio);
    }

    public IReadOnlyList<ConsumoDeModelo> Snapshot() =>
        _porModelo
            .Select(kv => new ConsumoDeModelo(kv.Key,
                Volatile.Read(ref kv.Value.Chamadas),
                Interlocked.Read(ref kv.Value.Entrada),
                Interlocked.Read(ref kv.Value.EntradaEmCache),
                Interlocked.Read(ref kv.Value.EscritaEmCache),
                Interlocked.Read(ref kv.Value.Saida),
                Interlocked.Read(ref kv.Value.Raciocinio)))
            .OrderBy(c => c.Modelo, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private sealed class Contador
    {
        public int Chamadas;
        public long Entrada;
        public long EntradaEmCache;
        public long EscritaEmCache;
        public long Saida;
        public long Raciocinio;
    }
}
