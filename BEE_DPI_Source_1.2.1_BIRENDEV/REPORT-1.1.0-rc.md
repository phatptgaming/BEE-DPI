# BEE DPI 1.1.0-rc — verification report

Date: 2026-10-30
Candidate package: `dist\BEE DPI 1.1.0-rc` (132.3 MB, 264 files)
Rollback package: `dist\BEE DPI` (1.0.0, unchanged)

This report covers the change from GoodbyeDPI 0.2.2 / `-5` to the verified
reference profile built on the official GoodbyeDPI 0.2.3rc3 x64 release, plus
the real Steam reachability and DNS-redirection tests.

The third-party Steam Bypass binaries (`SteamBypass_Setup.exe`,
`SteamBypass.exe`) were **not** executed, copied, unpacked into the product or
redistributed. Only externally observable behavior (engine identity, version
strings, launch arguments, DNS endpoints, Steam endpoints) was used.

## 1. Engine: old vs new

| Item | BEE DPI 1.0.0 (`dist\BEE DPI`) | BEE DPI 1.1.0-rc (`dist\BEE DPI 1.1.0-rc`) |
| --- | --- | --- |
| Engine | GoodbyeDPI 0.2.2 (stable) | GoodbyeDPI 0.2.3rc3 (upstream pre-release) |
| WinDivert | 1.4 | 2.2 |
| Launch arguments | `-5` | `-5 --dns-addr 1.1.1.1 --dns-port 53 --dnsv6-addr 2606:4700:4700::1111 --dnsv6-port 53` |
| Steam reachability probe | none | Store + Login + Community, anonymous HTTPS GET |
| Package | frozen rollback | new release candidate |

`-5` is the upstream "any country" profile and expands to
`-f 2 -e 2 --auto-ttl --reverse-frag --max-payload` in both releases.

The reference application was observed to use GoodbyeDPI 0.2.3rc3 with
WinDivert 2.2 and exactly the six-token argument set above. The argument order
matters: GoodbyeDPI rejects `--dns-port` before `--dns-addr` (and the IPv6
equivalent); this is enforced by a unit test and by the fixed constant in
`EngineDefinition.Arguments`.

## 2. Provenance

Both engine sets come from official upstream GitHub releases and were hashed
locally. Details are in `vendor/goodbyedpi/PROVENANCE.md`.

- Project: <https://github.com/ValdikSS/GoodbyeDPI>
- Releases: <https://github.com/ValdikSS/GoodbyeDPI/releases>
- WinDivert: <https://github.com/basil00/WinDivert>

Active engine (0.2.3rc3, official asset `goodbyedpi-0.2.3rc3-2.zip`,
SHA-256 `37F96B32D050DADCC930A639EBA68E1CCD57ED5C04A5F77DFCA908F01905A4C5`):

| File | SHA-256 |
| --- | --- |
| `goodbyedpi.exe` | `8D412B094BB9C137FF25BA9A794D1122ECC84BB776DEBFF6C249723A13CC31CD` |
| `WinDivert.dll` | `6110BFA44667405179C3E15E12AF1B62037E447ED59B054B19042032995E6C7E` |
| `WinDivert64.sys` | `E69B5BA3F0CD6CFB2983E442636E7F0B342B61B15264B0328317D4559C82CF50` |

Rollback engine (0.2.2, kept in `vendor/goodbyedpi/0.2.2/x64` and in
`dist\BEE DPI`):

| File | SHA-256 |
| --- | --- |
| `goodbyedpi.exe` | `331AC6C1D22BA5A0A217F3F27D0D823051869CAFC8B8EF7F2002FA2ACCEBC74E` |
| `WinDivert.dll` | `A97859785A2DF1D4462E7D48D33CCBD89FEDD40DAC4970F4AFD89E63F59EE1EC` |
| `WinDivert64.sys` | `53AB28EC00BE6E6F8AEFA9EE76FC2735E94D7F3F9DBC06EB2B7AC8CD3084A6AF` |

Embedded version string of the active engine:
`GoodbyeDPI v0.2.3rc3: Passive DPI blocker and Active DPI circumvention utility`.
`WinDivert64.sys` reports file/product version `2.2`. The bundled
`WinDivert.dll` carries no version resource, but its hash matches the DLL from
the 0.2.3rc3 release archive.

`build.ps1` verifies all three hashes in `engine/` before publishing and
re-verifies the published copies. Publishing uses `CopyToOutputDirectory` /
`CopyToPublishDirectory = Always` for the engine content, so a republish cannot
silently keep stale 0.2.2 files.

## 3. WinDivert compatibility

- 0.2.3rc3 ships and requires WinDivert **2.2**; 0.2.2 ships and requires
  **1.4**. The two sets are never mixed.
- GoodbyeDPI loads the driver that sits next to `goodbyedpi.exe`
  (`Engine\WinDivert64.sys`), not a system copy.
- WinDivert registers a kernel driver named `windivert` pointing at the
  package's `WinDivert64.sys` with start type Disabled, and keeps it loaded
  after the engine exits. During the run `Get-Service` showed no service while
  `sc query windivert` showed the driver RUNNING; after the run the driver was
  still RUNNING and the registry entry was Disabled. This is upstream WinDivert
  behavior, not a BEE DPI leak, and BEE DPI never stops, deletes or replaces
  WinDivert services it did not create.
- Because the 2.2 driver can stay loaded until reboot, switching back to the
  1.0.0 package (WinDivert 1.4) may fail with "incompatible version of the
  WinDivert driver is currently loaded" until Windows is restarted. This does
  not affect normal 1.1.0-rc use.

## 4. Implementation changes

New files:

- `src\BeeDPI.Core\Connectivity\SteamConnectivity.cs` — endpoint list, probe,
  result classification and the required status constants.
- `docs\ENGINE-COMPATIBILITY.md` — versions, arguments, provenance, WinDivert
  and connectivity semantics.
- `vendor\goodbyedpi\PROVENANCE.md` — official URLs, assets and hashes.
- `vendor\goodbyedpi\0.2.2\x64\*` and `vendor\goodbyedpi\0.2.3rc3\x64\*` —
  reference/rollback engine sets.

Modified:

- `src\BeeDPI.Core\EngineDefinition.cs` — 0.2.3rc3, WinDivert 2.2, exact
  arguments, DNS summary, file validation.
- `src\BeeDPI.App\ViewModels\MainViewModel.cs` — connect/disconnect state
  machine, Steam probe after engine start, 60 s refresh while connected,
  error mapping, cancellation/disposal of the probe loop.
- `src\BeeDPI.App\MainWindow.xaml` — `ConnectButton` / `StatusText` automation
  IDs and status bindings.
- `src\BeeDPI.App\BeeDPI.App.csproj` — engine content copied always, docs
  content added.
- `src\BeeDPI.Core\Branding.cs`, `Directory.Build.props` — product version
  1.1.0 (file/assembly version 1.1.0.0).
- `tests\BeeDPI.Tests\Program.cs` — 9 engine/Steam tests added (suite now 63
  tests).
- `build.ps1` — new hashes, 1.1.0-rc output, docs layout checks.
- `README.md`, `THIRD-PARTY-NOTICES.txt` — user-facing engine, status, Steam
  probe and licence updates.
- `engine\*` — replaced with the verified 0.2.3rc3 x64 set.

Branding is unchanged: BEE DPI, "Developed by BirenDev", "A BEE STUDIO
Project". GoodbyeDPI and WinDivert are credited as third-party components, and
the Apache 2.0, LGPL-3.0, getline and uthash license texts ship in `LICENSE/`.

## 5. Status model

| Status | Meaning |
| --- | --- |
| `CHƯA KẾT NỐI.` | Idle; engine not running. |
| `ĐANG KẾT NỐI...` | Requesting elevation / starting the engine. |
| `ENGINE ĐANG HOẠT ĐỘNG.` | Engine confirmed running; Steam probe in progress. |
| `ĐÃ KẾT NỐI.` | Engine running and at least one Steam endpoint answered. |
| `STEAM CHƯA PHẢN HỒI.` | Engine running, no Steam endpoint answered. |
| `ĐANG NGẮT KẾT NỐI.` | Stopping the owned engine. |
| `ĐÃ NGẮT KẾT NỐI.` | Engine fully stopped. |
| `KHÔNG THỂ KẾT NỐI.` | Engine/UAC/host failure (including a declined UAC prompt). |

A running engine is never reported as a successful connection by itself.
Any HTTP response counts as reachable, including 403, 429 and 5xx; 5xx marks the
result degraded. DNS resolution errors, TLS errors, timeouts and connection
failures count as unreachable. No login, cookie or credential is sent.

## 6. Build and automated tests

- `dotnet build BeeDPI.slnx` (SDK 10): **0 warnings, 0 errors**.
- Test suite: **63 passed, 0 failed**.
- New tests cover: exact six-token argument string, DNS address-before-port
  ordering for both families, required engine file set, missing-file
  validation, exact three Steam URLs, 200/403/429/5xx/timeout/TLS/DNS
  classification, anonymous GET-only requests, and all required status strings.
- Automated tests never touch real adapter DNS or start the real engine.
- `build.ps1` published the self-contained win-x64 package and re-verified the
  engine hashes, docs, tools script and both executables.

## 7. Real Steam results

Engine started through the UI with one UAC approval. Authoritative host log
line (`%LOCALAPPDATA%\BeeDPI\logs\engine-host.log`):

```
starting engine: ...\dist\BEE DPI 1.1.0-rc\Engine\goodbyedpi.exe -5 --dns-addr 1.1.1.1 --dns-port 53 --dnsv6-addr 2606:4700:4700::1111 --dnsv6-port 53
```

Observed UI sequence: `CHƯA KẾT NỐI.` → `ĐANG KẾT NỐI...` →
`ENGINE ĐANG HOẠT ĐỘNG.` → `ĐÃ KẾT NỐI.`

Probe results (three separate real runs, including the two accidental UAC
approvals):

| Run | Store | Login | Community | Final status |
| --- | --- | --- | --- | --- |
| 18:28 | HTTP 200 | HTTP 200 | HTTP 200 | `ĐÃ KẾT NỐI.` |
| 18:29 | HTTP 200 | HTTP 200 | HTTP 200 | `ĐÃ KẾT NỐI.` |
| 18:36 | HTTP 200 | HTTP 200 | HTTP 200 | `ĐÃ KẾT NỐI.` |

Probed URLs:

1. `https://store.steampowered.com/`
2. `https://store.steampowered.com/login/?redir=account%2F&redir_ssl=1`
3. `https://steamcommunity.com/login/home/`

The status detail also showed the DNS summary: DNS IPv4 1.1.1.1:53, DNS IPv6
[2606:4700:4700::1111]:53, handled inside the engine, adapter DNS unchanged.

## 8. Real DNS-redirection results

Test-only harness (elevated, temporary, outside the product) bound local UDP
listeners on the machine's own IPv4/IPv6 addresses at port 5353, proved with a
control packet that both listeners work, then started the bundled engine with:

```
-5 --dns-addr <local IPv4> --dns-port 5353 --dnsv6-addr <local IPv6> --dnsv6-port 5353 --dns-verb
```

Queries were then sent to the real public resolvers and had to arrive at the
test listener byte-for-byte:

| Family | Sent to | Observed | Result |
| --- | --- | --- | --- |
| IPv4 | `8.8.8.8:53` | exact 29-byte query, id `0x1234`, at local address:5353 | PASS |
| IPv6 | `[2001:4860:4860::8888]:53` | exact 29-byte query, id `0x5678`, at local IPv6:5353 | PASS |

The harness control passed first, so the listeners were proven reachable before
the engine was involved. An earlier attempt targeting `127.0.0.1`/`::1` could
not work because WinDivert does not handle loopback traffic; that attempt is
not counted as a failure of the product.

After the test: test engine force-stopped, no `goodbyedpi.exe` left, no BEE DPI
process left, and no additional WinDivert service entry beyond the driver that
WinDivert keeps loaded after use (see section 3).

Adapter DNS was checked before and after the engine tests: it still matches the
stored original baseline (IPv4 static 208.67.220.220/208.67.222.222, IPv6
automatic with fd00:db80::1; the DNS backup file has `restoredAt` set). The
engine profile does not change adapter DNS, proxy, gateway, hosts, routes or
the public IP.

## 9. Process-management and lifecycle results

| Test | Result |
| --- | --- |
| Connect through the UI with UAC approval | PASS |
| Exact engine path and six arguments used | PASS (host log, exact match) |
| Engine PID matches the host-reported PID | PASS |
| Steam Store + Login + Community reachable | PASS (HTTP 200 ×3, three runs) |
| Disconnect through the UI | PASS (`ĐÃ NGẮT KẾT NỐI.`) |
| Owned engine and host exit on disconnect | PASS (both PIDs gone) |
| No `goodbyedpi.exe` / `BeeDPI.EngineHost` left after disconnect | PASS |
| Second instance blocked by single-instance dialog | PASS (dialog `BEE DPI`, second process exited, first stayed) |
| Clean close of the main window | PASS (no engine/host/consent left) |
| Missing engine files | PASS at validation level (unit test); live UI variant not run |
| UAC prompt declined | **NOT VERIFIED** — the three attempts intended to decline were all approved; the code path `ERROR_CANCELLED` (1223) → `ElevationCancelledException` → `KHÔNG THỂ KẾT NỐI.` is present but was not exercised live |
| Failure recovery (engine/host error mapping) | Covered by code review and unit-level validation; not forced live |

Process safety constraints were respected: only the engine process created by
the elevated host is tracked and stopped; there is no `taskkill /F /IM
goodbyedpi.exe`, no process kill by image name, no removal of foreign WinDivert
services, no UAC bypass and no security-control change.

## 10. Package verification

`dist\BEE DPI 1.1.0-rc`

- 132.3 MB, 264 files, self-contained win-x64.
- `BeeDPI.exe` and `BeeDPI.EngineHost.exe`: file version `1.1.0.0`, product
  version `1.1.0`.
- `Engine\` contains exactly `goodbyedpi.exe`, `WinDivert.dll`,
  `WinDivert64.sys` with the hashes listed in section 2.
- `docs\DNS-PROVIDERS.md`, `docs\ENGINE-COMPATIBILITY.md`,
  `tools\dns-manager.ps1` (UTF-8 BOM), `README.md`,
  `THIRD-PARTY-NOTICES.txt`, and all five license files are present.
- No `SteamBypass` file is present anywhere in the package.
- The packaged README, notices, docs and DNS script are hash-identical to the
  source copies.
- No source file is newer than the packaged `BeeDPI.dll` (package is current).

Rollback package `dist\BEE DPI` was not modified: its engine still matches the
0.2.2 hashes in section 2 and its `BeeDPI.exe` is file version `1.0.0.0`.

## 11. Remaining limitations

1. UAC-decline behavior is implemented and unit-reachable, but a live declined
   prompt was never observed (all prompts were approved).
2. DPI effectiveness depends on ISP, region, time and the target service. This
   release reproduces the verified reference configuration; it does not claim
   a universal bypass.
3. GoodbyeDPI 0.2.3rc3 is an upstream pre-release. The stable 0.2.2 package
   remains available as rollback, but the WinDivert 2.2 driver may stay loaded
   until reboot, which can block the 1.4 driver used by 1.0.0 until restart.
4. The engine's WinDivert kernel driver remains loaded (registered disabled)
   after the engine exits. BEE DPI does not unload it.
5. WinDivert cannot intercept loopback traffic, so DNS redirection must target
   a non-loopback local address to be observable; redirect targets in the
   product (`1.1.1.1`, `2606:4700:4700::1111`) are unaffected.
6. The Steam probe contacts public endpoints directly and only proves that
   those endpoints answered; it is not proof of overall connectivity or login
   capability. The optional `api.ipify.org` check is not used as proof.
7. Multi-adapter DNS selection, partial-failure rollback in the advanced DNS
   panel, and a failed DoH backup save (warning only) were outside this change
   and are not re-verified here.
8. No public-IP, hosts, proxy, route or adapter-DNS change is performed for the
   engine profile; the advanced DNS panel remains a separate explicit action.
