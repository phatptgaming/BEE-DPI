# BEE DPI 1.2.1-rc — Release Notes

BEE DPI  
Developed by BirenDev  
Windows 10/11 x64 · .NET 10 (self-contained)

## What's new in 1.2.1-rc

### DNS Manager

- **ÁP DỤNG DNS** button now uses background `#FFC928` and foreground `#0B0C10`
  (black text on yellow), SemiBold, with correct normal, hover, pressed and
  disabled states.
- Provider dropdown shows friendly names: Cloudflare, Google, Quad9, AdGuard,
  OpenDNS, CleanBrowsing, Control D and Tùy chỉnh — no internal type names.
- Profile dropdown shows friendly localized names, e.g. "Chặn mã độc và nội dung
  người lớn" (Cloudflare), "Bảo vệ mã độc" and "Không lọc" (Quad9),
  "Chặn quảng cáo", "Không lọc" and "Bảo vệ gia đình" (AdGuard).
- The explanatory description below the DNS profile dropdown has been removed.
- Changing provider refreshes available profiles and selects a valid default;
  stale selections are prevented and the correct DNS addresses are preserved.

### Window and logo

- Removed the unwanted bright outer border from both windows. The window frame
  is now a clean dark rounded silhouette with no rectangular artifacts.
- Unified the bee-in-hexagon master logo across the main window, window title
  bar, EXE icon, taskbar, system tray and Start Menu shortcut.
- All baked-in rectangular shadows and glow artifacts removed from the logo.

### Branding

- Product name: **BEE DPI**. Developer credit: **Developed by BirenDev**.
- Removed `BEE STUDIO • A BirenDev Project`.
- Social links: Facebook, TikTok and Discord (visible label
  `discord.com/vthienbhat`, opens `https://discord.com/users/977745837176266813`).

### Engine (unchanged)

- GoodbyeDPI 0.2.3rc3 + WinDivert 2.2 (unmodified upstream binaries).
- Launch arguments:
  `-5 --dns-addr 1.1.1.1 --dns-port 53 --dnsv6-addr 2606:4700:4700::1111 --dnsv6-port 53`
- Real Steam endpoint verification, process supervision, single-instance
  protection and system tray operation are preserved.

## Packaging

- Self-contained .NET 10 x64 — no separate .NET runtime required.
- `dist\BEE DPI 1.2.1-rc` — installable folder.
- `dist\BEE-DPI-1.2.1-rc-Windows-x64.zip` — redistributable ZIP.
- `dist\BEE-DPI-Source-1.2.1.zip` — complete source code for public release.
- Previous release packages (`dist\BEE DPI`, `dist\BEE DPI 1.1.0-rc`,
  `dist\BEE DPI 1.2.0-rc`) remain untouched as rollback options.

## Version

- Both `BeeDPI.exe` and `BeeDPI.EngineHost.exe` report file version `1.2.1.0`
  and product version `1.2.1`.

## SHA-256

See `SHA256SUMS.txt` for archive and engine binary checksums.
