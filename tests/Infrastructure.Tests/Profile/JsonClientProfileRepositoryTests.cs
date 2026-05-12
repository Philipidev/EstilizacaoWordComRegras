using FluentAssertions;
using WordComplianceValidator.Infrastructure.Profile;
using Xunit;

namespace WordComplianceValidator.Infrastructure.Tests.Profile;

public class JsonClientProfileRepositoryTests
{
    private static string WriteTemp(string contents)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public async Task Loads_valid_profile()
    {
        var path = WriteTemp("""
        {
          "cliente": "X",
          "versao": "1.0",
          "parameters": {
            "logomarcas.minimo": "2",
            "codificacao.pdaRegex": "^[A-Z]{2}-\\d+$"
          }
        }
        """);
        try
        {
            var profile = await new JsonClientProfileRepository().LoadAsync(path);
            profile.Cliente.Should().Be("X");
            profile.GetInt("logomarcas.minimo").Should().Be(2);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Fails_on_invalid_regex()
    {
        var path = WriteTemp("""
        {
          "cliente": "X",
          "versao": "1.0",
          "parameters": { "codificacao.pdaRegex": "[" }
        }
        """);
        try
        {
            var act = () => new JsonClientProfileRepository().LoadAsync(path);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*codificacao.pdaRegex*regex inválido*");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Fails_on_non_integer_minimo()
    {
        var path = WriteTemp("""
        {
          "cliente": "X",
          "versao": "1.0",
          "parameters": { "logomarcas.minimo": "abc" }
        }
        """);
        try
        {
            var act = () => new JsonClientProfileRepository().LoadAsync(path);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*logomarcas.minimo*inteiro*");
        }
        finally { File.Delete(path); }
    }
}
