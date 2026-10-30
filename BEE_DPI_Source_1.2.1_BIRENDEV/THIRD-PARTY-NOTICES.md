BEE DPI — THIRD-PARTY NOTICES
=============================

BEE DPI is an independent application developed by BirenDev.
The BEE DPI user interface and the BEE DPI engine host are original work.
BEE DPI bundles an unmodified third-party network engine and its runtime
components. Those components are not presented as BEE DPI and remain under
their own licenses and copyrights.

The full license texts are distributed in the "licenses" folder next to this
file. Nothing in this notice limits the rights granted by those licenses.


1. GoodbyeDPI 0.2.3rc3
----------------------

Project:  https://github.com/ValdikSS/GoodbyeDPI
License:  Apache License, Version 2.0
Files:    Engine/goodbyedpi.exe
          Engine/WinDivert.dll
          Engine/WinDivert64.sys
Notice:   Copyright (c) ValdikSS and contributors.

The engine binaries are redistributed unmodified, exactly as published in the
official GoodbyeDPI 0.2.3rc3 x86_64 release. 0.2.3rc3 is an upstream
pre-release; the stable 1.0.0 package keeps GoodbyeDPI 0.2.2 as a rollback.

BEE DPI launches the engine with the fixed configuration:

  -5 --dns-addr 1.1.1.1 --dns-port 53 --dnsv6-addr 2606:4700:4700::1111 --dnsv6-port 53

The DNS options are handled inside GoodbyeDPI. BEE DPI does not change Windows
adapter DNS settings for this profile and does not modify, patch or re-brand the
engine.

License text: licenses/LICENSE-Apache-2.0.txt
              licenses/LICENSE-goodbyedpi.txt


2. WinDivert
------------

Project:  https://github.com/basil00/WinDivert
License:  GNU Lesser General Public License, Version 3.0 (LGPL-3.0)
Files:    Engine/WinDivert.dll
          Engine/WinDivert64.sys
Notice:   Copyright (c) Basil L. Delaney and contributors.

WinDivert is used by the GoodbyeDPI engine to intercept and re-inject network
packets. The WinDivert binaries (version 2.2) are redistributed unmodified as
part of the official GoodbyeDPI 0.2.3rc3 release. No changes are made to
WinDivert by BEE DPI.

License text: licenses/LICENSE-windivert.txt


3. getline (NetBSD)
-------------------

License:  BSD 2-Clause
Notice:   Copyright (c) 2011 The NetBSD Foundation, Inc. All rights reserved.
          This code is derived from software contributed to The NetBSD
          Foundation by Christos Zoulas.

getline is compiled into the GoodbyeDPI engine binary.

License text: licenses/LICENSE-getline.txt


4. uthash
---------

Project:  https://github.com/troydhanson/uthash
License:  BSD-style (see license text)
Notice:   Copyright (c) 2005-2014, Troy D. Hanson. All rights reserved.

uthash is compiled into the GoodbyeDPI engine binary.

License text: licenses/LICENSE-uthash.txt


5. Bundled engine verification
------------------------------

SHA-256 of the unmodified GoodbyeDPI 0.2.3rc3 x64 release files:

  goodbyedpi.exe    8D412B094BB9C137FF25BA9A794D1122ECC84BB776DEBFF6C249723A13CC31CD
  WinDivert.dll     6110BFA44667405179C3E15E12AF1B62037E447ED59B054B19042032995E6C7E
  WinDivert64.sys   E69B5BA3F0CD6CFB2983E442636E7F0B342B61B15264B0328317D4559C82CF50

These hashes are checked by build.ps1 before a release package is produced.


6. DNS provider data
--------------------

BEE DPI does not bundle DNS resolver software. The DNS panel configures the
resolver addresses that Windows already supports. Provider addresses and DoH
templates are factual configuration data taken from the providers' official
documentation; see docs/DNS-PROVIDERS.md for the source links and the
verification date.

Provider and product names (Cloudflare, Google Public DNS, Quad9, AdGuard DNS,
OpenDNS, CleanBrowsing, Control D) are trademarks of their respective owners and
are used only to identify the resolver the user chooses. BEE DPI is not
affiliated with, endorsed by, or sponsored by any of them.

Native DNS over HTTPS is provided by Windows itself (Add-DnsClientDohServerAddress
/ Get-DnsClientDohServerAddress). BEE DPI does not ship a DoH client, DoT client,
DoQ client, or DNSCrypt client.


7. Trademarks and affiliation
-----------------------------

BEE DPI and BirenDev are independent project names. GoodbyeDPI and
WinDivert are names of their respective projects and authors. BEE DPI is not
affiliated with, endorsed by, or sponsored by the authors of GoodbyeDPI or
WinDivert. Third-party names are used only for attribution and identification.
