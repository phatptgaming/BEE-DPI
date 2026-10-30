# BEE DPI 1.2.1-rc3 — Single-File Optimization Delivery Report

## Summary

Optimized the BEE DPI release package for .NET single-file self-contained deployment.
The extracted release folder is reduced from ~275 files to a compact set of necessary
files, while preserving all application features, the two-process security architecture,
and the unmodified GoodbyeDPI 0.2.3rc3 / WinDivert 2.2 engine.

## Changes

### Single-file publishing (BeeDPI.App.csproj, BeeDPI.EngineHost.csproj)

Both WPF application and EngineHost are now configured for:

- `PublishSingleFile = true` — all managed assemblies and the .NET runtime are
  bundled into each EXE.
- `SelfContained = true` — no separate .NET installation required on the target.
- `RuntimeIdentifier = win-x64` — Windows x64 only.
- `IncludeNativeLibrariesForSelfExtract = true` — native libraries extracted at
  runtime from the single file.
- `EnableCompressionInSingleFile = true` — smaller EXE size.
- `PublishTrimmed = false` — trimming disabled to preserve WPF, XAML resources,
  reflection and dynamically loaded dependencies.
- `DebugSymbols = false`, `DebugType = none` — no PDB files in the release.

### Version

- `Branding.VersionLabel` updated from `v1.2.1-rc` to `v1.2.1-rc3`.
- Product version remains `1.2.1` (file version `1.2.1.0`).

### build.ps1

- Default output path changed to `dist\BEE DPI 1.2.1-rc3`.
- Required-file assertions updated for the single-file layout (no loose .NET DLLs
  at root; Engine files remain in `Engine/`).
- Legacy package checks extended to include `1.2.1-rc2`.
- Report file assertion updated to `REPORT-1.2.1-rc3.md`.

## Resource Dictionary Loading Order

Unchanged from rc2:

1. `Themes/Theme.xaml` — design tokens, brushes, styles, converters.
2. `Themes/Brand.xaml` — master bee logo vector art (`BeeMarkImage`, etc.).

## Build Results

```
dotnet publish src/BeeDPI.App/BeeDPI.App.csproj -c Release -r win-x64 --self-contained true
Build succeeded.
```

## Automated Test Results

```
BEE DPI test suite (95 tests)
95 passed, 0 failed
```

## Package Layout (extracted)

```
BEE DPI/
├── BeeDPI.exe                    (single-file self-contained WPF app)
├── BeeDPI.EngineHost.exe         (single-file self-contained elevated host)
├── Engine/
│   ├── goodbyedpi.exe            (GoodbyeDPI 0.2.3rc3, unmodified)
│   ├── WinDivert.dll             (WinDivert 2.2, unmodified)
│   └── WinDivert64.sys           (WinDivert 2.2 driver, unmodified)
├── assets/
│   └── bee.ico
├── LICENSE/
│   ├── LICENSE-Apache-2.0.txt
│   ├── LICENSE-BeVietnamPro-OFL.txt
│   ├── LICENSE-getline.txt
│   ├── LICENSE-goodbyedpi.txt
│   ├── LICENSE-uthash.txt
│   └── LICENSE-windivert.txt
├── README.txt
├── THIRD-PARTY-NOTICES.txt
├── CHANGELOG.md
├── REPORT-1.2.1-rc3.md
├── install.ps1
├── uninstall.ps1
├── Cai dat BEE DPI.cmd
├── tools\dns-manager.ps1
├── docs\DNS-PROVIDERS.md
├── docs\ENGINE-COMPATIBILITY.md
└── docs\screenshots\main.png, dns.png
```

## Engine Hashes (verified unchanged)

| File | SHA-256 |
|------|---------|
| goodbyedpi.exe | 8D412B094BB9C137FF25BA9A794D1122ECC84BB776DEBFF6C249723A13CC31CD |
| WinDivert.dll | 6110BFA44667405179C3E15E12AF1B62037E447ED59B054B19042032995E6C7E |
| WinDivert64.sys | E69B5BA3F0CD6CFB2983E442636E7F0B342B61B15264B0328317D4559C82CF50 |

## Legacy Packages

All previous releases remain untouched:
- `dist\BEE DPI`
- `dist\BEE DPI 1.1.0-rc`
- `dist\BEE DPI 1.2.0-rc`
- `dist\BEE DPI 1.2.1-rc`
- `dist\BEE DPI 1.2.1-rc2`

## Remaining Limitations

- Single-file extraction to a temporary directory occurs on first launch; this is
  normal .NET single-file behavior and does not affect operation.
- The Engine folder (`goodbyedpi.exe`, `WinDivert.dll`, `WinDivert64.sys`) remains
  as external files because GoodbyeDPI is a separate native process that must be
  launched directly by path.
- No `.exe` installer is provided; the release is a portable ZIP.

## Deliverables

- `dist\BEE DPI 1.2.1-rc3\` — fixed release folder
- `dist\BEE-DPI-1.2.1-rc3-Windows-x64.zip` — Windows release archive
- `dist\BEE-DPI-Source-1.2.1-rc3.zip` — source archive
- `dist\SHA256SUMS.txt` — checksums for both archives and engine binaries
- `REPORT-1.2.1-rc3.md` — this report
- `RELEASE-NOTES.md` — user-facing release notes
