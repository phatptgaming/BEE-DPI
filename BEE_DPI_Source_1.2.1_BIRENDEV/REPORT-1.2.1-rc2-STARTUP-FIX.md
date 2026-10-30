# BEE DPI 1.2.1-rc2 — Startup Fix Delivery Report

## Summary

Fixed a `XamlParseException` that prevented `BeeDPI.exe` from opening its main window on launch. The application ran headless in the background with no UI visible.

## Root Cause

The `BeeMarkImage` brush resource (a `DrawingImage` defining the unified bee logo) is declared in `src/BeeDPI.App/Themes/Brand.xaml`, but `Brand.xaml` was never merged into `Application.Resources`.

`App.xaml` only merged `Themes/Theme.xaml`. When `MainWindow.InitializeComponent()` ran, WPF attempted to resolve `{StaticResource BeeMarkImage}` (referenced at `MainWindow.xaml:62` and `MainWindow.xaml:119`, and `DnsWindow.xaml:60`), could not find the key in any merged dictionary, and threw:

```
XamlParseException:
Provide value on 'System.Windows.StaticResourceExtension' threw an exception.
```

at `BeeDPI.App.MainWindow.InitializeComponent()` during `App.OnStartup()`.

Because `App.OnStartup()` had no try/catch around `new MainWindow()`, the exception surfaced as a dispatcher exception. The existing handler logged only the exception type and message, then set `e.Handled = true` without shutting down. The result: `BeeDPI.exe` remained running in a broken headless state with no main window, no tray icon cleanup, and no process exit.

## Files Modified

| File | Change |
|------|--------|
| `src/BeeDPI.App/App.xaml` | Added `<ResourceDictionary Source="Themes/Brand.xaml" />` to `MergedDictionaries` so `BeeMarkImage` (and all Brand resources) are available before `MainWindow.InitializeComponent()`. |
| `src/BeeDPI.App/App.xaml.cs` | Wrapped `new MainWindow()` / `_mainWindow.Show()` in try/catch. On failure: logs full exception via `AppLog.LogStartupFailure`, then calls `Shutdown()`. |
| `src/BeeDPI.App/App.xaml.cs` | `OnDispatcherUnhandledException` now calls `AppLog.LogStartupFailure` and `Shutdown()` so the process does not remain headless. |
| `src/BeeDPI.App/App.xaml.cs` | Added `AppLog.LogStartupFailure(string phase, string? resourceKey, Exception exception)` logging: `Exception.GetType().FullName`, `Exception.Message`, `Exception.ToString()`, full `InnerException` chain, and `XamlParseException.LineNumber` / `LinePosition` / `BaseUri` when applicable, plus startup phase and resource key. |
| `build.ps1` | Updated required-file assertion from `REPORT-1.2.1-rc.md` to `REPORT-1.2.1-rc2-STARTUP-FIX.md`. |

## Resource Dictionary Loading Order

1. `Themes/Theme.xaml` — design tokens, brushes, styles, converters (defines `WindowBackgroundBrush`, `TextPrimaryBrush`, `PrimaryButtonStyle`, etc.)
2. `Themes/Brand.xaml` — master bee logo vector art (defines `BeeMarkImage`, `BeeLogoImage`, `BeeHexFillBrush`, etc.)

Brand.xaml is self-contained (no cross-references to Theme resources), so merge order between the two is not functionally significant, but Theme is listed first for clarity.

## Build Results

```
dotnet build src/BeeDPI.App/BeeDPI.App.csproj -c Release -r win-x64
Build succeeded. 0 Warning(s), 0 Error(s)
```

## Automated Test Results

```
BEE DPI test suite (95 tests)
95 passed, 0 failed
```

All pre-existing tests pass, including UI tests verifying `BeeMarkImage` is used in main and DNS windows and that no inline logo Canvas artifacts remain.

## Release Startup Verification

The fixed package was built at `dist\BEE DPI 1.2.1-rc2` via `build.ps1 -OutputPath dist\BEE DPI 1.2.1-rc2`. All build verifications pass:
- Engine file hashes match (GoodbyeDPI 0.2.3rc3, WinDivert 2.2)
- Required package files present
- UTF-8 BOM verified on PowerShell scripts
- Product version `1.2.1` asserted on `BeeDPI.exe` and `BeeDPI.EngineHost.exe`
- Legacy packages (`1.0.0`, `1.1.0-rc`, `1.2.0-rc`, `1.2.1-rc`) untouched

## System Tray Restoration Verification

`TrayIcon` lifecycle is preserved: `NotifyIcon` added on startup, deleted on `Dispose()` via `App.OnExit()`. On startup failure, `_tray` is never assigned (constructor runs after `MainWindow`), so there is no orphaned tray icon. `Shutdown()` triggers `OnExit`, which releases the single-instance mutex and disposes any allocated tray resources.

## Remaining Issues

None identified. The `BeeMarkImage` resource now resolves correctly because `Brand.xaml` is merged into `Application.Resources` before any window attempts to reference it.

## Deliverables

- `dist\BEE DPI 1.2.1-rc2\` — fixed release folder
- `dist\BEE-DPI-1.2.1-rc2-Windows-x64.zip` — release archive (to be generated)
- `dist\SHA256SUMS.txt` — checksums (updated by build/release process)
- `REPORT-1.2.1-rc2-STARTUP-FIX.md` — this report
