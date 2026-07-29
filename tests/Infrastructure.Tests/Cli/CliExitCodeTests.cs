using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace WordComplianceValidator.Infrastructure.Tests.Cli;

/// <summary>
/// Testes de nível de processo para o código de saída do CLI.
/// <para>
/// O exit code só é observável executando o binário: durante um bom tempo o CLI definia
/// <c>Environment.ExitCode = 2</c> mas encerrava com <c>return await root.InvokeAsync(args)</c>,
/// cujo retorno sobrescrevia o valor — o processo sempre saía com 0. Nenhum teste unitário
/// pegaria isso, e um gate de CI baseado em <c>$LASTEXITCODE</c> aprovava silenciosamente
/// documentos com violações <c>Error</c>.
/// </para>
/// </summary>
public class CliExitCodeTests
{
    private const int ExitAprovado = 0;
    private const int ExitReprovado = 2;

    [Fact]
    public void Documento_conforme_sai_com_zero()
    {
        // O documento conforme é o RN799. O RN-816 deixou de servir aqui: ele carrega tarja de
        // emissão obsoleta (ver RealDocumentCheckTests.DefeitosConhecidos), e usá-lo faria este
        // teste exigir que o validador ignorasse um defeito real para continuar verde.
        var (exitCode, saida) = RodarReview(ProfilePadrao(), "RN799RL6496600.docx");

        exitCode.Should().Be(ExitAprovado, $"documento de referência é conforme.\n{saida}");
        saida.Should().Contain("nenhuma não conformidade");
    }

    [Fact]
    public void Documento_com_violacao_Error_sai_com_dois()
    {
        // logomarcas.minimo=99 força uma violação Severity.Error no documento de referência.
        var profile = ProfilePadrao(p => p["logomarcas.minimo"] = "99");

        var (exitCode, saida) = RodarReview(profile);

        exitCode.Should().Be(ExitReprovado,
            $"há violação Error e o gate de CI depende disso.\n{saida}");
        saida.Should().Contain("não conformes");
    }

    // --- infraestrutura ---

    private static (int ExitCode, string Saida) RodarReview(
        string profilePath, string documento = "RN-816-RL-67456-00.docx")
    {
        var saida = Path.Combine(Path.GetTempPath(), $"cli-exit-{Guid.NewGuid():N}.docx");
        var cliDll = Path.Combine(AppContext.BaseDirectory, "Revisor.dll");
        File.Exists(cliDll).Should().BeTrue(
            $"o CLI precisa estar no output do projeto de testes (ProjectReference): {cliDll}");

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // O --checklist default é resolvido a partir do diretório atual, então o processo
            // roda a partir da raiz do repositório.
            WorkingDirectory = RepoRoot(),
        };
        foreach (var arg in new[]
                 {
                     cliDll, "review",
                     "--doc", RepoFile("templates", documento),
                     "--profile", profilePath,
                     "--out", saida,
                     "--no-llm",
                 })
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(milliseconds: 180_000).Should().BeTrue("o CLI deve terminar em 3 min");

        try { if (File.Exists(saida)) File.Delete(saida); } catch { /* limpeza best-effort */ }

        return (proc.ExitCode, stdout + stderr);
    }

    /// <summary>Copia o profile de exemplo para um arquivo temporário, com ajustes opcionais.</summary>
    private static string ProfilePadrao(Action<Dictionary<string, string>>? ajustar = null)
    {
        var original = JsonDocument.Parse(File.ReadAllText(RepoFile("profiles", "exemplo.json")));
        var raiz = original.RootElement;

        var parametros = raiz.GetProperty("parameters")
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty);

        ajustar?.Invoke(parametros);

        var novo = new
        {
            cliente = raiz.GetProperty("cliente").GetString(),
            versao = raiz.GetProperty("versao").GetString(),
            parameters = parametros,
        };

        var destino = Path.Combine(Path.GetTempPath(), $"profile-{Guid.NewGuid():N}.json");
        File.WriteAllText(destino,
            JsonSerializer.Serialize(novo, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
        return destino;
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(dir, "EstilizacaoWordComRegras.sln"))) return dir;
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        throw new DirectoryNotFoundException("Raiz do repositório não encontrada.");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
}
