$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\SoundDeck.App\SoundDeck.App.csproj"
$output = Join-Path $root "artifacts\msix\"

New-Item -ItemType Directory -Force -Path $output | Out-Null

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:GenerateAppxPackageOnBuild=true `
    -p:AppxPackageSigningEnabled=false `
    -p:AppxBundle=Never `
    -p:AppxPackageDir="$output"

Write-Host "Paquete generado en $output"
