param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "./dist"
)

$ErrorActionPreference = "Stop"

Write-Host "Publishing PhaseLab ($Configuration, $Runtime)..."

dotnet publish "PhaseLab.Shell/PhaseLab.Shell.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $OutputDir

Write-Host ""
Write-Host "Done. Executable: $OutputDir/PhaseLab.exe"
