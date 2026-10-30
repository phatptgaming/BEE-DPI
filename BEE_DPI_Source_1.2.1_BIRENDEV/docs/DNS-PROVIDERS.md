# BEE DPI — DNS provider catalog

BEE DPI ships a fixed, data-driven DNS catalog (`DnsProviderCatalog.json`, embedded
in `BeeDPI.Core.dll`). The UI never hard-codes provider addresses. To add or change
a provider, edit the JSON catalog, revalidate the profile, and rebuild.

- `lastVerified` records the date the endpoints below were checked against the
  provider's official documentation.
- Every profile is validated at load time: IPv4 and IPv6 addresses must parse,
  duplicates are rejected, and a DoH template must be an absolute HTTPS URL.
- BEE DPI configures **plain DNS** (UDP/TCP port 53) and, where Windows supports
  it, **native Windows DNS over HTTPS**. DoT, DoQ and DNSCrypt are never claimed
  or configured by BEE DPI. A profile having a DoH-capable resolver does not mean
  BEE DPI encrypts DNS unless the user explicitly enables the DoH checkbox on a
  supported Windows build.

Verification date: **2026-10-30**.

## Cloudflare

Source: <https://developers.cloudflare.com/1.1.1.1/setup/>

| Profile | IPv4 | IPv6 | DoH template |
| --- | --- | --- | --- |
| Standard | `1.1.1.1`, `1.0.0.1` | `2606:4700:4700::1111`, `2606:4700:4700::1001` | `https://cloudflare-dns.com/dns-query` |
| Malware blocking (1.1.1.2) | `1.1.1.2`, `1.0.0.2` | `2606:4700:4700::1112`, `2606:4700:4700::1002` | `https://security.cloudflare-dns.com/dns-query` |
| Family (1.1.1.3) | `1.1.1.3`, `1.0.0.3` | `2606:4700:4700::1113`, `2606:4700:4700::1003` | `https://family.cloudflare-dns.com/dns-query` |

## Google Public DNS

Source: <https://developers.google.com/speed/public-dns/docs/using>

| Profile | IPv4 | IPv6 | DoH template |
| --- | --- | --- | --- |
| Standard | `8.8.8.8`, `8.8.4.4` | `2001:4860:4860::8888`, `2001:4860:4860::8844` | `https://dns.google/dns-query` |

## Quad9

Source: <https://quad9.net/service/service-addresses-and-features/>

| Profile | IPv4 | IPv6 | DoH template |
| --- | --- | --- | --- |
| Secure (blocking) | `9.9.9.9`, `149.112.112.112` | `2620:fe::fe`, `2620:fe::9` | `https://dns.quad9.net/dns-query` |
| Unsecured | `9.9.9.10`, `149.112.112.10` | `2620:fe::10`, `2620:fe::fe:10` | `https://dns.quad9.net/dns-query` |

## AdGuard DNS

Source: <https://adguard-dns.io/en/public-dns.html>

| Profile | IPv4 | IPv6 | DoH template |
| --- | --- | --- | --- |
| Default (ad/tracker blocking) | `94.140.14.14`, `94.140.15.15` | `2a10:50c0::ad1:ff`, `2a10:50c0::ad2:ff` | `https://dns.adguard-dns.com/dns-query` |
| Unfiltered | `94.140.14.140`, `94.140.14.141` | `2a10:50c0::1:ff`, `2a10:50c0::2:ff` | `https://unfiltered.adguard-dns.com/dns-query` |
| Family | `94.140.14.15`, `94.140.15.16` | `2a10:50c0::bad1:ff`, `2a10:50c0::bad2:ff` | `https://family.adguard-dns.com/dns-query` |

## OpenDNS (Cisco)

Source: <https://www.cisco.com/c/en/us/support/docs/security/umbrella/225802-configure-opendns-on-windows-11-macos.html>

| Profile | IPv4 | IPv6 | DoH template |
| --- | --- | --- | --- |
| Standard | `208.67.222.222`, `208.67.220.220` | none published | `https://doh.opendns.com/dns-query` |
| FamilyShield | `208.67.222.123`, `208.67.220.123` | none published | `https://doh.familyshield.opendns.com/dns-query` |

## CleanBrowsing

Source: <https://cleanbrowsing.org/filters/>

| Profile | IPv4 | IPv6 | DoH template |
| --- | --- | --- | --- |
| Security | `185.228.168.9`, `185.228.169.9` | `2a0d:2a00:1::2`, `2a0d:2a00:2::2` | `https://doh.cleanbrowsing.org/doh/security-filter/` |
| Adult | `185.228.168.10`, `185.228.169.11` | `2a0d:2a00:1::1`, `2a0d:2a00:2::1` | `https://doh.cleanbrowsing.org/doh/adult-filter/` |
| Family | `185.228.168.168`, `185.228.169.168` | `2a0d:2a00:1::`, `2a0d:2a00:2::` | `https://doh.cleanbrowsing.org/doh/family-filter/` |

## Control D

Source: <https://docs.controld.com/docs/free-dns>

| Profile | IPv4 | IPv6 | DoH template |
| --- | --- | --- | --- |
| Unfiltered | `76.76.2.0`, `76.76.10.0` | `2606:1a40::0`, `2606:1a40:1::0` | `https://freedns.controld.com/p0` |
| Malware | `76.76.2.1`, `76.76.10.1` | `2606:1a40::1`, `2606:1a40:1::1` | `https://freedns.controld.com/p1` |
| Ads & trackers | `76.76.2.2`, `76.76.10.2` | `2606:1a40::2`, `2606:1a40:1::2` | `https://freedns.controld.com/p2` |
| Social | `76.76.2.3`, `76.76.10.3` | `2606:1a40::3`, `2606:1a40:1::3` | `https://freedns.controld.com/p3` |
| Family | `76.76.2.4`, `76.76.10.4` | `2606:1a40::4`, `2606:1a40:1::4` | `https://freedns.controld.com/family` |
| Uncensored | `76.76.2.5`, `76.76.10.5` | `2606:1a40::5`, `2606:1a40:1::5` | `https://freedns.controld.com/uncensored` |

## Custom DNS

`Tùy chỉnh` accepts one or both address families. The user supplies a
comma-separated list of IPv4 addresses and/or IPv6 addresses.

- Empty fields are allowed; at least one address is required to apply.
- Every address must parse as an IPv4 or IPv6 literal.
- Duplicates are rejected.
- A family with no supplied addresses is left untouched.
- Custom entries are plain DNS only. DoH is not offered for custom addresses.

## Why no automatic provider updates

The catalog is compiled into the signed application. BEE DPI never downloads a
provider list at runtime, so a compromised or stale endpoint cannot be injected
silently. Re-verify an endpoint before editing the JSON catalog.
