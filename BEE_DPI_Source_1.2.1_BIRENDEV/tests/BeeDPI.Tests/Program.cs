using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using BeeDPI.App.ViewModels;
using BeeDPI.Core;
using BeeDPI.Core.Connectivity;
using BeeDPI.Core.Dns;

namespace BeeDPI.Tests;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Run)> Tests = new();

    private static int _passed;
    private static int _failed;

    private static async Task<int> Main()
    {
        RegisterValidationTests();
        RegisterCatalogTests();
        RegisterBackupTests();
        RegisterApplyRestoreTests();
        RegisterAdapterTests();
        RegisterScriptTests();
        RegisterEngineTests();
        RegisterSteamProbeTests();
        RegisterUiContractTests();
        RegisterDnsUiTests();

        Console.WriteLine($"BEE DPI test suite ({Tests.Count} tests)");
        Console.WriteLine();

        foreach (var test in Tests)
        {
            try
            {
                await test.Run().ConfigureAwait(false);
                _passed++;
                Console.WriteLine("PASS  " + test.Name);
            }
            catch (Exception exception)
            {
                _failed++;
                Console.WriteLine("FAIL  " + test.Name);
                Console.WriteLine("      " + exception.GetType().Name + ": " + exception.Message);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static void Add(string name, Action action) =>
        Tests.Add((name, () =>
        {
            action();
            return Task.CompletedTask;
        }));

    private static void AddAsync(string name, Func<Task> action) => Tests.Add((name, action));

    // ---------------------------------------------------------------------
    // Validation
    // ---------------------------------------------------------------------

    private static void RegisterValidationTests()
    {
        Add("validation: normalizes valid IPv4 and IPv6", () =>
        {
            var result = DnsValidation.Validate(
                new[] { " 8.8.8.8 ", "8.8.4.4" },
                new[] { "2001:0db8:0000:0000:0000:0000:0000:0001" });

            True(result.IsValid);
            Equal(2, result.Ipv4.Count);
            Equal("8.8.8.8", result.Ipv4[0]);
            Equal(1, result.Ipv6.Count);
            Equal("2001:db8::1", result.Ipv6[0]);
        });

        Add("validation: one family is enough", () =>
        {
            var result = DnsValidation.Validate(new[] { "1.1.1.1" }, Array.Empty<string>());
            True(result.IsValid);
            True(result.HasAnyAddress);
        });

        Add("validation: empty input rejected when required", () =>
        {
            var result = DnsValidation.Validate(Array.Empty<string>(), Array.Empty<string>());
            False(result.IsValid);
            Contains("ít nhất", result.Error);
        });

        Add("validation: empty input allowed when optional", () =>
        {
            var result = DnsValidation.Validate(
                Array.Empty<string>(),
                Array.Empty<string>(),
                requireAtLeastOne: false);
            True(result.IsValid);
            False(result.HasAnyAddress);
        });

        Add("validation: rejects duplicates after normalization", () =>
        {
            var result = DnsValidation.Validate(
                new[] { "1.1.1.1", "1.1.1.1" },
                new[] { "2001:db8::1", "2001:0db8::1" });

            False(result.IsValid);
            Contains("trùng", result.Error);
        });

        Add("validation: rejects malformed input", () =>
        {
            var result = DnsValidation.Validate(new[] { "not-an-ip" }, Array.Empty<string>());
            False(result.IsValid);
            Contains("không hợp lệ", result.Error);
        });

        Add("validation: rejects loopback, multicast and unspecified", () =>
        {
            foreach (var address in new[] { "127.0.0.1", "224.0.0.1", "255.255.255.255", "0.0.0.0" })
            {
                False(DnsValidation.Validate(new[] { address }, Array.Empty<string>()).IsValid);
            }

            foreach (var address in new[] { "::1", "ff02::1", "::" })
            {
                False(DnsValidation.Validate(Array.Empty<string>(), new[] { address }).IsValid);
            }
        });

        Add("validation: rejects link-local user input", () =>
        {
            False(DnsValidation.Validate(Array.Empty<string>(), new[] { "fe80::1" }).IsValid);
            True(DnsValidation.TryParseLiteral("fe80::1", out _));
        });

        Add("validation: rejects an address in the wrong family", () =>
        {
            False(DnsValidation.Validate(new[] { "2001:db8::1" }, Array.Empty<string>()).IsValid);
            False(DnsValidation.Validate(Array.Empty<string>(), new[] { "1.1.1.1" }).IsValid);
        });

        Add("validation: enforces maximum addresses per family", () =>
        {
            var addresses = Enumerable.Range(1, 9).Select(i => "1.1.1." + i).ToArray();
            var result = DnsValidation.Validate(addresses, Array.Empty<string>());
            False(result.IsValid);
            Equal(DnsValidation.MaxAddressesPerFamily, result.Ipv4.Count);
            Contains("tối đa", result.Error);
        });
    }

    // ---------------------------------------------------------------------
    // Catalog
    // ---------------------------------------------------------------------

    private static void RegisterCatalogTests()
    {
        Add("catalog: all required providers are present", () =>
        {
            var catalog = DnsCatalog.LoadEmbedded();
            var ids = catalog.Providers.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var required in new[]
                     {
                         "cloudflare", "google", "quad9", "adguard", "opendns", "cleanbrowsing", "controld"
                     })
            {
                True(ids.Contains(required), "missing provider: " + required);
            }

            False(ids.Contains(DnsCatalog.CustomProviderId), "custom must be UI-only");
        });

        Add("catalog: no Yandex DNS is present", () =>
        {
            var catalog = DnsCatalog.LoadEmbedded();
            foreach (var provider in catalog.Providers)
            {
                False(provider.Name.Contains("yandex", StringComparison.OrdinalIgnoreCase));
                False(provider.SourceUrl.Contains("yandex", StringComparison.OrdinalIgnoreCase));
                foreach (var profile in provider.Profiles)
                {
                    False(profile.Name.Contains("yandex", StringComparison.OrdinalIgnoreCase));
                    False(profile.Description.Contains("yandex", StringComparison.OrdinalIgnoreCase));
                }
            }
        });

        Add("catalog: every profile has valid endpoints", () =>
        {
            var catalog = DnsCatalog.LoadEmbedded();
            False(catalog.Providers.Count == 0);
            foreach (var provider in catalog.Providers)
            {
                True(Uri.TryCreate(provider.SourceUrl, UriKind.Absolute, out var source)
                     && source.Scheme == Uri.UriSchemeHttps,
                    "source must be https: " + provider.Id);

                foreach (var profile in provider.Profiles)
                {
                    var validation = DnsValidation.Validate(profile.Ipv4, profile.Ipv6);
                    True(validation.IsValid, "invalid profile: " + profile.Id);

                    if (!string.IsNullOrWhiteSpace(profile.DohTemplate))
                    {
                        True(Uri.TryCreate(profile.DohTemplate, UriKind.Absolute, out var doh)
                             && doh.Scheme == Uri.UriSchemeHttps,
                            "DoH must be https: " + profile.Id);
                        True(profile.SupportsDoh);
                    }
                }
            }
        });

        Add("catalog: reports a verification date", () =>
        {
            var catalog = DnsCatalog.LoadEmbedded();
            True(DateTime.TryParse(catalog.LastVerified, out _));
        });
    }

    // ---------------------------------------------------------------------
    // Backup store
    // ---------------------------------------------------------------------

    private static void RegisterBackupTests()
    {
        AddAsync("backup: round-trips a valid document", async () =>
        {
            using var temp = new TempDirectory();
            var store = new FileDnsBackupStore(temp.Path);
            var document = NewBackup();

            await store.SaveAsync(document);
            var load = await store.LoadAsync(document.AdapterGuid);

            Equal(DnsBackupStatus.Valid, load.Status);
            Equal(TestData.Guid, load.Document!.AdapterGuid);
            Equal(document.Original.Fingerprint, load.Document.Original.Fingerprint);
        });

        AddAsync("backup: original baseline survives profile switches", async () =>
        {
            using var temp = new TempDirectory();
            var store = new FileDnsBackupStore(temp.Path);
            var document = NewBackup();
            var originalFingerprint = document.Original.Fingerprint;

            await store.SaveAsync(document);

            var applied = (await store.LoadAsync(document.AdapterGuid)).Document!;
            applied.LastApplied = TestData.Snapshot(
                ipv4: DnsFamilyState.Static(new[] { "9.9.9.9", "149.112.112.112" }),
                ipv6: DnsFamilyState.Static(new[] { "2620:fe::fe" }));
            applied.AppliedProfileId = "second";
            applied.AppliedProfileName = "Second";
            await store.SaveAsync(applied);

            var reloaded = (await store.LoadAsync(document.AdapterGuid)).Document!;
            Equal(originalFingerprint, reloaded.Original.Fingerprint);
            Equal("second", reloaded.AppliedProfileId);
        });

        AddAsync("backup: atomic save leaves no temporary files", async () =>
        {
            using var temp = new TempDirectory();
            var store = new FileDnsBackupStore(temp.Path);
            await store.SaveAsync(NewBackup());
            Equal(0, Directory.GetFiles(temp.Path, "*.tmp").Length);
        });

        AddAsync("backup: invalid JSON is reported as corrupt", async () =>
        {
            using var temp = new TempDirectory();
            var store = new FileDnsBackupStore(temp.Path);
            var path = Path.Combine(
                temp.Path,
                "backup-" + FileDnsBackupStore.NormalizeGuid(TestData.Guid) + ".json");
            await File.WriteAllTextAsync(path, "{ not json");

            var load = await store.LoadAsync(TestData.Guid);
            Equal(DnsBackupStatus.Corrupt, load.Status);
        });

        AddAsync("backup: document for another adapter is rejected", async () =>
        {
            using var temp = new TempDirectory();
            var store = new FileDnsBackupStore(temp.Path);
            const string otherGuid = "{11111111-2222-3333-4444-555555555555}";
            var json = JsonSerializer.Serialize(NewBackup(), DnsJson.Options);
            var path = Path.Combine(
                temp.Path,
                "backup-" + FileDnsBackupStore.NormalizeGuid(otherGuid) + ".json");
            await File.WriteAllTextAsync(path, json);

            var load = await store.LoadAsync(otherGuid);
            Equal(DnsBackupStatus.Corrupt, load.Status);
        });

        AddAsync("backup: DHCP link-local address is accepted", async () =>
        {
            using var temp = new TempDirectory();
            var store = new FileDnsBackupStore(temp.Path);
            var document = NewBackup(
                ipv6: DnsFamilyState.Automatic(new[] { "fe80::1" }));

            await store.SaveAsync(document);
            var load = await store.LoadAsync(document.AdapterGuid);
            Equal(DnsBackupStatus.Valid, load.Status);
        });

        AddAsync("backup: static link-local address is rejected", async () =>
        {
            using var temp = new TempDirectory();
            var store = new FileDnsBackupStore(temp.Path);
            var document = NewBackup(ipv6: DnsFamilyState.Static(new[] { "fe80::1" }));

            await ThrowsAsync<InvalidDataException>(() => store.SaveAsync(document));
        });

        AddAsync("backup: missing file is reported as missing", async () =>
        {
            using var temp = new TempDirectory();
            var store = new FileDnsBackupStore(temp.Path);
            var load = await store.LoadAsync(TestData.Guid);
            Equal(DnsBackupStatus.Missing, load.Status);
        });
    }

    // ---------------------------------------------------------------------
    // Apply / restore orchestration
    // ---------------------------------------------------------------------

    private static void RegisterApplyRestoreTests()
    {
        AddAsync("apply: backs up the original before mutating DNS", async () =>
        {
            var store = new FakeBackupStore();
            var platform = new FakePlatform { BackupStore = store };
            var initialFingerprint = platform.Current.Fingerprint;
            var sawBackup = false;

            platform.ApplyHandler = (request, backups) =>
            {
                var load = backups.LoadAsync(request.AdapterGuid).GetAwaiter().GetResult();
                sawBackup = load.Status == DnsBackupStatus.Valid;
                return Task.FromResult(platform.BuildApplySuccess(request));
            };

            var coordinator = new DnsCoordinator(platform, store);
            var result = await coordinator.ApplyAsync(TestData.Adapter(), TestData.Profile(), useDoh: false);

            True(result.Success);
            True(sawBackup, "the backup must exist before the platform is invoked");

            var backup = (await store.LoadAsync(TestData.Guid)).Document!;
            Equal(initialFingerprint, backup.Original.Fingerprint);
            True(backup.LastApplied is not null);
            Equal("Cloudflare — cloudflare-standard", backup.AppliedProfileName);
            Equal(0, backup.DohChanges.Count);
        });

        AddAsync("apply: original baseline is never overwritten", async () =>
        {
            var store = new FakeBackupStore();
            var platform = new FakePlatform { BackupStore = store };
            var coordinator = new DnsCoordinator(platform, store);
            var originalFingerprint = platform.Current.Fingerprint;

            await coordinator.ApplyAsync(TestData.Adapter(), TestData.Profile(), useDoh: false);
            await coordinator.ApplyAsync(
                TestData.Adapter(),
                TestData.Profile(
                    id: "google-standard",
                    provider: "Google Public DNS",
                    supportsDoh: false,
                    dohTemplate: null,
                    ipv4: new[] { "8.8.8.8", "8.8.4.4" },
                    ipv6: new[] { "2001:4860:4860::8888" }),
                useDoh: false);

            var backup = (await store.LoadAsync(TestData.Guid)).Document!;
            Equal(originalFingerprint, backup.Original.Fingerprint);
            Equal("Google Public DNS — google-standard", backup.AppliedProfileName);
        });

        AddAsync("apply: failure keeps the original backup and does not report success", async () =>
        {
            var store = new FakeBackupStore();
            var platform = new FakePlatform { BackupStore = store };
            var originalFingerprint = platform.Current.Fingerprint;

            platform.ApplyHandler = (_, _) => Task.FromResult(new DnsApplyResult(
                false,
                "APPLY_FAILED",
                "Không thể áp dụng DNS.",
                null,
                Array.Empty<DnsDohChange>(),
                "Không thể xác minh DNS.",
                "Đã khôi phục cấu hình DNS trước đó."));

            var coordinator = new DnsCoordinator(platform, store);
            var error = await ThrowsAsync<DnsOperationException>(
                () => coordinator.ApplyAsync(TestData.Adapter(), TestData.Profile(), useDoh: false));

            Equal("APPLY_FAILED", error.Code);
            var backup = (await store.LoadAsync(TestData.Guid)).Document!;
            Equal(originalFingerprint, backup.Original.Fingerprint);
            True(backup.LastApplied is null);
        });

        AddAsync("apply: invalid profile is rejected before touching the platform", async () =>
        {
            var store = new FakeBackupStore();
            var platform = new FakePlatform { BackupStore = store };
            var coordinator = new DnsCoordinator(platform, store);

            var error = await ThrowsAsync<DnsOperationException>(
                () => coordinator.ApplyAsync(
                    TestData.Adapter(),
                    TestData.Profile(ipv4: new[] { "999.1.1.1" }, ipv6: Array.Empty<string>()),
                    useDoh: false));

            Equal("INVALID_ADDRESS", error.Code);
            Equal(0, platform.ApplyCalls.Count);
        });

        AddAsync("apply: DoH is refused for a profile that does not support it", async () =>
        {
            var store = new FakeBackupStore();
            var platform = new FakePlatform { BackupStore = store };
            var coordinator = new DnsCoordinator(platform, store);

            var error = await ThrowsAsync<DnsOperationException>(
                () => coordinator.ApplyAsync(
                    TestData.Adapter(),
                    TestData.Profile(supportsDoh: false, dohTemplate: null),
                    useDoh: true));

            Equal("DOH_UNSUPPORTED_PROFILE", error.Code);
            Equal(0, platform.ApplyCalls.Count);
        });

        AddAsync("apply: DoH state and prior changes are passed to the platform", async () =>
        {
            var store = new FakeBackupStore();
            var platform = new FakePlatform { BackupStore = store };
            var coordinator = new DnsCoordinator(platform, store);

            await coordinator.ApplyAsync(TestData.Adapter(), TestData.Profile(), useDoh: true);

            var request = platform.ApplyCalls[0];
            True(request.UseDoh);
            Equal("https://cloudflare-dns.com/dns-query", request.DohTemplate);
            Equal(4, request.DohAddresses.Count);
            Equal(0, request.PreviousDohChanges.Count);

            var backup = (await store.LoadAsync(TestData.Guid)).Document!;
            Equal(4, backup.DohChanges.Count);

            await coordinator.ApplyAsync(TestData.Adapter(), TestData.Profile(), useDoh: true);
            Equal(4, platform.ApplyCalls[1].PreviousDohChanges.Count);
        });

        AddAsync("apply: a family without addresses is left unchanged", async () =>
        {
            var store = new FakeBackupStore();
            var platform = new FakePlatform { BackupStore = store };
            var coordinator = new DnsCoordinator(platform, store);
            var originalIpv6 = platform.Current.Ipv6;

            await coordinator.ApplyAsync(
                TestData.Adapter(),
                TestData.Profile(ipv4: new[] { "1.1.1.1" }, ipv6: Array.Empty<string>()),
                useDoh: false);

            Equal(0, platform.ApplyCalls[0].Ipv6.Count);
            Equal(originalIpv6.FingerprintKey(), platform.Current.Ipv6.FingerprintKey());
        });

        AddAsync("restore: restores the exact original state per family", async () =>
        {
            var store = new FakeBackupStore();
            var platform = new FakePlatform { BackupStore = store };
            var coordinator = new DnsCoordinator(platform, store);

            var original = platform.Current;
            await coordinator.ApplyAsync(TestData.Adapter(), TestData.Profile(), useDoh: false);
            var result = await coordinator.RestoreAsync(TestData.Adapter(), allowOverwriteExternalChanges: false);

            True(result.Success);
            var request = platform.RestoreCalls[0];
            Equal(original.Ipv4.IsStatic, request.OriginalIpv4.IsStatic);
            Equal(string.Join(",", original.Ipv4.ServerAddresses), string.Join(",", request.OriginalIpv4.ServerAddresses));
            False(request.OriginalIpv6.IsStatic);
            Equal(string.Join(",", original.Ipv6.ServerAddresses), string.Join(",", request.OriginalIpv6.ServerAddresses));

            var backup = (await store.LoadAsync(TestData.Guid)).Document!;
            True(backup.RestoredAt is not null);
            True(backup.LastApplied is null);
            Equal(0, backup.DohChanges.Count);
        });

        AddAsync("restore: DoH changes from the backup are reverted", async () =>
        {
            var store = new FakeBackupStore();
            var platform = new FakePlatform { BackupStore = store };
            var coordinator = new DnsCoordinator(platform, store);

            await coordinator.ApplyAsync(TestData.Adapter(), TestData.Profile(), useDoh: true);
            await coordinator.RestoreAsync(TestData.Adapter(), allowOverwriteExternalChanges: false);

            Equal(4, platform.RestoreCalls[0].PreviousDohChanges.Count);
        });

        AddAsync("restore: external change is refused unless overwrite is allowed", async () =>
        {
            var store = new FakeBackupStore();
            var platform = new FakePlatform { BackupStore = store };
            var coordinator = new DnsCoordinator(platform, store);

            await coordinator.ApplyAsync(TestData.Adapter(), TestData.Profile(), useDoh: false);

            platform.Current = TestData.Snapshot(
                ipv4: DnsFamilyState.Static(new[] { "76.76.2.0", "76.76.10.0" }),
                ipv6: DnsFamilyState.Automatic(new[] { "fd00:db80::1" }));

            var error = await ThrowsAsync<DnsConfigurationChangedException>(
                () => coordinator.RestoreAsync(TestData.Adapter(), allowOverwriteExternalChanges: false));
            Equal(0, platform.RestoreCalls.Count);
            True(error.Current.Ipv4.ServerAddresses.Contains("76.76.2.0"));

            var result = await coordinator.RestoreAsync(TestData.Adapter(), allowOverwriteExternalChanges: true);
            True(result.Success);
            Equal(1, platform.RestoreCalls.Count);
        });

        AddAsync("restore: no backup is a clear error", async () =>
        {
            var store = new FakeBackupStore();
            var platform = new FakePlatform { BackupStore = store };
            var coordinator = new DnsCoordinator(platform, store);

            var error = await ThrowsAsync<DnsOperationException>(
                () => coordinator.RestoreAsync(TestData.Adapter(), allowOverwriteExternalChanges: false));
            Equal("NO_BACKUP", error.Code);
        });

        AddAsync("corrupt backup blocks both apply and restore", async () =>
        {
            var store = new FakeBackupStore { Corrupt = true };
            var platform = new FakePlatform { BackupStore = store };
            var coordinator = new DnsCoordinator(platform, store);

            var applyError = await ThrowsAsync<DnsOperationException>(
                () => coordinator.ApplyAsync(TestData.Adapter(), TestData.Profile(), useDoh: false));
            Equal("BACKUP_CORRUPT", applyError.Code);

            var restoreError = await ThrowsAsync<DnsOperationException>(
                () => coordinator.RestoreAsync(TestData.Adapter(), allowOverwriteExternalChanges: false));
            Equal("BACKUP_CORRUPT", restoreError.Code);
        });
    }

    // ---------------------------------------------------------------------
    // Adapter selection
    // ---------------------------------------------------------------------

    private static void RegisterAdapterTests()
    {
        Add("adapters: physical Ethernet and Wi-Fi are usable", () =>
        {
            True(DnsCoordinator.IsUsable(TestData.Adapter(alias: "Ethernet")));
            True(DnsCoordinator.IsUsable(TestData.Adapter(alias: "Wi-Fi", description: "Intel Wi-Fi 6")));
        });

        Add("adapters: inactive or virtual adapters are excluded", () =>
        {
            False(DnsCoordinator.IsUsable(TestData.Adapter(isUp: false)));
            False(DnsCoordinator.IsUsable(TestData.Adapter(isPhysical: false)));
        });

        Add("adapters: known virtual/VPN names are excluded", () =>
        {
            foreach (var name in new[]
                     {
                         "Hyper-V Virtual Ethernet Adapter",
                         "vEthernet (Default Switch)",
                         "VMware Network Adapter VMnet8",
                         "VirtualBox Host-Only Network",
                         "WSL",
                         "Loopback Pseudo-Interface 1",
                         "WinDivert",
                         "TAP-Windows Adapter V9",
                         "WireGuard Tunnel",
                         "Tailscale",
                         "ZeroTier One",
                         "OpenVPN TAP",
                         "Bluetooth Network Connection",
                         "Docker Desktop",
                         "Npcap Loopback Adapter"
                     })
            {
                False(DnsCoordinator.IsUsable(TestData.Adapter(alias: name, description: name)),
                    "should exclude: " + name);
            }
        });

        AddAsync("adapters: default route and metric determine the active adapter order", async () =>
        {
            var platform = new FakePlatform();
            platform.Adapters.Add(TestData.Adapter(alias: "Ethernet", hasDefaultRoute: false, routeMetric: 9999, index: 1));
            platform.Adapters.Add(TestData.Adapter(alias: "Wi-Fi", hasDefaultRoute: true, routeMetric: 50, index: 2));
            platform.Adapters.Add(TestData.Adapter(alias: "VPN", hasDefaultRoute: true, routeMetric: 0, index: 3));
            platform.Adapters.Add(TestData.Adapter(alias: "USB Ethernet", hasDefaultRoute: true, routeMetric: 10, index: 4));

            var coordinator = new DnsCoordinator(platform, new FakeBackupStore());
            var adapters = await coordinator.GetUsableAdaptersAsync();

            Equal(3, adapters.Count);
            Equal("USB Ethernet", adapters[0].Alias);
            Equal("Wi-Fi", adapters[1].Alias);
            Equal("Ethernet", adapters[2].Alias);
        });

        Add("selector: Wi-Fi only is detected as Wi-Fi", () =>
        {
            var wifi = TestData.Adapter(alias: "Wi-Fi", isWireless: true, routeMetric: 25, routeCost: 45);
            var ethernet = TestData.Adapter(alias: "Ethernet", hasDefaultRoute: false, routeMetric: 9000);

            var active = DnsAdapterSelector.FindActive(new[] { ethernet, wifi }, out var ambiguous);

            False(ambiguous);
            Equal("Wi-Fi", active!.NetworkKind);
            Equal("Wi-Fi", active.DisplayName);
        });

        Add("selector: Ethernet only is detected as Ethernet", () =>
        {
            var wifi = TestData.Adapter(alias: "Wi-Fi", isWireless: true, hasDefaultRoute: false, routeMetric: 9000);
            var ethernet = TestData.Adapter(alias: "Ethernet", routeMetric: 10, routeCost: 30);

            var active = DnsAdapterSelector.FindActive(new[] { wifi, ethernet }, out var ambiguous);

            False(ambiguous);
            Equal("Ethernet", active!.NetworkKind);
        });

        Add("selector: combined IPv4/IPv6 route cost wins over a lower raw metric", () =>
        {
            // Wi-Fi wins on raw route metric but loses once the interface metric
            // is added, which is exactly how Windows chooses its default route.
            var wifi = TestData.Adapter(alias: "Wi-Fi", isWireless: true, routeMetric: 5, routeCost: 60);
            var ethernet = TestData.Adapter(alias: "Ethernet", routeMetric: 20, routeCost: 25);

            var active = DnsAdapterSelector.FindActive(new[] { wifi, ethernet }, out var ambiguous);

            False(ambiguous);
            Equal("Ethernet", active!.DisplayName);
        });

        Add("selector: equal best routes are genuinely ambiguous", () =>
        {
            var wifi = TestData.Adapter(alias: "Wi-Fi", isWireless: true, routeCost: 40, index: 2);
            var ethernet = TestData.Adapter(alias: "Ethernet", routeCost: 40, index: 3);

            var active = DnsAdapterSelector.FindActive(new[] { wifi, ethernet }, out var ambiguous);

            True(ambiguous);
            True(active is not null);
        });

        Add("selector: switching Wi-Fi to Ethernet follows the live default route", () =>
        {
            var wifiOn = TestData.Adapter(alias: "Wi-Fi", isWireless: true, routeCost: 20, index: 2);
            var ethernetOff = TestData.Adapter(alias: "Ethernet", hasDefaultRoute: false, routeCost: 999999, index: 3);

            var first = DnsAdapterSelector.FindActive(new[] { wifiOn, ethernetOff }, out _);
            Equal("Wi-Fi", first!.NetworkKind);

            var wifiOff = TestData.Adapter(alias: "Wi-Fi", isWireless: true, hasDefaultRoute: false, routeCost: 999999, index: 2);
            var ethernetOn = TestData.Adapter(alias: "Ethernet", routeCost: 20, index: 3);

            var second = DnsAdapterSelector.FindActive(new[] { wifiOff, ethernetOn }, out _);
            Equal("Ethernet", second!.NetworkKind);
        });

        Add("selector: no physical default route is a warning, not a silent choice", () =>
        {
            var ethernet = TestData.Adapter(alias: "Ethernet", hasDefaultRoute: false, routeMetric: 999999);
            var wifi = TestData.Adapter(alias: "Wi-Fi", isWireless: true, hasDefaultRoute: false, routeMetric: 999999);

            var active = DnsAdapterSelector.FindActive(new[] { ethernet, wifi }, out var ambiguous);

            True(active is not null);
            True(ambiguous);
            True(!active!.HasDefaultRoute);
        });

        Add("selector: a stale adapter identifier is never reused", () =>
        {
            var requested = TestData.Adapter(alias: "Wi-Fi", isWireless: true, guid: "{AAAAAAAA-1111-2222-3333-444444444444}", index: 8);

            Equal(null, DnsAdapterSelector.FindLiveMatch(requested, Array.Empty<DnsAdapterInfo>()));

            var changedIndex = TestData.Adapter(
                alias: "Wi-Fi",
                isWireless: true,
                guid: "{AAAAAAAA-1111-2222-3333-444444444444}",
                index: 9);
            Equal(null, DnsAdapterSelector.FindLiveMatch(requested, new[] { changedIndex }));

            var down = TestData.Adapter(
                alias: "Wi-Fi",
                isWireless: true,
                guid: "{AAAAAAAA-1111-2222-3333-444444444444}",
                index: 8,
                isUp: false);
            Equal(null, DnsAdapterSelector.FindLiveMatch(requested, new[] { down }));

            var valid = TestData.Adapter(
                alias: "Wi-Fi",
                isWireless: true,
                guid: "aaaaaaaa-1111-2222-3333-444444444444",
                index: 8);
            Equal(8u, DnsAdapterSelector.FindLiveMatch(requested, new[] { valid })!.InterfaceIndex);
        });
    }

    // ---------------------------------------------------------------------
    // Fixed PowerShell script / JSON contract
    // ---------------------------------------------------------------------

    private static void RegisterScriptTests()
    {
        Add("script: is shipped as UTF-8 with BOM for PowerShell 5.1", () =>
        {
            var path = ScriptPath();
            True(File.Exists(path), "missing script: " + path);
            var bytes = File.ReadAllBytes(path);
            True(bytes.Length > 3);
            Equal(0xEF, (int)bytes[0]);
            Equal(0xBB, (int)bytes[1]);
            Equal(0xBF, (int)bytes[2]);
        });

        Add("script: takes only a JSON request file and never evals input", () =>
        {
            var content = File.ReadAllText(ScriptPath(), Encoding.UTF8);
            Contains("$RequestPath", content);
            True(Regex.IsMatch(content, @"\[Parameter\(Mandatory\s*=\s*\$true\)\]", RegexOptions.IgnoreCase));
            False(Regex.IsMatch(content, @"\bInvoke-Expression\b|\biex\b", RegexOptions.IgnoreCase));
            False(Regex.IsMatch(content, @"\bAdd-Type\b|\bScriptBlock\b", RegexOptions.IgnoreCase));
        });

        Add("script: implements every required action", () =>
        {
            var content = File.ReadAllText(ScriptPath(), Encoding.UTF8);
            foreach (var action in new[] { "capabilities", "list", "snapshot", "apply", "restore", "verify" })
            {
                Contains("'" + action + "'", content);
            }
        });

        Add("script: uses the adapter InterfaceType property", () =>
        {
            var content = File.ReadAllText(ScriptPath(), Encoding.UTF8);
            Contains("InterfaceType", content);
        });

        Add("script: never changes IP, route, proxy or firewall configuration", () =>
        {
            var content = File.ReadAllText(ScriptPath(), Encoding.UTF8);
            foreach (var forbidden in new[]
                     {
                         "Set-NetIPAddress",
                         "Set-NetIPInterface",
                         "New-NetRoute",
                         "Set-NetRoute",
                         "Remove-NetRoute",
                         "Set-NetFirewall",
                         "New-NetFirewall",
                         "proxycfg"
                     })
            {
                False(content.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    "forbidden operation: " + forbidden);
            }
        });

        Add("script: uses netsh only for per-family DNS servers", () =>
        {
            // Set-DnsClientServerAddress cannot switch one address family to DHCP
            // while the other keeps static servers, so DNS configuration uses
            // netsh. It must never touch addresses, routes, firewall or WinHTTP.
            var content = File.ReadAllText(ScriptPath(), Encoding.UTF8);
            foreach (var forbidden in new[]
                     {
                         "set address",
                         "add address",
                         "delete address",
                         "set route",
                         "add route",
                         "delete route",
                         "set firewall",
                         "add firewall",
                         "delete firewall",
                         "winhttp",
                         "winsock",
                         "proxycfg"
                     })
            {
                False(content.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    "forbidden network operation: " + forbidden);
            }

            Contains("'dnsservers'", content);
        });

        Add("script: configures native DoH only through Windows cmdlets", () =>
        {
            var content = File.ReadAllText(ScriptPath(), Encoding.UTF8);
            Contains("Add-DnsClientDohServerAddress", content);
            Contains("Set-DnsClientDohServerAddress", content);
            Contains("Remove-DnsClientDohServerAddress", content);
            Contains("Clear-DnsClientCache", content);
            Contains("Resolve-DnsName", content);
            False(content.Contains("dnscrypt", StringComparison.OrdinalIgnoreCase));
            False(content.Contains("DoT", StringComparison.Ordinal));
        });

        Add("json: apply request keeps arrays even with one element", () =>
        {
            var request = new DnsApplyRequest(
                TestData.Guid,
                TestData.Index,
                "fingerprint",
                new[] { "1.1.1.1" },
                Array.Empty<string>(),
                UseDoh: true,
                "https://cloudflare-dns.com/dns-query",
                new[] { "1.1.1.1" },
                new[]
                {
                    new DnsDohChange("1.1.1.1", false, null, false, true, "https://cloudflare-dns.com/dns-query", true, false)
                });

            using var document = JsonDocument.Parse(ScriptDnsPlatform.SerializeApplyRequest(request));
            var root = document.RootElement;
            Equal("apply", root.GetProperty("action").GetString());
            Equal(TestData.Guid, root.GetProperty("interfaceGuid").GetString());
            Equal(1, root.GetProperty("ipv4").GetArrayLength());
            Equal(1, root.GetProperty("dohAddresses").GetArrayLength());
            Equal(1, root.GetProperty("previousDohChanges").GetArrayLength());
            True(root.GetProperty("useDoh").GetBoolean());
        });

        Add("json: restore request keeps per-family static/DHCP state", () =>
        {
            var request = new DnsRestoreRequest(
                TestData.Guid,
                TestData.Index,
                "fingerprint",
                DnsFamilyState.Static(new[] { "208.67.222.222" }),
                DnsFamilyState.Automatic(new[] { "fd00:db80::1" }),
                Array.Empty<DnsDohChange>());

            using var document = JsonDocument.Parse(ScriptDnsPlatform.SerializeRestoreRequest(request));
            var root = document.RootElement;
            Equal("restore", root.GetProperty("action").GetString());
            True(root.GetProperty("originalIpv4").GetProperty("isStatic").GetBoolean());
            Equal(1, root.GetProperty("originalIpv4").GetProperty("serverAddresses").GetArrayLength());
            False(root.GetProperty("originalIpv6").GetProperty("isStatic").GetBoolean());
        });

        Add("json: parses a successful apply result", () =>
        {
            const string json = """
                {"ok":true,"code":"OK","message":"Đã đổi DNS thành công.","snapshot":{"interfaceGuid":"{D3E33B3A-2D1C-498D-B5F3-65AE1B6F6BD8}","interfaceIndex":17,"alias":"Ethernet","ipv4":{"isStatic":true,"serverAddresses":["1.1.1.1"]},"ipv6":{"isStatic":false,"serverAddresses":[]},"fingerprint":"fp"},"dohChanges":[{"serverAddress":"1.1.1.1","existed":false,"dohTemplate":null,"autoUpgrade":false,"allowFallbackToUdp":true,"appliedDohTemplate":"https://cloudflare-dns.com/dns-query","appliedAutoUpgrade":true,"appliedAllowFallbackToUdp":false}],"capabilities":null,"verification":"ok","rollback":""}
                """;

            var result = ScriptDnsPlatform.ParseApplyResult(json);
            True(result.Success);
            Equal("OK", result.Code);
            Equal(1, result.DohChanges.Count);
            Equal("https://cloudflare-dns.com/dns-query", result.DohChanges[0].AppliedDohTemplate);
            Equal("1.1.1.1", result.Snapshot!.Ipv4.ServerAddresses[0]);
            Equal("ok", result.VerificationDetail);
        });

        Add("json: parses an apply failure with rollback detail", () =>
        {
            const string json = """
                {"ok":false,"code":"APPLY_FAILED","message":"Không thể áp dụng DNS.","snapshot":null,"dohChanges":null,"capabilities":null,"verification":"","rollback":"Đã khôi phục cấu hình DNS trước đó."}
                """;

            var result = ScriptDnsPlatform.ParseApplyResult(json);
            False(result.Success);
            Equal("APPLY_FAILED", result.Code);
            Equal("Đã khôi phục cấu hình DNS trước đó.", result.RollbackDetail);
        });

        Add("json: parses an adapter list and capabilities", () =>
        {
            const string listJson = """
                {"ok":true,"code":"OK","message":"ok","adapters":[{"interfaceGuid":"{D3E33B3A-2D1C-498D-B5F3-65AE1B6F6BD8}","interfaceIndex":17,"alias":"Ethernet","description":"Realtek","isUp":true,"isPhysical":true,"hasDefaultRoute":true,"routeMetric":0}],"snapshot":null,"dohChanges":null,"capabilities":null,"verification":"","rollback":""}
                """;
            const string capsJson = """
                {"ok":true,"code":"OK","message":"ok","adapters":null,"snapshot":null,"dohChanges":null,"capabilities":{"nativeDohSupported":true,"windowsBuild":26300,"detail":"yes"},"verification":"","rollback":""}
                """;

            var adapters = ScriptDnsPlatform.ParseAdapterList(listJson);
            Equal(1, adapters.Count);
            Equal("Ethernet", adapters[0].Alias);
            Equal(17u, adapters[0].InterfaceIndex);

            var capabilities = ScriptDnsPlatform.ParseCapabilities(capsJson);
            True(capabilities.NativeDohSupported);
            Equal(26300, capabilities.WindowsBuild);
        });

        Add("json: parses a snapshot", () =>
        {
            const string json = """
                {"ok":true,"code":"OK","message":"ok","snapshot":{"interfaceGuid":"{D3E33B3A-2D1C-498D-B5F3-65AE1B6F6BD8}","interfaceIndex":17,"alias":"Ethernet","ipv4":{"isStatic":true,"serverAddresses":["208.67.222.222"]},"ipv6":{"isStatic":false,"serverAddresses":["fd00:db80::1"]},"fingerprint":"fp"},"adapters":null,"dohChanges":null,"capabilities":null,"verification":"","rollback":""}
                """;

            var snapshot = ScriptDnsPlatform.ParseSnapshot(json);
            True(snapshot.Ipv4.IsStatic);
            Equal("208.67.222.222", snapshot.Ipv4.ServerAddresses[0]);
            False(snapshot.Ipv6.IsStatic);
            Equal("fp", snapshot.Fingerprint);
        });
    }

    // ---------------------------------------------------------------------
    // Engine definition and launch arguments
    // ---------------------------------------------------------------------

    private static void RegisterEngineTests()
    {
        Add("engine: exact verified reference arguments", () =>
        {
            Equal(
                "-5 --dns-addr 1.1.1.1 --dns-port 53 --dnsv6-addr 2606:4700:4700::1111 --dnsv6-port 53",
                EngineDefinition.Arguments);
            Equal("0.2.3rc3", EngineDefinition.EngineVersion);
            Equal("2.2", EngineDefinition.WinDivertVersion);
        });

        Add("engine: DNS address precedes its port for both families", () =>
        {
            var tokens = EngineDefinition.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var ipv4Addr = Array.IndexOf(tokens, "--dns-addr");
            var ipv4Port = Array.IndexOf(tokens, "--dns-port");
            var ipv6Addr = Array.IndexOf(tokens, "--dnsv6-addr");
            var ipv6Port = Array.IndexOf(tokens, "--dnsv6-port");

            True(ipv4Addr >= 0 && ipv4Port > ipv4Addr, "--dns-addr must precede --dns-port");
            True(ipv6Addr >= 0 && ipv6Port > ipv6Addr, "--dnsv6-addr must precede --dnsv6-port");
            Equal("1.1.1.1", tokens[ipv4Addr + 1]);
            Equal("53", tokens[ipv4Port + 1]);
            Equal("2606:4700:4700::1111", tokens[ipv6Addr + 1]);
            Equal("53", tokens[ipv6Port + 1]);
            Equal(1, tokens.Count(token => token == "--dns-addr"));
            Equal(1, tokens.Count(token => token == "--dnsv6-addr"));
        });

        Add("engine: required files are the unmodified upstream x64 set", () =>
        {
            Equal(3, EngineDefinition.RequiredFiles.Count);
            True(EngineDefinition.RequiredFiles.Contains("goodbyedpi.exe"));
            True(EngineDefinition.RequiredFiles.Contains("WinDivert.dll"));
            True(EngineDefinition.RequiredFiles.Contains("WinDivert64.sys"));
            Equal("Engine", EngineDefinition.EngineFolderName);
        });

        Add("engine: missing files are reported without touching the disk", () =>
        {
            using var temp = new TempDirectory();
            var validation = EngineDefinition.Validate(temp.Path);
            False(validation.IsValid);
            Equal(3, validation.MissingFiles.Count);
        });
    }

    // ---------------------------------------------------------------------
    // Steam reachability probe (no real network access)
    // ---------------------------------------------------------------------

    private static void RegisterSteamProbeTests()
    {
        Add("steam: probes exactly the three required public endpoints", () =>
        {
            Equal(3, SteamProbe.DefaultEndpoints.Count);
            Equal("Steam Store", SteamProbe.DefaultEndpoints[0].Name);
            Equal("https://store.steampowered.com/", SteamProbe.DefaultEndpoints[0].Url);
            Equal("Steam Login", SteamProbe.DefaultEndpoints[1].Name);
            Equal(
                "https://store.steampowered.com/login/?redir=account%2F&redir_ssl=1",
                SteamProbe.DefaultEndpoints[1].Url);
            Equal("Steam Community", SteamProbe.DefaultEndpoints[2].Name);
            Equal("https://steamcommunity.com/login/home/", SteamProbe.DefaultEndpoints[2].Url);
        });

        Add("steam: a 200 response is reachable", () =>
        {
            using var handler = new StubHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
            using var probe = new SteamProbe(handler);

            var result = probe.RunAsync(new[] { SteamProbe.DefaultEndpoints[0] })
                .GetAwaiter().GetResult();

            True(result.Reachable);
            False(result.Degraded);
            Equal(SteamProbeOutcome.Reachable, result.Endpoints[0].Outcome);
            Equal(200, result.Endpoints[0].StatusCode);
        });

        Add("steam: 403 and 429 are responses, not DPI blocking", () =>
        {
            using var forbidden = new StubHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
            using var probe = new SteamProbe(forbidden);
            var forbiddenResult = probe.RunAsync(new[] { SteamProbe.DefaultEndpoints[0] })
                .GetAwaiter().GetResult();
            True(forbiddenResult.Reachable);
            Equal(SteamProbeOutcome.Reachable, forbiddenResult.Endpoints[0].Outcome);

            using var throttled = new StubHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage((HttpStatusCode)429)));
            using var throttleProbe = new SteamProbe(throttled);
            var throttledResult = throttleProbe.RunAsync(new[] { SteamProbe.DefaultEndpoints[0] })
                .GetAwaiter().GetResult();
            True(throttledResult.Reachable);
            Equal(429, throttledResult.Endpoints[0].StatusCode);
        });

        Add("steam: 5xx is reachable but flagged degraded", () =>
        {
            using var handler = new StubHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
            using var probe = new SteamProbe(handler);

            var result = probe.RunAsync(new[] { SteamProbe.DefaultEndpoints[0] })
                .GetAwaiter().GetResult();

            True(result.Reachable);
            True(result.Degraded);
            Equal(SteamProbeOutcome.ServerError, result.Endpoints[0].Outcome);
        });

        Add("steam: a timeout is not reachable", () =>
        {
            using var handler = new StubHandler(async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var probe = new SteamProbe(handler, TimeSpan.FromMilliseconds(80));

            var result = probe.RunAsync(new[] { SteamProbe.DefaultEndpoints[0] })
                .GetAwaiter().GetResult();

            False(result.Reachable);
            Equal(SteamProbeOutcome.Timeout, result.Endpoints[0].Outcome);
        });

        Add("steam: TLS and DNS failures are classified, not reachable", () =>
        {
            using var tls = new StubHandler((_, _) =>
                Task.FromException<HttpResponseMessage>(
                    new HttpRequestException(HttpRequestError.SecureConnectionError, "tls")));
            using var tlsProbe = new SteamProbe(tls);
            var tlsResult = tlsProbe.RunAsync(new[] { SteamProbe.DefaultEndpoints[0] })
                .GetAwaiter().GetResult();
            False(tlsResult.Reachable);
            Equal(SteamProbeOutcome.TlsFailure, tlsResult.Endpoints[0].Outcome);

            using var dns = new StubHandler((_, _) =>
                Task.FromException<HttpResponseMessage>(
                    new HttpRequestException(HttpRequestError.NameResolutionError, "dns")));
            using var dnsProbe = new SteamProbe(dns);
            var dnsResult = dnsProbe.RunAsync(new[] { SteamProbe.DefaultEndpoints[0] })
                .GetAwaiter().GetResult();
            False(dnsResult.Reachable);
            Equal(SteamProbeOutcome.DnsFailure, dnsResult.Endpoints[0].Outcome);
        });

        Add("steam: probe sends anonymous GET requests only", () =>
        {
            var requests = new List<HttpRequestMessage>();
            using var handler = new StubHandler((request, _) =>
            {
                lock (requests)
                {
                    requests.Add(request);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });
            using var probe = new SteamProbe(handler);

            probe.RunAsync().GetAwaiter().GetResult();

            Equal(3, requests.Count);
            foreach (var request in requests)
            {
                Equal(HttpMethod.Get, request.Method);
                True(request.RequestUri is not null);
                True(request.Headers.Authorization is null, "probe must not send credentials");
                False(
                    request.Headers.TryGetValues("Cookie", out var cookies) && cookies.Any(),
                    "probe must not send cookies");
                True(request.Content is null, "probe must not send a body");
            }
        });

        Add("steam: status labels match the product specification", () =>
        {
            Equal("ĐANG KẾT NỐI...", ConnectionStatusText.Connecting);
            Equal("ĐÃ KẾT NỐI", ConnectionStatusText.EngineRunning);
            Equal("ĐÃ KẾT NỐI", ConnectionStatusText.Connected);
            Equal("ĐÃ KẾT NỐI", ConnectionStatusText.SteamUnresponsive);
            Equal("KHÔNG THỂ KẾT NỐI", ConnectionStatusText.Failed);
            Equal("ĐANG HỦY KẾT NỐI...", ConnectionStatusText.Disconnecting);
            Equal("CHƯA KẾT NỐI", ConnectionStatusText.Idle);
            Equal(ConnectionStatusText.Idle, ConnectionStatusText.Disconnected);
        });
    }

    // ---------------------------------------------------------------------
    // UI contract, tray and typography
    // ---------------------------------------------------------------------

    private static void RegisterUiContractTests()
    {
        Add("ui: main window is compact and never scrolls", () =>
        {
            var xaml = ReadSource("src", "BeeDPI.App", "MainWindow.xaml");
            False(xaml.Contains("ScrollViewer", StringComparison.Ordinal), "main window must not scroll");
            Contains("Width=\"424\"", xaml);
            Contains("MinHeight=\"430\"", xaml);
            Contains("QUẢN LÝ DNS", xaml);
            Contains("AutomationProperties.AutomationId=\"ConnectButton\"", xaml);
            False(xaml.Contains("BEE STUDIO", StringComparison.Ordinal));
            False(xaml.Contains("A BirenDev Project", StringComparison.Ordinal));
            False(xaml.Contains("StatusDetail", StringComparison.Ordinal), "diagnostics must stay in the log");
        });

        Add("ui: DNS window shows only the required Vietnamese fields", () =>
        {
            var xaml = ReadSource("src", "BeeDPI.App", "DnsWindow.xaml");
            foreach (var label in new[]
                     {
                         "MẠNG ĐANG SỬ DỤNG",
                         "NHÀ CUNG CẤP DNS",
                         "CHẾ ĐỘ DNS",
                         "ÁP DỤNG DNS",
                         "KHÔI PHỤC DNS BAN ĐẦU",
                         "Tùy chọn nâng cao"
                     })
            {
                Contains(label, xaml);
            }

            Contains("ScrollViewer", xaml);
            Contains("VerticalScrollBarVisibility=\"Auto\"", xaml);
            False(xaml.Contains("CurrentDnsSummary", StringComparison.Ordinal));
            False(xaml.Contains("BackupSummary", StringComparison.Ordinal));
            False(Regex.IsMatch(xaml, @"\b\d{1,3}(\.\d{1,3}){3}\b"), "no raw DNS addresses in the window");

            // No description text below the profile dropdown
            False(Regex.IsMatch(xaml, @"DNS công cộng", RegexOptions.IgnoreCase), "no provider description in DNS window");
            False(xaml.Contains("SelectedProfile.Description", StringComparison.Ordinal), "no profile description binding");
        });

        Add("ui: apply DNS button uses black text on yellow background", () =>
        {
            var theme = ReadSource("src", "BeeDPI.App", "Themes", "Theme.xaml");
            Contains("#0B0C10", theme, "apply button foreground must be black");
            Contains("#FFC928", theme, "apply button background must be yellow");
            Contains("DnsPrimaryButtonStyle", theme, "apply button style must exist");
            Contains("Value=\"Bold\"", theme, "apply button must be bold");
        });

        Add("ui: no outer white border on main window", () =>
        {
            var xaml = ReadSource("src", "BeeDPI.App", "MainWindow.xaml");
            var frame = Regex.Match(xaml, @"<Border x:Name=""WindowFrame""[^>]*>");
            True(frame.Success, "could not find WindowFrame element");
            False(frame.Value.Contains("BorderThickness"), "main window frame must not have a visible border thickness");
            False(frame.Value.Contains("BorderBrush"), "main window frame must not have a BorderBrush");
            Contains("CornerRadius=\"22\"", xaml, "main window frame must be rounded");
        });

        Add("ui: no outer white border on DNS window", () =>
        {
            var xaml = ReadSource("src", "BeeDPI.App", "DnsWindow.xaml");
            var frame = Regex.Match(xaml, @"<Border x:Name=""WindowFrame""[^>]*>");
            True(frame.Success, "could not find WindowFrame element");
            False(frame.Value.Contains("BorderThickness"), "DNS window frame must not have a visible border thickness");
            False(frame.Value.Contains("BorderBrush"), "DNS window frame must not have a BorderBrush");
            Contains("CornerRadius=\"22\"", xaml, "DNS window frame must be rounded");
        });

        Add("ui: master logo used in all locations", () =>
        {
            var main = ReadSource("src", "BeeDPI.App", "MainWindow.xaml");
            Contains("BeeMarkImage", main, "main window must use BeeMarkImage");
            var dns = ReadSource("src", "BeeDPI.App", "DnsWindow.xaml");
            Contains("BeeMarkImage", dns, "DNS window must use BeeMarkImage");
        });

        Add("ui: no inline logo canvas artifacts", () =>
        {
            var main = ReadSource("src", "BeeDPI.App", "MainWindow.xaml");
            False(Regex.IsMatch(main, @"HeroGlowBrush", RegexOptions.IgnoreCase), "no glow ellipse in main window logo area");
            var dns = ReadSource("src", "BeeDPI.App", "DnsWindow.xaml");
            False(Regex.IsMatch(dns, @"HeroGlowBrush", RegexOptions.IgnoreCase), "no glow ellipse in DNS window");
        });

        Add("ui: no technical identifiers leak into the UI", () =>
        {
            foreach (var file in new[] { "MainWindow.xaml", "DnsWindow.xaml", "Themes\\Theme.xaml" })
            {
                var text = ReadSource("src", "BeeDPI.App", file);
                False(Regex.IsMatch(text, @"BeeDPI\.Core|Win32Exception|System\.Exception|PowerShell|GoodbyeDPI|WinDivert"),
                    "technical identifier in " + file);
            }
        });

        Add("ui: palette tokens match the design specification", () =>
        {
            var theme = ReadSource("src", "BeeDPI.App", "Themes", "Theme.xaml");
            foreach (var color in new[]
                     {
                         "#090A0D", "#13151B", "#1A1D24", "#FFC928", "#FFD951",
                         "#48DDF4", "#F5F5F5", "#9298A5", "#292D36"
                     })
            {
                Contains(color, theme);
            }

            Contains("/Fonts/#Be Vietnam Pro", theme);
        });

        Add("brand: social links and version are exact", () =>
        {
            Equal("https://www.facebook.com/vthienbhat/", Branding.FacebookUrl);
            Equal("https://www.tiktok.com/@nthienbhat", Branding.TikTokUrl);
            Equal("https://discord.com/users/977745837176266813", Branding.DiscordUrl);
            Equal("discord.com/vthienbhat", Branding.DiscordLabel);
            Equal("1.2.1", Branding.ProductVersion);
            Equal("BEE DPI", Branding.ProductName);
            Equal("BirenDev", Branding.Developer);
        });

        Add("tray: menu, balloon and exit labels are present", () =>
        {
            var tray = ReadSource("src", "BeeDPI.App", "Services", "TrayIcon.cs");
            Contains("Mở BEE DPI", tray);
            Contains("Thoát BEE DPI", tray);
            Contains("BEE DPI vẫn đang chạy trong khay hệ thống.", tray);
            Contains("Shell_NotifyIcon", tray);

            var app = ReadSource("src", "BeeDPI.App", "App.xaml.cs");
            Contains("RequestExit", app);
            Contains("_tray?.Dispose()", app);
        });

        Add("safety: job object, parent watchdog and UAC stay in place", () =>
        {
            var job = ReadSource("src", "BeeDPI.EngineHost", "Native", "JobObject.cs");
            Contains("JobObjectLimitKillOnJobClose", job);

            var watchdog = ReadSource("src", "BeeDPI.EngineHost", "ParentWatchdog.cs");
            Contains("WaitForExit", watchdog);

            var launcher = ReadSource("src", "BeeDPI.App", "Services", "HostLauncher.cs");
            Contains("runas", launcher);

            var manifest = ReadSource("src", "BeeDPI.App", "app.manifest");
            Contains("level=\"asInvoker\"", manifest);
            Contains("PerMonitorV2", manifest);
        });

        Add("font: bundled Be Vietnam Pro covers Vietnamese diacritics", () =>
        {
            var regular = Path.Combine(
                RepoRoot(),
                "src",
                "BeeDPI.App",
                "Fonts",
                "BeVietnamPro-Regular.ttf");
            True(File.Exists(regular), "missing bundled font: " + regular);

            var glyphs = new GlyphTypeface(new Uri(regular));
            foreach (var character in "ếộữĐđầẤỹợƯơăâêô")
            {
                True(
                    glyphs.CharacterToGlyphMap.ContainsKey(character),
                    "font is missing glyph U+" + ((int)character).ToString("X4"));
            }

            True(File.Exists(Path.Combine(
                RepoRoot(),
                "licenses",
                "LICENSE-BeVietnamPro-OFL.txt")), "missing OFL notice");
        });

        Add("catalog: provider names are friendly", () =>
        {
            Equal("Cloudflare", DnsCatalog.Default.Providers.First(p => p.Id == "cloudflare").DisplayName);
            Equal("Google", DnsCatalog.Default.Providers.First(p => p.Id == "google").DisplayName);
            Equal("Control D", DnsCatalog.Default.Providers.First(p => p.Id == "controld").DisplayName);
            Equal("CleanBrowsing", DnsCatalog.Default.Providers.First(p => p.Id == "cleanbrowsing").DisplayName);
            Equal("Tùy chỉnh", new DnsProvider { Id = "custom", Name = "Custom" }.DisplayName);
        });

        Add("catalog: profile names are friendly", () =>
        {
            var cloudflare = DnsCatalog.Default.Providers.First(p => p.Id == "cloudflare");
            Equal("Tiêu chuẩn", cloudflare.Profiles.First(p => p.Id == "cloudflare-standard").ProfileDisplayName);
            Equal("Chặn mã độc", cloudflare.Profiles.First(p => p.Id == "cloudflare-malware").ProfileDisplayName);
            Equal("Chặn mã độc và nội dung người lớn", cloudflare.Profiles.First(p => p.Id == "cloudflare-family").ProfileDisplayName);

            var quad9 = DnsCatalog.Default.Providers.First(p => p.Id == "quad9");
            Equal("Bảo vệ mã độc", quad9.Profiles.First(p => p.Id == "quad9-secure").ProfileDisplayName);
            Equal("Không lọc", quad9.Profiles.First(p => p.Id == "quad9-unsecured").ProfileDisplayName);

            var adguard = DnsCatalog.Default.Providers.First(p => p.Id == "adguard");
            Equal("Chặn quảng cáo", adguard.Profiles.First(p => p.Id == "adguard-default").ProfileDisplayName);
            Equal("Không lọc", adguard.Profiles.First(p => p.Id == "adguard-unfiltered").ProfileDisplayName);
            Equal("Bảo vệ gia đình", adguard.Profiles.First(p => p.Id == "adguard-family").ProfileDisplayName);
        });

        Add("catalog: no internal namespace strings in profile names", () =>
        {
            var catalog = DnsCatalog.Default;
            foreach (var provider in catalog.Providers)
            {
                False(provider.DisplayName.Contains("BeeDPI.Core", StringComparison.Ordinal));
                False(provider.DisplayName.Contains("DnsProvider", StringComparison.Ordinal));
                foreach (var profile in provider.Profiles)
                {
                    False(profile.ProfileDisplayName.Contains("BeeDPI.Core", StringComparison.Ordinal));
                    False(profile.ProfileDisplayName.Contains("DnsProfile", StringComparison.Ordinal));
                }
            }
        });

        Add("catalog: no provider descriptions exposed as display names", () =>
        {
            var catalog = DnsCatalog.Default;
            foreach (var provider in catalog.Providers)
            {
                foreach (var profile in provider.Profiles)
                {
                    False(profile.ProfileDisplayName.Contains("DNS công cộng", StringComparison.Ordinal));
                    False(profile.ProfileDisplayName.Contains("không lọc nội dung", StringComparison.Ordinal));
                    False(profile.ProfileDisplayName.Contains("mã hóa", StringComparison.Ordinal));
                }
            }
        });
    }

    private static void RegisterDnsUiTests()
    {
        AddAsync("dns ui: Wi-Fi is shown as the active network", async () =>
        {
            const string wifiGuid = "{11111111-1111-1111-1111-111111111111}";
            var platform = new FakePlatform();
            platform.Adapters.Add(TestData.Adapter(
                alias: "Ethernet",
                hasDefaultRoute: false,
                routeMetric: 9000,
                guid: "{22222222-2222-2222-2222-222222222222}",
                index: 2));
            platform.Adapters.Add(TestData.Adapter(
                alias: "Wi-Fi",
                isWireless: true,
                routeMetric: 10,
                routeCost: 25,
                guid: wifiGuid,
                index: 3));
            platform.Current = TestData.Snapshot(wifiGuid, 3);

            await using var viewModel = new DnsViewModel(platform, new FakeBackupStore());
            await viewModel.InitializeAsync();

            Equal("Wi-Fi", viewModel.NetworkType);
            False(viewModel.ShowAdapterSelector);
            Equal(wifiGuid, viewModel.SelectedAdapter!.InterfaceGuid);
            False(viewModel.HasStatus);
        });

        AddAsync("dns ui: equal routes ask the user instead of guessing", async () =>
        {
            const string ethernetGuid = "{33333333-3333-3333-3333-333333333333}";
            var platform = new FakePlatform();
            platform.Adapters.Add(TestData.Adapter(
                alias: "Wi-Fi",
                isWireless: true,
                routeCost: 40,
                guid: "{44444444-4444-4444-4444-444444444444}",
                index: 4));
            platform.Adapters.Add(TestData.Adapter(
                alias: "Ethernet",
                routeCost: 40,
                guid: ethernetGuid,
                index: 5));
            // DnsCoordinator orders by route cost then display name.
            platform.Current = TestData.Snapshot(ethernetGuid, 5);

            await using var viewModel = new DnsViewModel(platform, new FakeBackupStore());
            await viewModel.InitializeAsync();

            True(viewModel.ShowAdapterSelector);
            Equal("Ethernet", viewModel.SelectedAdapter!.DisplayName);
        });

        AddAsync("dns ui: a VPN-owned default route shows a warning", async () =>
        {
            var platform = new FakePlatform();
            platform.Adapters.Add(TestData.Adapter(
                alias: "Ethernet",
                hasDefaultRoute: false,
                routeMetric: 999999));
            platform.Current = TestData.Snapshot();

            await using var viewModel = new DnsViewModel(platform, new FakeBackupStore());
            await viewModel.InitializeAsync();

            True(viewModel.ShowNetworkWarning);
            Equal("Ethernet", viewModel.NetworkType);
        });

        AddAsync("dns ui: custom provider expands advanced options and applies", async () =>
        {
            var platform = new FakePlatform();
            platform.Adapters.Add(TestData.Adapter(alias: "Ethernet", routeCost: 15));
            platform.Current = TestData.Snapshot();
            var backups = new FakeBackupStore();

            await using var viewModel = new DnsViewModel(platform, backups);
            await viewModel.InitializeAsync();

            viewModel.SelectedProvider = viewModel.Providers.First(
                p => p.Id == DnsCatalog.CustomProviderId);

            True(viewModel.IsAdvancedOpen);
            True(viewModel.ShowCustomFields);
            False(viewModel.ShowProfileSelector);

            viewModel.CustomIpv4 = "1.1.1.1";
            viewModel.ApplyCommand.Execute(null);
            await viewModel.WaitForIdleAsync();

            Equal("ĐÃ ĐỔI DNS", viewModel.StatusTitle);
            True(viewModel.IsSuccess);
            True(viewModel.HasBackup);
            Equal(1, platform.ApplyCalls.Count);

            viewModel.RestoreCommand.Execute(null);
            await viewModel.WaitForIdleAsync();

            Equal("ĐÃ KHÔI PHỤC DNS", viewModel.StatusTitle);
            True(viewModel.IsSuccess);
        });

        AddAsync("dns ui: invalid custom address is rejected without touching the system", async () =>
        {
            var platform = new FakePlatform();
            platform.Adapters.Add(TestData.Adapter(alias: "Ethernet", routeCost: 15));
            platform.Current = TestData.Snapshot();

            await using var viewModel = new DnsViewModel(platform, new FakeBackupStore());
            await viewModel.InitializeAsync();

            viewModel.SelectedProvider = viewModel.Providers.First(
                p => p.Id == DnsCatalog.CustomProviderId);
            viewModel.CustomIpv4 = "999.1.1.1";
            viewModel.ApplyCommand.Execute(null);
            await viewModel.WaitForIdleAsync();

            Equal("KHÔNG THỂ ÁP DỤNG DNS", viewModel.StatusTitle);
            True(viewModel.IsError);
            Equal(0, platform.ApplyCalls.Count);
        });

        AddAsync("dns ui: switching provider refreshes profiles and selects default", async () =>
        {
            var platform = new FakePlatform();
            platform.Adapters.Add(TestData.Adapter(alias: "Ethernet", routeCost: 15));
            platform.Current = TestData.Snapshot();

            await using var viewModel = new DnsViewModel(platform, new FakeBackupStore());
            await viewModel.InitializeAsync();

            // Start with Cloudflare
            var cloudflare = viewModel.Providers.First(p => p.Id == "cloudflare");
            viewModel.SelectedProvider = cloudflare;
            await viewModel.WaitForIdleAsync();

            True(viewModel.ShowProfileSelector);
            True(viewModel.Profiles.Count > 0);
            NotNull(viewModel.SelectedProfile);
            Equal("Tiêu chuẩn", viewModel.SelectedProfile!.ProfileDisplayName);

            // Switch to Quad9
            var quad9 = viewModel.Providers.First(p => p.Id == "quad9");
            viewModel.SelectedProvider = quad9;
            await viewModel.WaitForIdleAsync();

            True(viewModel.Profiles.Count > 0);
            NotNull(viewModel.SelectedProfile);
            Equal("Bảo vệ mã độc", viewModel.SelectedProfile!.ProfileDisplayName);
        });

        AddAsync("dns ui: switching to custom hides profile selector and opens advanced", async () =>
        {
            var platform = new FakePlatform();
            platform.Adapters.Add(TestData.Adapter(alias: "Ethernet", routeCost: 15));
            platform.Current = TestData.Snapshot();

            await using var viewModel = new DnsViewModel(platform, new FakeBackupStore());
            await viewModel.InitializeAsync();

            viewModel.SelectedProvider = viewModel.Providers.First(
                p => p.Id == DnsCatalog.CustomProviderId);
            await viewModel.WaitForIdleAsync();

            True(viewModel.IsAdvancedOpen);
            True(viewModel.ShowCustomFields);
            False(viewModel.ShowProfileSelector);
            Null(viewModel.SelectedProfile);
        });

        AddAsync("dns ui: a disappeared adapter blocks apply with a short message", async () =>
        {
            var platform = new FakePlatform();
            platform.Adapters.Add(TestData.Adapter(alias: "Ethernet", routeCost: 15));
            platform.Current = TestData.Snapshot();

            await using var viewModel = new DnsViewModel(platform, new FakeBackupStore());
            await viewModel.InitializeAsync();

            platform.Adapters.Clear();
            viewModel.ApplyCommand.Execute(null);
            await viewModel.WaitForIdleAsync();

            Equal("KHÔNG THỂ XÁC ĐỊNH MẠNG", viewModel.StatusTitle);
            Equal(0, platform.ApplyCalls.Count);
        });
    }

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "BeeDPI.slnx")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new TestFailureException("could not locate the repository root");
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()), Encoding.UTF8);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
            _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => _responder(request, cancellationToken);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static DnsBackupDocument NewBackup(DnsFamilyState? ipv6 = null) => new()
    {
        AdapterGuid = TestData.Guid,
        InterfaceIndex = TestData.Index,
        AdapterAlias = "Ethernet",
        CreatedAt = DateTimeOffset.Now,
        Original = TestData.Snapshot(ipv6: ipv6),
        DohChanges = new List<DnsDohChange>()
    };

    private static string ScriptPath() =>
        Path.Combine(AppContext.BaseDirectory, "tools", "dns-manager.ps1");

    private static void True(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new TestFailureException(message ?? "expected true");
        }
    }

    private static void False(bool condition, string? message = null) => True(!condition, message ?? "expected false");

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new TestFailureException($"expected <{expected}> but got <{actual}>");
        }
    }

private static void Contains(string expected, string? actual, string? message = null)
        {
            if (actual is null || !actual.Contains(expected, StringComparison.Ordinal))
            {
                var preview = actual is null
                    ? "<null>"
                    : actual.Length <= 200 ? actual : actual[..200] + "…";
                throw new TestFailureException(message ?? $"expected text to contain <{expected}>, actual <{preview}>");
            }
        }

        private static void NotNull(object? actual, string? message = null)
        {
            if (actual is null)
            {
                throw new TestFailureException(message ?? "expected not null");
            }
        }

        private static void Null(object? actual, string? message = null)
        {
            if (actual is not null)
            {
                throw new TestFailureException(message ?? "expected null");
            }
        }

    private static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new TestFailureException($"expected {typeof(T).Name} but got {exception.GetType().Name}");
        }

        throw new TestFailureException($"expected {typeof(T).Name} but no exception was thrown");
    }

    private static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (T exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new TestFailureException($"expected {typeof(T).Name} but got {exception.GetType().Name}: {exception.Message}");
        }

        throw new TestFailureException($"expected {typeof(T).Name} but no exception was thrown");
    }

    private sealed class TestFailureException : Exception
    {
        public TestFailureException(string message)
            : base(message)
        {
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "beedpi-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best effort.
            }
        }
    }
}

internal static class FamilyStateExtensions
{
    public static string FingerprintKey(this DnsFamilyState state) =>
        (state.IsStatic ? "static:" : "auto:") + string.Join(",", state.ServerAddresses);
}
