$ErrorActionPreference = "Stop"

$requiredRuntime = "Microsoft.WindowsDesktop.App 10."
$installedRuntimes = @()

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $installedRuntimes = @(& dotnet --list-runtimes 2>$null)
}

if ($installedRuntimes | Where-Object { $_.StartsWith($requiredRuntime, [StringComparison]::OrdinalIgnoreCase) }) {
    Write-Host ".NET Desktop Runtime 10 já está instalado. Nenhuma alteração foi necessária."
    exit 0
}

if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    Write-Error "WinGet não foi encontrado. Instale manualmente o .NET Desktop Runtime 10 x64 em https://dotnet.microsoft.com/download/dotnet/10.0"
}

Write-Host "Instalando Microsoft .NET Desktop Runtime 10 x64..."
& winget install --id Microsoft.DotNet.DesktopRuntime.10 --exact --source winget --architecture x64 --accept-package-agreements --accept-source-agreements

if ($LASTEXITCODE -ne 0) {
    throw "A instalação do .NET Desktop Runtime terminou com o código $LASTEXITCODE."
}

Write-Host "Requisito instalado. O Ars Extractum já pode ser executado."
