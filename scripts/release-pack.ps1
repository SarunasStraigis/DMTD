param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublishDir = "./publish",
    [string]$OutputDir = "./dist/releases",
    [string]$VelopackVersion = "0.0.1298",
    [string]$PackId = "PhaseLab"
)

$ErrorActionPreference = "Stop"

Write-Host "Publishing PhaseLab $Version ($Configuration, $Runtime)..."

if (Test-Path $PublishDir) {
    Remove-Item -Recurse -Force $PublishDir
}

dotnet publish "PhaseLab.Shell/PhaseLab.Shell.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:Version=$Version `
    -o $PublishDir

Write-Host ""
Write-Host "Packing Velopack release..."

if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$vpkInstalled = dotnet tool list -g | Select-String "vpk"
if (-not $vpkInstalled) {
    dotnet tool install -g vpk --version $VelopackVersion
}

vpk pack `
    --packId $PackId `
    --packVersion $Version `
    --packDir $PublishDir `
    --mainExe PhaseLab.exe `
    --outputDir $OutputDir

Write-Host ""
Write-Host "Done. Installer and release assets are in $OutputDir"
