using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeeDPI.Core.Dns;

/// <summary>A selectable DNS profile, e.g. "Cloudflare — Chặn mã độc".</summary>
public sealed class DnsProfile
{
    public string Id { get; init; } = string.Empty;

    public string ProviderId { get; init; } = string.Empty;

    public string ProviderName { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Filtering { get; init; } = string.Empty;

    public IReadOnlyList<string> Ipv4 { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Ipv6 { get; init; } = Array.Empty<string>();

    public string? DohTemplate { get; init; }

    public IReadOnlyList<DnsProtocol> Protocols { get; init; } = Array.Empty<DnsProtocol>();

    public bool SupportsDoh => !string.IsNullOrWhiteSpace(DohTemplate)
                               && Protocols.Contains(DnsProtocol.Doh);

    /// <summary>
    /// The only string the UI should show for a profile. It deliberately avoids
    /// the type name that WPF would otherwise display if a binding failed.
    /// </summary>
    public string ProfileDisplayName => Name;

    public string DisplayName => ProviderName + " — " + Name;

    /// <summary>Safety net for controls that fall back to ToString().</summary>
    public override string ToString() => ProfileDisplayName;

    public string AddressSummary
    {
        get
        {
            var parts = new List<string>();
            if (Ipv4.Count > 0)
            {
                parts.Add("IPv4: " + string.Join(", ", Ipv4));
            }

            if (Ipv6.Count > 0)
            {
                parts.Add("IPv6: " + string.Join(", ", Ipv6));
            }

            return string.Join("  ·  ", parts);
        }
    }
}

/// <summary>A DNS provider and its supported profiles.</summary>
public sealed class DnsProvider
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Short, friendly provider name for the UI. The catalog keeps the legal
    /// service name; the user only needs a recognisable label.
    /// </summary>
    public string DisplayName => Id.ToLowerInvariant() switch
    {
        "cloudflare" => "Cloudflare",
        "google" => "Google",
        "quad9" => "Quad9",
        "adguard" => "AdGuard",
        "opendns" => "OpenDNS",
        "cleanbrowsing" => "CleanBrowsing",
        "controld" => "Control D",
        "custom" => "Tùy chỉnh",
        _ => Name
    };

    public string Homepage { get; init; } = string.Empty;

    public string SourceUrl { get; init; } = string.Empty;

    public string LastVerified { get; init; } = string.Empty;

    public IReadOnlyList<DnsProfile> Profiles { get; init; } = Array.Empty<DnsProfile>();

    /// <summary>Safety net for controls that fall back to ToString().</summary>
    public override string ToString() => DisplayName;
}

/// <summary>
/// Data-driven DNS provider catalog. Adding a provider only requires editing
/// <c>DnsProviderCatalog.json</c>; no provider-specific code exists in the UI or
/// in the DNS engine.
/// </summary>
public sealed class DnsCatalog
{
    public const string CustomProviderId = "custom";
    public const string CustomProfileId = "custom";

    private static readonly Lazy<DnsCatalog> DefaultCatalog = new(LoadEmbedded, isThreadSafe: true);

    private DnsCatalog(IReadOnlyList<DnsProvider> providers, string lastVerified)
    {
        Providers = providers;
        LastVerified = lastVerified;
    }

    public IReadOnlyList<DnsProvider> Providers { get; }

    public string LastVerified { get; }

    public static DnsCatalog Default => DefaultCatalog.Value;

    public static DnsCatalog LoadEmbedded()
    {
        var assembly = typeof(DnsCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("DnsProviderCatalog.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new InvalidOperationException("Không tìm thấy danh mục DNS đi kèm.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException("Không đọc được danh mục DNS đi kèm.");
        using var reader = new StreamReader(stream);
        return LoadFromJson(reader.ReadToEnd());
    }

    public static DnsCatalog LoadFromJson(string json)
    {
        var document = JsonSerializer.Deserialize<CatalogDocument>(json, DnsJson.Options)
                       ?? throw new InvalidDataException("Danh mục DNS rỗng.");

        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException("Phiên bản danh mục DNS không được hỗ trợ.");
        }

        var providers = new List<DnsProvider>();
        var providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in document.Providers ?? new List<ProviderDocument>())
        {
            if (string.IsNullOrWhiteSpace(provider.Id) || string.IsNullOrWhiteSpace(provider.Name))
            {
                throw new InvalidDataException("Nhà cung cấp DNS thiếu mã hoặc tên.");
            }

            if (!providerIds.Add(provider.Id))
            {
                throw new InvalidDataException("Mã nhà cung cấp DNS bị trùng: " + provider.Id);
            }

            var profiles = new List<DnsProfile>();
            foreach (var profile in provider.Profiles ?? new List<ProfileDocument>())
            {
                if (string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Name))
                {
                    throw new InvalidDataException("Hồ sơ DNS thiếu mã hoặc tên.");
                }

                if (!profileIds.Add(profile.Id))
                {
                    throw new InvalidDataException("Mã hồ sơ DNS bị trùng: " + profile.Id);
                }

                var validation = DnsValidation.Validate(profile.Ipv4, profile.Ipv6);
                if (!validation.IsValid)
                {
                    throw new InvalidDataException(
                        $"Hồ sơ DNS {profile.Id} không hợp lệ: {validation.Error}");
                }

                if (!string.IsNullOrWhiteSpace(profile.DohTemplate)
                    && (!Uri.TryCreate(profile.DohTemplate, UriKind.Absolute, out var uri)
                        || uri.Scheme != Uri.UriSchemeHttps))
                {
                    throw new InvalidDataException("DoH template phải là URL HTTPS: " + profile.Id);
                }

                profiles.Add(new DnsProfile
                {
                    Id = profile.Id,
                    ProviderId = provider.Id,
                    ProviderName = provider.Name,
                    Name = profile.Name,
                    Description = profile.Description ?? string.Empty,
                    Filtering = profile.Filtering ?? string.Empty,
                    Ipv4 = validation.Ipv4,
                    Ipv6 = validation.Ipv6,
                    DohTemplate = string.IsNullOrWhiteSpace(profile.DohTemplate) ? null : profile.DohTemplate.Trim(),
                    Protocols = ParseProtocols(profile.Protocols)
                });
            }

            if (profiles.Count == 0)
            {
                throw new InvalidDataException("Nhà cung cấp DNS không có hồ sơ: " + provider.Id);
            }

            providers.Add(new DnsProvider
            {
                Id = provider.Id,
                Name = provider.Name,
                Homepage = provider.Homepage ?? string.Empty,
                SourceUrl = provider.SourceUrl ?? string.Empty,
                LastVerified = provider.LastVerified ?? document.LastVerified ?? string.Empty,
                Profiles = profiles
            });
        }

        if (providers.Count == 0)
        {
            throw new InvalidDataException("Danh mục DNS không có nhà cung cấp nào.");
        }

        return new DnsCatalog(providers, document.LastVerified ?? string.Empty);
    }

    public DnsProfile? FindProfile(string? profileId) =>
        string.IsNullOrWhiteSpace(profileId)
            ? null
            : Providers.SelectMany(p => p.Profiles)
                .FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<DnsProtocol> ParseProtocols(IEnumerable<string>? values)
    {
        var result = new List<DnsProtocol>();
        foreach (var value in values ?? Array.Empty<string>())
        {
            if (Enum.TryParse<DnsProtocol>(value, ignoreCase: true, out var protocol)
                && !result.Contains(protocol))
            {
                result.Add(protocol);
            }
        }

        return result;
    }

    internal sealed class CatalogDocument
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("lastVerified")]
        public string? LastVerified { get; set; }

        [JsonPropertyName("providers")]
        public List<ProviderDocument>? Providers { get; set; }
    }

    internal sealed class ProviderDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("homepage")]
        public string? Homepage { get; set; }

        [JsonPropertyName("sourceUrl")]
        public string? SourceUrl { get; set; }

        [JsonPropertyName("lastVerified")]
        public string? LastVerified { get; set; }

        [JsonPropertyName("profiles")]
        public List<ProfileDocument>? Profiles { get; set; }
    }

    internal sealed class ProfileDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("filtering")]
        public string? Filtering { get; set; }

        [JsonPropertyName("ipv4")]
        public List<string>? Ipv4 { get; set; }

        [JsonPropertyName("ipv6")]
        public List<string>? Ipv6 { get; set; }

        [JsonPropertyName("dohTemplate")]
        public string? DohTemplate { get; set; }

        [JsonPropertyName("protocols")]
        public List<string>? Protocols { get; set; }
    }
}
