# GoodbyeDPI engine provenance

BEE DPI bundles the **unmodified x64** GoodbyeDPI binaries published by the
upstream author (ValdikSS). No file is edited, patched or rebuilt.

Third-party binaries from the analysed Steam Bypass application were **not**
used as a source. Every file below was downloaded from the official GitHub
release and hashed locally.

## Active engine: GoodbyeDPI 0.2.3rc3

- Upstream project: https://github.com/ValdikSS/GoodbyeDPI
- Upstream release: https://github.com/ValdikSS/GoodbyeDPI/releases/tag/0.2.3rc3
- Release asset: `goodbyedpi-0.2.3rc3-2.zip`
- Asset SHA-256: `37F96B32D050DADCC930A639EBA68E1CCD57ED5C04A5F77DFCA908F01905A4C5`
- Published: 2024-09-14, marked **pre-release** upstream
- Embedded version string: `GoodbyeDPI v0.2.3rc3`
- Bundled WinDivert: **2.2** (driver `WinDivert64.sys`, file version 2.2)

| File | SHA-256 |
| --- | --- |
| `goodbyedpi.exe` | `8D412B094BB9C137FF25BA9A794D1122ECC84BB776DEBFF6C249723A13CC31CD` |
| `WinDivert.dll` | `6110BFA44667405179C3E15E12AF1B62037E447ED59B054B19042032995E6C7E` |
| `WinDivert64.sys` | `E69B5BA3F0CD6CFB2983E442636E7F0B342B61B15264B0328317D4559C82CF50` |

## Rollback engine: GoodbyeDPI 0.2.2

Kept in `vendor/goodbyedpi/0.2.2/x64` for comparison and rollback. It is the
engine shipped inside the frozen BEE DPI 1.0.0 production package.

- Upstream release: https://github.com/ValdikSS/GoodbyeDPI/releases/tag/0.2.2
- Release asset: `goodbyedpi-0.2.2.zip`
- Asset SHA-256: `00A2F8B99CD817F8C7FC4C449033015F039D18AF213DE78CB66BF202277C0628`
- Embedded version string: `GoodbyeDPI v0.2.2`
- Bundled WinDivert: **1.4**

| File | SHA-256 |
| --- | --- |
| `goodbyedpi.exe` | `331AC6C1D22BA5A0A217F3F27D0D823051869CAFC8B8EF7F2002FA2ACCEBC74E` |
| `WinDivert.dll` | `A97859785A2DF1D4462E7D48D33CCBD89FEDD40DAC4970F4AFD89E63F59EE1EC` |
| `WinDivert64.sys` | `53AB28EC00BE6E6F8AEFA9EE76FC2735E94D7F3F9DBC06EB2B7AC8CD3084A6AF` |

## WinDivert compatibility

GoodbyeDPI 0.2.3rc3 is published together with WinDivert 2.2 and must be used
with that DLL/driver pair. The 0.2.2 engine is published with WinDivert 1.4.
The two engine sets are never mixed: `engine/` always contains one complete,
hash-verified upstream x64 set.

WinDivert is licensed under LGPL-3.0; the upstream license text is included in
`licenses/LICENSE-windivert.txt`. GoodbyeDPI is Apache-2.0
(`licenses/LICENSE-goodbyedpi.txt`).

## Licensing

BEE DPI is an independent product by BirenDev / BEE STUDIO. GoodbyeDPI and
WinDivert remain third-party components and are credited as such in the UI and
in `THIRD-PARTY-NOTICES.txt`.
