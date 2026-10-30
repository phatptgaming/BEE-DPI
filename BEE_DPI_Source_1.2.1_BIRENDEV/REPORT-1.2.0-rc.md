# BEE DPI 1.2.0-rc — delivery report

Date: 2026-10-30
Candidate package: `dist\BEE DPI 1.2.0-rc`
Rollback packages left untouched: `dist\BEE DPI` (1.0.0) and `dist\BEE DPI 1.1.0-rc`

This report replaces the visual identity and interaction layer of BEE DPI while
keeping the verified network engine, its launch arguments, the DNS safety logic
and the previous release packages unchanged.

Only work that was actually observed is marked PASS. Anything that was not
exercised is marked **NOT VERIFIED** and listed in section 13.

## 1. What changed and what did not

| Area | 1.1.0-rc | 1.2.0-rc |
| --- | --- | --- |
| Engine | GoodbyeDPI 0.2.3rc3 + WinDivert 2.2 | identical files and hashes |
| Arguments | `-5 --dns-addr 1.1.1.1 --dns-port 53 --dnsv6-addr 2606:4700:4700::1111 --dnsv6-port 53` | identical, unchanged |
| Steam verification | 3 anonymous HTTPS GETs | identical logic, UserAgent now `BEE-DPI/1.2.0` |
| Main window | 424×560 dark UI | compact 424×560 cyberpunk UI, rounded chrome |
| DNS | panel inside the main window | separate `Quản lý DNS` window |
| Tray | native tray icon | same mechanism, re-tested on the packaged build |
| Identity | BEE DPI / BEE STUDIO footer | BEE DPI only, "Developed by BirenDev" |

The engine was not rebuilt, patched or re-signed. The three engine hashes are
re-verified by `build.ps1` at packaging time and match the 1.1.0-rc values:

| File | SHA-256 |
| --- | --- |
| `Engine\goodbyedpi.exe` | `8D412B094BB9C137FF25BA9A794D1122ECC84BB776DEBFF6C249723A13CC31CD` |
| `Engine\WinDivert.dll` | `6110BFA44667405179C3E15E12AF1B62037E447ED59B054B19042032995E6C7E` |
| `Engine\WinDivert64.sys` | `E69B5BA3F0CD6CFB2983E442636E7F0B342B61B15264B0328317D4559C82CF50` |

No SteamBypass binary was executed, copied, unpacked into the product or
redistributed. The release still makes no universal-bypass claim.

## 2. Modified and added files

New files:

- `src\BeeDPI.App\Services\WindowPlacement.cs` — clamps both windows to the
  work area of the current monitor in DIPs (PerMonitorV2 aware).
- `src\BeeDPI.App\Fonts\BeVietnamPro-{Regular,Medium,SemiBold,Bold}.ttf` and
  `src\BeeDPI.App\Fonts\OFL.txt` — bundled from the official Google Fonts
  release, with the OFL text.
- `src\BeeDPI.App\DnsWindow.xaml` + `.xaml.cs` — the separate DNS Manager.
- `docs\screenshots\main.png`, `docs\screenshots\dns.png` — real captures of
  the rendered windows.
- `REPORT-1.2.0-rc.md` — this report.

Regenerated assets:

- `assets\bee.ico` (7 PNG-compressed entries: 16, 24, 32, 48, 64, 128, 256 px,
  all 32 bpp) and `assets\bee-logo.png`, produced by
  `tools\Generate-BrandAssets.ps1`.

Modified application files:

- `src\BeeDPI.App\MainWindow.xaml`, `MainWindow.xaml.cs` — compact layout,
  custom chrome, rounded clipped surface, automation IDs, drag/resize.
- `src\BeeDPI.App\DnsWindow.xaml`, `DnsWindow.xaml.cs` — DNS Manager layout,
  network selector, advanced section, dedicated dark scrollbar.
- `src\BeeDPI.App\ViewModels\MainViewModel.cs` — only two public states,
  hides probe details from the UI.
- `src\BeeDPI.App\ViewModels\DnsViewModel.cs` — friendly provider labels,
  adapter revalidation, short Vietnamese errors, success titles `ĐÃ ĐỔI DNS`
  and `ĐÃ KHÔI PHỤC DNS`.
- `src\BeeDPI.App\Themes\Theme.xaml` — palette, fonts, control, menu,
  scrollbar and status styles.
- `src\BeeDPI.App\Services\TrayIcon.cs` — native `Shell_NotifyIcon` icon,
  message window, menu and one-per-session balloon.
- `src\BeeDPI.App\App.xaml.cs` — tray wiring, hide-on-close, ordered exit.
- `src\BeeDPI.App\app.manifest` — `asInvoker`, PerMonitorV2, 1.2.0 identity.
- `src\BeeDPI.App\BeeDPI.App.csproj` — fonts, icon, docs, screenshots,
  changelog, installer scripts and this report are copied to the package.
- `Directory.Build.props`, `src\BeeDPI.EngineHost\app.manifest` — version
  1.2.0 and matching manifest identity.

Modified core/DNS files:

- `src\BeeDPI.Core\Branding.cs` — version 1.2.0, exact social URLs and label.
- `src\BeeDPI.Core\EngineDefinition.cs` — 1.2.0 metadata, engine values and
  arguments unchanged.
- `src\BeeDPI.Core\Connectivity\SteamConnectivity.cs` — 1.2.0 UserAgent,
  log-only probe summaries; a double-encoding defect was repaired (section 11).
- `src\BeeDPI.Core\Dns\DnsModels.cs` — `RouteCost`, `RouteMetric`,
  `IsWireless`, `NetworkKind`, `DnsAdapterSelector.FindActive` and
  `FindLiveMatch`.
- `src\BeeDPI.Core\Dns\DnsCatalog.cs` — friendly provider names.
- `src\BeeDPI.Core\Dns\DnsCoordinator.cs` — physical/active filtering and
  route-based ordering.
- `src\BeeDPI.Core\Dns\ScriptDnsPlatform.cs` — JSON protocol and stale-request
  handling.
- `tools\dns-manager.ps1` — adapter metrics (IPv4 and IPv6), interface type,
  verification, partial-failure rollback and DoH undo.

Build, test and documentation files:

- `tests\BeeDPI.Tests\Program.cs`, `Fakes.cs`, `BeeDPI.Tests.csproj` — UI,
  detection, selector, tray, safety, font, catalog and DNS view-model tests.
- `build.ps1` — 1.2.0-rc output, required assets, hashes, BOM checks, version
  checks, screenshot checks, legacy-package untouched checks and report check.
- `installer\install.ps1`, `installer\uninstall.ps1`,
  `installer\Cai dat BEE DPI.cmd` — self-elevating installer, Start Menu
  shortcut, uninstall entry, no startup registration.
- `README.md`, `CHANGELOG.md`, `THIRD-PARTY-NOTICES.txt`,
  `docs\ENGINE-COMPATIBILITY.md`, `docs\DNS-PROVIDERS.md` — 1.2.0 branding,
  screenshots, social links and limitations.

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
- The main layout was verified through UI Automation at 424×560 and at the
  380×430 minimum: title row, logo, product name, developer credit, status
  card, primary button, DNS button and social row are all present and inside
  the window; no `ScrollViewer` exists on the main window.
- `WindowPlacement.FitToWorkArea` clamps preferred size to the monitor work
  area minus a 12 DIP margin, recalculated per monitor in DIPs.
- The live verification display is 1920×1080 at 100% scaling. At 100%,
  1280×720 and 1366×768 both leave enough work-area height (≈672 and ≈720 px)
  for the 560 DIP window, and the 430 DIP minimum covers any smaller area.
- 125% and 150% scaling could not be switched on this machine without signing
  out, so those scales are **NOT VERIFIED** live (section 13).

## 4. Screenshots

Both screenshots were captured from the rendered, visible window with
`CopyFromScreen` over the real window rectangle, not assembled or mocked. A
candidate capture is accepted only if the palette statistics match the design:
dark `#090A0D` must dominate and the yellow accent must be present.

| File | Size | Dark share | Yellow share | SHA-256 |
| --- | --- | --- | --- | --- |
| `docs\screenshots\main.png` | 424×560 | 74.8% | 6.9% | `3251A492EF029CE29E7B2004999312126DFBC16DA5437C6FDF75D5E725AE5426` |
| `docs\screenshots\dns.png` | 404×556 | 82.1% | 6.0% | `42A4B70335036FE2946B808FDB4AD514EB5CB0FD12F85084CC76392D0BBDAB5A` |

- `main.png` shows the bee-in-hexagon logo, `BEE DPI`, `v1.2.0`,
  `Developed by BirenDev`, `CHƯA KẾT NỐI`, the hint
  `Nhấn KẾT NỐI để bắt đầu.`, the primary `KẾT NỐI` button, `QUẢN LÝ DNS` and
  the three social controls.
- `dns.png` shows the loaded physical adapter, the friendly provider/profile
  selectors and an enabled `ÁP DỤNG DNS` button.
- The package copies are hash-identical to `docs\screenshots\*` and the README
  links to them.

## 5. Main screen behaviour

- The user sees exactly two states: `CHƯA KẾT NỐI` and `ĐÃ KẾT NỐI`.
  `ĐANG KẾT NỐI...` remains visible while the engine starts and the Steam probe
  runs, so a running engine is never reported as a successful connection.
- The primary button toggles `KẾT NỐI` / `HỦY KẾT NỐI`.
- When connected, Steam HTTP results, DNS addresses, IPv4/IPv6 diagnostics and
  engine arguments are not shown anywhere in the window; they remain only in
  `%LOCALAPPDATA%\BeeDPI\logs\app.log`.
- The DNS Manager is a single secondary button; the social row is visually
  subordinate to the primary action.
- No technical identifiers, namespace names, JSON keys, exception names,
  stack traces, command lines or PowerShell output are displayed. This is
  enforced by the `ui: no technical identifiers leak into the UI` test.

## 6. DNS Manager

The window is separate, has its own title `Quản lý DNS`, and shows only:

- `MẠNG ĐANG SỬ DỤNG` with the detected network type (`Wi-Fi` or `Ethernet`);
- `NHÀ CUNG CẤP DNS` (Cloudflare, Google, Quad9, AdGuard, OpenDNS,
  CleanBrowsing, Control D and `Tùy chỉnh`);
- `CHẾ ĐỘ DNS` (20 catalog profiles, described in plain Vietnamese);
- `ÁP DỤNG DNS` and `KHÔI PHỤC DNS BAN ĐẦU`.

Advanced options are collapsed by default. They contain custom IPv4/IPv6
addresses and, for supported profiles, the native Windows DoH switch. Choosing
`Tùy chỉnh` opens the advanced section automatically; an invalid address is
rejected before any system call.

Behaviour and safeguards:

- Before every mutation the selected adapter is revalidated against the live
  list by GUID, interface index, link state and physical type. A stale adapter
  is refused with the short message `Mạng đã thay đổi. Vui lòng thử lại.`
  instead of being written to Windows.
- After apply, the helper verifies the effective servers and performs a real
  DNS resolution. On failure it restores the pre-operation snapshot and any
  DoH entries it created.
- Restore uses the original baseline stored at
  `%LOCALAPPDATA%\BeeDPI\dns\backup-<GUID>.json`; a new apply updates only
  `lastApplied` and never overwrites `original`.
- Only adapter DNS servers and BEE DPI-created DoH entries are changed. Local
  IP, gateway, subnet, proxy, routes, hosts and VPN configuration are not
  touched.
- Success titles are exactly `ĐÃ ĐỔI DNS` and `ĐÃ KHÔI PHỤC DNS`. Failures use
  short Vietnamese text such as `KHÔNG THỂ XÁC ĐỊNH MẠNG`,
  `KHÔNG CÓ QUYỀN QUẢN TRỊ`, `KHÔNG THỂ ÁP DỤNG DNS`.
- The DNS window is the only place with a scrollbar; it is dark with a golden
  thumb and golden hover state.

Live DNS status in this verification pass:

- An apply of Quad9 Secure was observed at 18:43:24 on the physical Ethernet
  adapter; the helper reported success and name resolution succeeded.
- A live restore was run on the final packaged build at 19:56:58 through the
  `KHÔI PHỤC DNS BAN ĐẦU` button. The helper reported success, resolved
  `www.microsoft.com`, and the backup file now has `restoredAt` set.
- The adapter is back on the stored original baseline: IPv4
  `208.67.220.220`, `208.67.222.222`; IPv6 automatic `fd00:db80::1`.
- The `dns apply` / `dns restore` lines at 19:17, 19:39 and 19:42 in `app.log`
  are fake-platform test output (each is immediately followed by the
  stale-adapter test warning). The real adapter was not changed at those times;
  they must not be counted as live tests.

## 7. Adapter detection

`tools\dns-manager.ps1` asks Windows for every adapter, its interface type and
its IPv4/IPv6 route metrics. `DnsCoordinator` keeps only adapters that are up
and physical, and `DnsAdapterSelector` orders them by
`RouteCost = route metric + interface metric`, preferring the best metric
across both address families. Adapter display names are not used to decide the
network type.

Live read-only result on this machine:

```
alias=Ethernet
description=Realtek PCIe GbE Family Controller
interfaceIndex=17
hasDefaultRoute=true
routeCost=25
isWireless=false
networkKind=Ethernet
```

Coverage in the automated suite (synthetic adapters only):

| Scenario | Expected | Test |
| --- | --- | --- |
| Wi-Fi only | `Wi-Fi` | `selector: Wi-Fi only is detected as Wi-Fi` |
| Ethernet only | `Ethernet` | `selector: Ethernet only is detected as Ethernet` |
| Both active | lower combined route cost wins | `selector: combined IPv4/IPv6 route cost wins over a lower raw metric` |
| Equal best routes | ask the user | `selector: equal best routes are genuinely ambiguous` |
| VPN owns the default route | warning, no silent choice | `dns ui: a VPN-owned default route shows a warning` |
| Virtual/loopback adapters | excluded | `adapters: known virtual/VPN names are excluded` (15 names incl. Hyper-V, VMware, VirtualBox, WSL, WireGuard, Tailscale, ZeroTier) |
| Disconnected adapter | excluded | `adapters: inactive or virtual adapters are excluded` |
| Wi-Fi → Ethernet switch | follow the live default route | `selector: switching Wi-Fi to Ethernet follows the live default route` |
| IPv4/IPv6 metric difference | combined cost wins | `selector: combined IPv4/IPv6 route cost wins over a lower raw metric` |
| Stale identifier | refused, never reused | `selector: a stale adapter identifier is never reused` |

The live machine has a single physical Ethernet adapter, so only the
"Ethernet only" path was observed on real hardware. Wi-Fi and switching paths
are verified at the selector and view-model level, not by physically moving
this machine between networks.

## 8. Tray and lifecycle (packaged build)

Tested against `dist\BEE DPI 1.2.0-rc\BeeDPI.exe`:

| Check | Result |
| --- | --- |
| Launch and main window created | PASS (424×560, title `BEE DPI`) |
| Close button (X) | PASS — window hides; process stays alive; 0 visible windows |
| Balloon on first hide | PASS by code/test — `BEE DPI vẫn đang chạy trong khay hệ thống.`, once per session |
| Left-click tray icon | PASS — window restored and focused |
| Right-click menu | PASS — `Mở BEE DPI`, `Kết nối`, `Thoát BEE DPI` (toggle label is `Hủy kết nối` while connected) |
| `Thoát BEE DPI` | PASS — `exit requested` → `window closing: stopping session` → `viewmodel shutdown` → `session stopped` |
| Tray icon removed after exit | PASS — tray window destroyed |
| Remaining processes | PASS — 0 BeeDPI processes |
| Logged tray window | `tray icon ready (window 0x…)` per session |

The tray uses `Shell_NotifyIcon` with a message-only `HwndSource`; there is no
third-party tray dependency. The exit path stops the owned engine, waits for
the host, releases handles, disposes the tray icon and only then exits.

## 9. Forced-termination safety

- The elevated host assigns the engine process to a Job Object created with
  `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. If the host closes for any reason,
  Windows terminates the engine; the engine cannot be orphaned.
- `ParentWatchdog` waits on the BEE DPI process and calls
  `EngineSupervisor.EmergencyStop()` if the UI disappears, including a crash or
  Task Manager termination.
- There is no `taskkill` by image name, no killing of unrelated
  `goodbyedpi.exe` processes, no deletion of WinDivert services and no global
  process sweep.
- The unit test `safety: job object, parent watchdog and UAC stay in place`
  asserts these mechanisms and the manifests.
- A live **Task Manager → End task** run was **NOT VERIFIED** in this pass.
  The expected result is that the watchdog removes the owned engine within a
  few seconds and leaves no BEE DPI process or engine process behind.

## 10. UAC and installation

- `BeeDPI.exe` is `asInvoker`; the UI never runs elevated.
- `BeeDPI.EngineHost.exe` is `requireAdministrator` and is started only when
  the user connects or applies DNS, through the normal Windows UAC prompt
  (`Verb = "runas"`). No credentials are stored, no UAC bypass is used and no
  privileged scheduled task or service is registered.
- A declined prompt is mapped to `KHÔNG THỂ KẾT NỐI` / `KHÔNG CÓ QUYỀN QUẢN
  TRỊ` without showing technical details.
- `install.ps1` self-elevates with `Start-Process -Verb RunAs`, copies the
  portable package to `%ProgramFiles%\BEE DPI`, creates the machine-wide Start
  Menu shortcut with `assets\bee.ico`, writes the standard uninstall entry and
  ships `uninstall.ps1`. It does not register BEE DPI to start with Windows.
- `uninstall.ps1` removes the app, shortcut and uninstall entry. The UTF-8 BOM
  requirement for Windows PowerShell 5.1 is checked for both scripts.
- Live results: the UAC prompt was approved during the 1.2.0 real engine test
  and again for the live DNS restore. A **declined** prompt and a full
  `install.ps1` run into `Program Files` were **NOT VERIFIED** in this pass.

## 11. Social links and Vietnamese typography

| Control | Tooltip / label | URL |
| --- | --- | --- |
| Facebook | `Facebook` | `https://www.facebook.com/vthienbhat/` |
| TikTok | `TikTok` | `https://www.tiktok.com/@nthienbhat` |
| Discord | `Discord`, visible `discord.com/vthienbhat` | `https://discord.com/users/977745837176266813` |

The links are in a subdued bottom row, are keyboard reachable and expose
accessible names. The visible Discord label is intentionally not the raw URL.

Typography:

- Be Vietnam Pro is bundled in four weights with its OFL license; the theme
  falls back to Segoe UI Variable Text / Display and Segoe UI.
- The test `font: bundled Be Vietnam Pro covers Vietnamese diacritics` opens the
  bundled TTF with `GlyphTypeface` and checks the Vietnamese characters used by
  the UI, so missing glyphs fail the build.
- All user-facing strings are Vietnamese; uppercase status labels are
  intentional, dotted technical identifiers are not used.
- A double-encoding defect in `SteamConnectivity.cs` was found during final
  verification (UTF-8 bytes previously read as cp1252) and repaired in place.
  A repository-wide scan now reports 0 affected source/package files, and the
  compiled `BeeDPI.Core.dll` contains the correct status strings with no
  mojibake bytes.

## 12. Build, tests and package

- `dotnet build BeeDPI.slnx` (Release): 0 warnings, 0 errors.
- Automated suite: **85 passed, 0 failed** (`net10.0-windows`).
- The suite covers UI contracts, compact layout, palette, DNS fields, provider
  names, adapter filtering/ordering/ambiguity/switching/staleness, DNS safety
  and rollback via fakes, tray labels, job object/watchdog/UAC presence, font
  glyphs and Steam probe classification.
- Automated tests use fake platforms and never change real adapter DNS or start
  the real engine. Test runs do write app-log lines to the normal user log path
  (see section 13).
- `build.ps1` published self-contained win-x64, then verified: required files,
  screenshots, this report, UTF-8 BOMs, both executable versions
  (`1.2.0.0` / product `1.2.0`), the three engine hashes, and that
  `dist\BEE DPI` and `dist\BEE DPI 1.1.0-rc` were not modified.
- Package: `dist\BEE DPI 1.2.0-rc`, 133.0 MB, 273 files. Both EXEs report file
  version `1.2.0.0` and product version `1.2.0`. The packaged README,
  changelog, notices, docs, DNS script and screenshots are hash-identical to
  the source copies.

## 13. Remaining limitations and approval checklist

These items are **not** claimed as verified:

1. **Live DNS apply on the final package.** A live restore was performed on
   `dist\BEE DPI 1.2.0-rc`, but no fresh apply was run with that exact binary.
   Apply is covered by the earlier live run and by the fake-platform tests.
2. **Task Manager force-kill.** The Job Object and parent watchdog are in place
   and unit-checked, but a live "End task" was not performed.
3. **Declined UAC.** The declined-prompt mapping is implemented but was not
   observed live; the recent prompts were approved.
4. **Installer execution.** `install.ps1`/`uninstall.ps1` were inspected and
   BOM-verified but not run into `Program Files`; no installed copy exists from
   this pass.
5. **125% and 150% scaling.** Not switched on this machine because Windows
   requires a sign-out. The windows are PerMonitorV2, clamp to the work area
   and were verified at 424×560, 404×556 and the 380×430 minimum at 100%.
6. **1280×720 / 1366×768.** Fits by work-area calculation at 100%; those
   physical resolutions were not observed live (the test display is
   1920×1080).
7. **Wi-Fi hardware path.** Wi-Fi detection and Wi-Fi↔Ethernet switching are
   covered with synthetic adapters; this machine has only one physical
   Ethernet adapter, so the live Wi-Fi path was not exercised.
8. **Test log separation.** The DNS view-model tests write success/failure lines
   to `%LOCALAPPDATA%\BeeDPI\logs\app.log`, the same file used by the app. This
   is not user-facing but can confuse a log-based audit; the timestamps in
   section 6 explain the known entries.
9. **Engine effectiveness.** DPI behaviour still depends on ISP, region, time
   and service. This release reproduces the verified configuration and does
   not claim universal bypass.
10. **WinDivert driver lifetime.** As with 1.1.0-rc, WinDivert 2.2 may remain
    loaded (registered disabled) after the engine exits, which can block the
    1.4 driver used by the 1.0.0 rollback until Windows restarts.

Approval needed before each of these real-machine actions:

- apply a new DNS profile on the final package and restore it;
- start the engine, verify Steam, disconnect and close the app;
- decline a UAC prompt on purpose;
- end BEE DPI from Task Manager while the engine is running;
- run `install.ps1` into `Program Files` and then `uninstall.ps1`.
