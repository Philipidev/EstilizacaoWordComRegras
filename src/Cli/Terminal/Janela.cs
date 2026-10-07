using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WordComplianceValidator.Cli.Terminal;

/// <summary>Integração com o Windows: janela própria, abrir arquivo e pasta.</summary>
public static class Janela
{
    /// <summary>
    /// A janela do console foi criada só para este processo — duplo clique no .exe ou arquivo
    /// arrastado sobre ele. Nesse caso, ao terminar, o Windows fecha a janela na hora, e uma
    /// mensagem de erro some antes de alguém conseguir ler.
    /// </summary>
    public static bool Propria()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var processos = new uint[4];
            return GetConsoleProcessList(processos, (uint)processos.Length) == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // DllImport e não LibraryImport: o gerador do LibraryImport exige AllowUnsafeBlocks no
    // projeto inteiro por causa de uma única chamada sem ponteiros.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(uint[] processos, uint quantidade);

    public static void AbrirArquivo(string caminho) =>
        Process.Start(new ProcessStartInfo(caminho) { UseShellExecute = true });

    /// <summary>Abre o Explorer com o arquivo já selecionado.</summary>
    public static void AbrirPasta(string arquivo)
    {
        if (OperatingSystem.IsWindows())
            Process.Start("explorer.exe", $"/select,\"{arquivo}\"");
        else
            Process.Start(new ProcessStartInfo(Path.GetDirectoryName(arquivo)!) { UseShellExecute = true });
    }
}
