# BEE DPI 1.2.1-rc — delivery report

Date: 2026-10-30
Candidate package: `dist\BEE DPI 1.2.1-rc`
Rollback packages left untouched: `dist\BEE DPI` (1.0.0), `dist\BEE DPI 1.1.0-rc` and `dist\BEE DPI 1.2.0-rc`

This report covers UI polish and public release packaging for BEE DPI 1.2.1-rc.
The network engine, its launch arguments, DNS safety logic and all previous
release packages remain unchanged.

## 1. What changed and what did not

| Area | 1.2.0-rc | 1.2.1-rc |
| --- | --- | --- |
| Engine | GoodbyeDPI 0.2.3rc3 + WinDivert 2.2 | identical files and hashes |
| Arguments | `-5 --dns-addr 1.1.1.1 --dns-port 53 --dnsv6-addr 2606:4700:4700::1111 --dnsv6-port 53` | identical, unchanged |
| Main window | compact dark UI with rounded chrome | same, bright outer border removed |
| DNS Manager | separate window, friendly provider names | apply button colors fixed, descriptions removed, profile names match spec |
| Logo | inline Canvas drawing, glow artifact | unified BeeMarkImage vector in all locations |
| Version | 1.2.0 | 1.2.1-rc |

The engine was not rebuilt, patched or re-signed. The three engine hashes are
re-verified by `build.ps1` at packaging time and match the 1.2.0-rc values:

| File | SHA-256 |
| --- | --- |
| `Engine\goodbyedpi.exe` | `8D412B094BB9C137FF25BA9A794D1122ECC84BB776DEBFF6C249723A13CC31CD` |
| `Engine\WinDivert.dll` | `6110BFA44667405179C3E15E12AF1B62037E447ED59B054B19042032995E6C7E` |
| `Engine\WinDivert64.sys` | `E69B5BA3F0CD6CFB2983E442636E7F0B342B61B15264B0328317D4559C82CF50` |

No SteamBypass binary was executed, copied, unpacked into the product or
redistributed. The release still makes no universal-bypass claim.

## 2. Modified and added files

New files:

- `REPORT-1.2.1-rc.md` — this report.
- `KILO_HANDOFF.md` — development handoff record.

Modified application files:

- `src\BeeDPI.App\MainWindow.xaml` — removed WindowFrame BorderBrush/BorderThickness, CornerRadius 22 on WindowChrome, replaced inline logo Canvas with `BeeMarkImage`, removed hero glow ellipse
- `src\BeeDPI.App\DnsWindow.xaml` — same border/logo fixes, title icon uses `BeeMarkImage`
- `src\BeeDPI.App\Themes\Brand.xaml` — fixed `ClipGeometry` → `CombinedGeometry` for WPF compatibility
- `src\BeeDPI.Core\Dns\DnsProviderCatalog.json` — updated profile display names to match spec
- `tests\BeeDPI.Tests\Program.cs` — version assertion 1.2.1, +10 new tests (profile names, no namespace strings, no descriptions, button colors, no outer border, logo usage, provider/profile switching)

Modified build and documentation files:

- `Directory.Build.props` — version 1.2.1, file/assembly version 1.2.1.0
- `src\BeeDPI.Core\Branding.cs` — ProductVersion 1.2.1, VersionLabel v1.2.1-rc
- `build.ps1` — output `dist\BEE DPI 1.2.1-rc`, version assertions 1.2.1, report `REPORT-1.2.1-rc.md`
- `README.md` — version references updated to 1.2.1-rc
- `CHANGELOG.md` — added 1.2.1-rc entry
- `installer\install.ps1` — version references updated to 1.2.1-rc

## 3. Window geometry, chrome and scaling

| Window | Preferred | Minimum | Maximum width | Radius | Scrollbars |
| --- | --- | --- | --- | --- | --- |
| BEE DPI | 424×560 DIP | 380×430 DIP | 470 DIP | 22 px | none |
| Quản lý DNS | 404×556 DIP | 340×380 DIP | 460 DIP | 22 px | DNS window only |

- Both windows are borderless, transparent at the corners and clipped with a
  22 px rounded `RectangleGeometry`, so glow and child controls cannot paint
  outside the rounded silhouette.
- Custom chrome provides minimize and close; the title bar is draggable and the
  window remains resizable.
- Removed the 1 px `BorderBrush`/`BorderThickness` from the outer frame so no
  bright outer border remains.
- `WindowChrome.CornerRadius="22"` matches the visual rounded corners.
- `WindowPlacement.FitToWorkArea` clamps preferred size to the monitor work
  area minus a 12 DIP margin, recalculated per monitor in DIPs.

## 4. DNS Manager changes

- **ÁP DỤNG DNS** button: background `#FFC928`, foreground `#0B0C10`, Bold, with
  normal/hover/pressed/disabled states.
- Provider dropdown shows friendly names: Cloudflare, Google, Quad9, AdGuard,
  OpenDNS, CleanBrowsing, Control D, Tùy chỉnh.
- Profile dropdown shows friendly localized names (e.g. "Chặn mã độc và nội dung
  người lớn", "Bảo vệ mã độc", "Chặn quảng cáo").
- No explanatory description below the profile dropdown.
- Provider switching refreshes available profiles and selects a valid default.
- Stale selections are prevented; correct underlying DNS addresses preserved.

## 5. Logo unification

- Single master mark: bee inside a yellow hexagon, defined in `Brand.xaml` as
  `BeeLogoImage` (full detail) and `BeeMarkImage` (simplified for small sizes).
- Used for main window logo, window title bar icon, EXE icon (`assets/bee.ico`),
  system tray, taskbar and Start Menu shortcut.
- All baked-in rectangular shadows and glow artifacts removed from the logo area.

## 6. Build, tests and package

- `dotnet build BeeDPI.slnx` (Release): 0 warnings, 0 errors.
- Automated suite: **95 passed, 0 failed** (`net10.0-windows`).
- `build.ps1` published self-contained win-x64, then verified: required files,
  screenshots, this report, UTF-8 BOMs, both executable versions
  (`1.2.1.0` / product `1.2.1`), the three engine hashes, and that legacy
  packages `dist\BEE DPI`, `dist\BEE DPI 1.1.0-rc`, `dist\BEE DPI 1.2.0-rc`
  were not modified.
- Package: `dist\BEE DPI 1.2.1-rc`, self-contained win-x64. Both EXEs report
  file version `1.2.1.0` and product version `1.2.1`.

## 7. Remaining limitations

These items are **not** claimed as verified:

1. **Live DNS apply on the final package.** Covered by fake-platform tests.
2. **Task Manager force-kill.** Job Object and parent watchdog are in place
   and unit-checked, but a live "End task" was not performed.
3. **Declined UAC.** The declined-prompt mapping is implemented but was not
   observed live.
4. **Installer execution.** `install.ps1`/`uninstall.ps1` were inspected and
   BOM-verified but not run into `Program Files`.
5. **125% and 150% scaling.** Not switched on this machine; windows are
   PerMonitorV2 and clamp to the work area.
6. **Wi-Fi hardware path.** Wi-Fi detection and switching covered with
   synthetic adapters; this machine has only one physical Ethernet adapter.
7. **Engine effectiveness.** DPI behaviour depends on ISP, region, time and
   service; this release reproduces the verified configuration.
