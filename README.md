# PhaseLab

Windows desktop app for DMTD phase analysis and jitter measurement. Use the toolbar to switch modes.

## Install

Download **PhaseLab-win-Setup.exe** from [GitHub Releases](https://github.com/SarunasStraigis/DMTD/releases). No .NET SDK required.

Windows SmartScreen may warn because the installer is not code-signed yet. Choose **More info → Run anyway**.

The app checks for updates on startup and prompts before installing.

## Development

**Requirements:** Windows 10/11 x64, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```powershell
dotnet run --project PhaseLab.Shell/PhaseLab.Shell.csproj
```

Publish a single-file exe:

```powershell
./publish.ps1
```

Output: `dist/PhaseLab.exe`

## REST API

While the app is running, a localhost REST API is available for scripting:

- Docs: [docs/API.md](docs/API.md)
- Swagger UI: http://127.0.0.1:8787/docs
- Snapshot: `GET /api/modules/{dmtd|jitter}/snapshot`

Settings live in `%AppData%\PhaseLab\settings.json` (`apiEnabled`, `apiPort`).

## Releases (maintainers)

```powershell
git tag v1.0.1
git push origin v1.0.1
```

GitHub Actions builds and publishes the installer. To test packaging locally:

```powershell
./scripts/release-pack.ps1 -Version 1.0.1
```
