# BEE DPI — Build Instructions

BEE DPI is a C# WPF application targeting .NET 10 on Windows x64.

## Prerequisites

- Windows 10 or Windows 11 (x64)
- .NET SDK 10.0.400 or later
- The WPF desktop workload (`dotnet workload install wpf-desktop` may be needed)

## Clone

```powershell
git clone <repository-url>
cd BEE DPI MANAGER\BeeDPI
```

## Build

```powershell
# Build all projects (Release)
dotnet build BeeDPI.slnx -c Release

# Publish the self-contained win-x64 release package
.\build.ps1
```

The default output is `dist\BEE DPI 1.2.1-rc\`. Both `BeeDPI.exe` and
`BeeDPI.EngineHost.exe` are published as self-contained .NET 10 apps, so the
target machine does not need a separate .NET runtime.

## Framework-dependent build

```powershell
.\build.ps1 -FrameworkDependent
```

This requires .NET Desktop Runtime 10 on the target machine.

## Run tests

```powershell
dotnet run --project tests\BeeDPI.Tests\BeeDPI.Tests.csproj -c Debug
```

The test suite uses fake platforms and never changes real adapter DNS or starts
the real engine.

## Release package contents

`dist\BEE DPI 1.2.1-rc\` contains:

- `BeeDPI.exe` — WPF UI (self-contained .NET 10)
- `BeeDPI.EngineHost.exe` — elevated engine host (self-contained .NET 10)
- `BeeDPI.Core.dll`, `BeeDPI.dll` — core and UI assemblies
- `Engine/` — GoodbyeDPI 0.2.3rc3 + WinDivert 2.2 (unmodified upstream binaries)
- `assets\bee.ico` — application icon
- `LICENSE\` — third-party license texts
- `THIRD-PARTY-NOTICES.txt` — third-party notices
- `README.md`, `CHANGELOG.md`, `REPORT-1.2.1-rc.md` — documentation
- `tools\dns-manager.ps1` — DNS helper script (UTF-8 with BOM)
- `install.ps1`, `uninstall.ps1`, `Cai dat BEE DPI.cmd` — installer scripts
- `docs\` — DNS provider catalog, engine compatibility, screenshots

## Version

Version is managed in `Directory.Build.props` and `src\BeeDPI.Core\Branding.cs`.
Both executables must report the same file and product version.
