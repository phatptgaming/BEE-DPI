# Engine compatibility and launch profile

BEE DPI launches one bundled, unmodified GoodbyeDPI x64 engine. The engine is an
independent third-party component; BEE DPI only supplies its launch arguments
and manages the process it started.

## Versions

| Item | BEE DPI 1.0.0 (rollback) | BEE DPI 1.1.0-rc | BEE DPI 1.2.1-rc |
| --- | --- | --- | --- |
| GoodbyeDPI | 0.2.2 (stable) | 0.2.3rc3 (upstream pre-release) | 0.2.3rc3 (unchanged) |
| WinDivert | 1.4 | 2.2 | 2.2 (unchanged) |
| Launch profile | `-5` | `-5 --dns-addr 1.1.1.1 --dns-port 53 --dnsv6-addr 2606:4700:4700::1111 --dnsv6-port 53` | identical to 1.1.0-rc |
| Steam reachability probe | no | yes | yes (unchanged) |

The frozen 1.0.0 package in `dist\BEE DPI` and the 1.1.0-rc candidate in
`dist\BEE DPI 1.1.0-rc` are not overwritten. The 1.2.0-rc candidate is produced
in `dist\BEE DPI 1.2.0-rc`; the 1.2.1-rc candidate is produced in
`dist\BEE DPI 1.2.1-rc`.

## Provenance

Both engine sets were downloaded from the official upstream GitHub releases and
hashed locally. The third-party Steam Bypass binaries were not used as a source.

- GoodbyeDPI: <https://github.com/ValdikSS/GoodbyeDPI>
- Releases: <https://github.com/ValdikSS/GoodbyeDPI/releases>
- WinDivert: <https://github.com/basil00/WinDivert>

Exact asset names and SHA-256 values are recorded in
`vendor/goodbyedpi/PROVENANCE.md` and `THIRD-PARTY-NOTICES.txt`.

## Why 0.2.3rc3 is the active engine

The reference application identifies its engine as GoodbyeDPI 0.2.3rc3 with
WinDivert 2.2. The same upstream pre-release supports the exact argument set
used by that application. BEE DPI therefore bundles the official 0.2.3rc3 x64
set in the release candidate while keeping 0.2.2 as a rollback.

0.2.3rc3 is marked **pre-release** upstream. It is shipped here as a release
candidate (`1.2.0-rc`), not silently promoted over the stable 1.0.0 package.
The engine files, hashes and launch arguments are identical to 1.1.0-rc; the
1.2.1 work is a UI, tray, DNS-window and packaging change only.

## Launch arguments

```
-5
--dns-addr 1.1.1.1
--dns-port 53
--dnsv6-addr 2606:4700:4700::1111
--dnsv6-port 53
```

`-5` is the upstream "any country" profile. In both 0.2.2 and 0.2.3rc3 it
expands to `-f 2 -e 2 --auto-ttl --reverse-frag --max-payload`.

The `--dns-*` options are implemented inside GoodbyeDPI. They redirect UDP DNS
packets to the supplied resolver without changing Windows adapter DNS settings.
Each address option must appear before its matching port option; BEE DPI uses a
fixed, unit-tested argument string.

BEE DPI does **not** modify adapter DNS, proxy, gateway, hosts, routes or the
public IP for this profile. The advanced DNS panel remains a separate, explicit
user action and is unrelated to the engine's packet-level DNS redirection.

## WinDivert compatibility

The engine and WinDivert must come from the same upstream release:

- 0.2.3rc3 → `WinDivert.dll` / `WinDivert64.sys` version 2.2
- 0.2.2 → `WinDivert.dll` / `WinDivert64.sys` version 1.4

The two sets are never mixed. `build.ps1` verifies all three SHA-256 hashes in
`engine/` before publishing and re-verifies the published copies. WinDivert is
LGPL-3.0; its license text ships in `LICENSE/LICENSE-windivert.txt`.

## Process ownership

- The elevated host creates exactly one engine process and tracks its handle.
- Disconnect sends a stop request to that host, waits for the owned process to
  exit and releases the pseudoconsole/job object.
- BEE DPI never enumerates or kills processes by image name, never removes a
  WinDivert service it did not create and never bypasses UAC.
- An engine that starts and then exits prematurely is reported as a failure, not
  as a successful connection.

## Connectivity semantics

A running engine is not proof that Steam is reachable. After the engine reports
`Filter activated, GoodbyeDPI is now running!`, BEE DPI sends anonymous HTTPS GET
requests to:

1. <https://store.steampowered.com/>
2. <https://store.steampowered.com/login/?redir=account%2F&redir_ssl=1>
3. <https://steamcommunity.com/login/home/>

Any HTTP response, including 403, 429 and 5xx, proves that the endpoint answered.
DNS, TLS, timeout and connection failures are reported as unreachable. No login
is attempted and no credentials are collected. While connected the probe is
repeated every 60 seconds.

The main window intentionally exposes only two user-facing states:

- `CHƯA KẾT NỐI` — the engine is not running.
- `ĐÃ KẾT NỐI` — the engine is running and the probe has completed.

The detailed per-endpoint HTTP results, engine output and DNS internals remain in
the local log. The probe still runs exactly as in 1.1.0-rc; only its presentation
was simplified.

## Limitations

- DPI behaviour differs by ISP, region and time. No universal bypass is claimed.
- 0.2.3rc3 is an upstream pre-release; the stable 0.2.2 package remains available.
- The probe visits public Steam endpoints directly and does not log in.
- DNS redirection inside the engine is independent of the advanced Windows DNS
  panel; the panel is not required for this profile.
