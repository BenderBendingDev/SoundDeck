$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\SoundDeck.App\SoundDeck.App.csproj"
$output = Join-Path $root "artifacts\msix\"

New-Item -ItemType Directory -Force -Path $output | Out-Null

$certificate = Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
    Where-Object { $_.Subject -eq "CN=AppPublisher" -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

$signingArguments = if ($certificate) {
    @(
        "-p:AppxPackageSigningEnabled=true",
        "-p:PackageCertificateThumbprint=$($certificate.Thumbprint)",
        "-p:PayloadSigningThumbprint=$($certificate.Thumbprint)"
    )
} else {
    Write-Warning "No se encontró el certificado local; se generará un paquete sin firma."
    @("-p:AppxPackageSigningEnabled=false")
}

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:GenerateAppxPackageOnBuild=true `
    -p:AppxBundle=Never `
    -p:AppxPackageDir="$output" `
    @signingArguments

if ($LASTEXITCODE -ne 0) {
    throw "No se pudo generar el paquete MSIX."
}

Write-Host "Paquete generado en $output"
