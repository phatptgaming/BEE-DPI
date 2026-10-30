# KILO_HANDOFF — BEE DPI 1.2.1-rc

## Status
COMPLETE — All phases A–I done. Final deliverables ready.

## Project
- Root: `E:\Game\BEE DPI MANAGER\BeeDPI`
- Product: BEE DPI (C# WPF / .NET 10)
- Developer: BirenDev
- Target version: 1.2.1-rc

## Completed tasks

PHASE A — Inspection and resume:
- Inspected full project structure (src, tests, docs, dist, engine, licenses, tools, installer, assets)
- Fixed build error: `ClipGeometry` on `GeometryDrawing` in `Brand.xaml` → `CombinedGeometry` (Intersect)
- Build: `dotnet build BeeDPI.slnx -c Release` — 0 warnings, 0 errors
- Confirmed legacy packages untouched: `dist\BEE DPI`, `dist\BEE DPI 1.1.0-rc`, `dist\BEE DPI 1.2.0-rc`
- .NET SDK 10.0.403 confirmed installed

PHASE B — DNS fixes:
- Apply button: #FFC928 bg, #0B0C10 fg, Bold, hover/pressed/disabled states in `DnsPrimaryButtonStyle`
- Provider dropdown: `{Binding DisplayName}` — Cloudflare, Google, Quad9, AdGuard, OpenDNS, CleanBrowsing, Control D, Tùy chỉnh
- Profile dropdown: `{Binding ProfileDisplayName}` — updated catalog names to match spec
- No profile descriptions displayed in DnsWindow.xaml

PHASE C — Window border and logo:
- Removed `BorderThickness`/`BorderBrush` from WindowFrame in both windows
- Set `WindowChrome.CornerRadius="22"` on both windows
- Replaced inline Canvas logo with `BeeMarkImage` from Brand.xaml (main window + DNS title bar)
- Removed hero glow ellipses from both windows

PHASE D — Tests: 95 passed, 0 failed (added 10 new tests)

PHASE E — Release build: `build.ps1` published self-contained win-x64 to `dist\BEE DPI 1.2.1-rc`, all verifications pass

PHASE F — Source archive: `dist\BEE-DPI-Source-1.2.1.zip` (508 KB, no binaries)

PHASE G — Release archive: `dist\BEE-DPI-1.2.1-rc-Windows-x64.zip` (60 MB, no PDBs)

PHASE H — Verification:
- SHA256SUMS.txt created in `dist/`
- Both ZIPs verified to open and contain required files
- Legacy packages untouched (verified by build.ps1)
- Both EXEs report file version 1.2.1.0 / product version 1.2.1
- Engine hashes verified

PHASE I — Final report: REPORT-1.2.1-rc.md, RELEASE-NOTES.md created

## Files modified
- `src/BeeDPI.App/Themes/Brand.xaml` — fixed ClipGeometry → CombinedGeometry
- `src/BeeDPI.App/MainWindow.xaml` — border/logo fixes
- `src/BeeDPI.App/DnsWindow.xaml` — border/logo fixes
- `src/BeeDPI.Core/Dns/DnsProviderCatalog.json` — updated profile names
- `Directory.Build.props` — version 1.2.1 / 1.2.1.0
- `src/BeeDPI.Core/Branding.cs` — version 1.2.1 / v1.2.1-rc
- `build.ps1` — output path, version assertions, report name, legacy package check
- `README.md`, `CHANGELOG.md`, `installer/install.ps1`, `docs/ENGINE-COMPATIBILITY.md` — version references
- `tests/BeeDPI.Tests/Program.cs` — version assertion + 10 new tests
- `src/BeeDPI.App/BeeDPI.App.csproj` — added REPORT-1.2.1-rc.md content

## Files created
- `REPORT-1.2.1-rc.md`
- `RELEASE-NOTES.md`
- `BUILDING.md`
- `LICENSE`
- `THIRD-PARTY-NOTICES.md`
- `dist/SHA256SUMS.txt`
- `dist\BEE-DPI-Source-1.2.1.zip`
- `dist\BEE-DPI-1.2.1-rc-Windows-x64.zip`
- `KILO_HANDOFF.md`

## Build status
- `dotnet build BeeDPI.slnx -c Release`: PASS (0 warnings, 0 errors)
- `build.ps1`: PASS (all verifications, legacy packages untouched)

## Test results
- 95 passed, 0 failed

## Known issues
- None blocking release

## Exact next action
None — all deliverables complete. Ready for manual public release.
