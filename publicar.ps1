<#
.SYNOPSIS
    Gera o pacote distribuível do Revisor: um executável único, sem dependência de .NET.

.DESCRIPTION
    Produz uma pasta pronta para copiar para a máquina de quem vai usar:

        Revisor.exe                              (autocontido, ~80 MB)
        templates\checklists\CL-001-....xlsx
        profiles\*.json

    Quem recebe não precisa de SDK, runtime nem do repositório. Basta arrastar um .docx
    sobre o Revisor.exe, ou dar duplo clique nele.

.PARAMETER Destino
    Pasta de saída. Default: .\dist

.EXAMPLE
    .\publicar.ps1
    .\publicar.ps1 -Destino "\\servidor\ferramentas\Revisor"
#>
param(
    [string]$Destino = "dist"
)

$ErrorActionPreference = "Stop"
$raiz = $PSScriptRoot
$projeto = Join-Path $raiz "src\Cli\WordComplianceValidator.Cli.csproj"

if (Test-Path $Destino) { Remove-Item $Destino -Recurse -Force }

Write-Host "Publicando..." -ForegroundColor Cyan
dotnet publish $projeto `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o $Destino

if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (exit $LASTEXITCODE)." }

# Guarda de segurança: appsettings.Development.json guarda a OPENAI_API_KEY e nunca pode ser
# distribuído. O csproj marca CopyToPublishDirectory=Never, mas isto é barato e o custo de
# errar é uma chave paga vazada em todas as máquinas que receberem o pacote.
$vazamentos = Get-ChildItem $Destino -Recurse -File |
    Where-Object { $_.Name -like "appsettings.*.json" -and $_.Name -ne "appsettings.json" }

if ($vazamentos) {
    $vazamentos | ForEach-Object { Remove-Item $_.FullName -Force }
    throw "Arquivos de configuração local entraram no pacote e foram removidos: " +
          ($vazamentos.Name -join ", ") + ". Verifique o csproj antes de distribuir."
}

$exe = Join-Path $Destino "Revisor.exe"
if (-not (Test-Path $exe)) { throw "Revisor.exe não foi gerado em $Destino." }

$tamanhoMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)

Write-Host ""
Write-Host "Pacote pronto em: $((Resolve-Path $Destino).Path)" -ForegroundColor Green
Write-Host "  Revisor.exe  ($tamanhoMb MB)"
Get-ChildItem $Destino -Recurse -File -Include *.xlsx, *.json |
    ForEach-Object { Write-Host "  $($_.FullName.Substring((Resolve-Path $Destino).Path.Length + 1))" }

Write-Host ""
Write-Host "Sem OPENAI_API_KEY na maquina de destino, o Revisor roda so as verificacoes" -ForegroundColor Yellow
Write-Host "deterministicas; ortografia e as regras semanticas ficam de fora." -ForegroundColor Yellow
