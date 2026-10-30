#requires -Version 5.1
<#
    BEE DPI - Windows DNS manager (fixed helper script)

    All input arrives in a temporary JSON file. This script never evaluates or
    interpolates user supplied text as code. It only:
      * lists active physical adapters,
      * snapshots DNS state,
      * applies validated IPv4/IPv6 DNS addresses and optional native DoH,
      * restores a previously captured snapshot,
      * verifies the result with a real DNS lookup.

    Results are written as one JSON line prefixed with ##BEEDPI-RESULT##.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RequestPath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$ResultMarker = '##BEEDPI-RESULT##'
$script:RequestFile = $RequestPath

try {
    [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $OutputEncoding = New-Object System.Text.UTF8Encoding($false)
}
catch {
    # Older hosts may keep the console encoding; ASCII JSON is still valid.
}

function Read-Request {
    if (-not (Test-Path -LiteralPath $script:RequestFile)) {
        throw 'Yêu cầu DNS không tồn tại.'
    }

    $raw = [System.IO.File]::ReadAllText($script:RequestFile, [System.Text.Encoding]::UTF8)
    try {
        Remove-Item -LiteralPath $script:RequestFile -Force -ErrorAction SilentlyContinue
    }
    catch {
        # The caller also removes the file.
    }

    return $raw | ConvertFrom-Json
}

function Write-Result {
    param(
        [bool]$Ok,
        [string]$Code,
        [string]$Message,
        [object]$Snapshot = $null,
        [object]$Adapters = $null,
        [object]$DohChanges = $null,
        [object]$Capabilities = $null,
        [string]$Verification = $null,
        [string]$Rollback = $null
    )

    $payload = [ordered]@{
        ok           = $Ok
        code         = $Code
        message      = $Message
        snapshot     = $Snapshot
        adapters     = $Adapters
        dohChanges   = $DohChanges
        capabilities = $Capabilities
        verification = $Verification
        rollback     = $Rollback
    }

    $json = $payload | ConvertTo-Json -Depth 12 -Compress
    [Console]::Out.WriteLine($ResultMarker + $json)
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Normalize-Guid {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    try {
        return ([Guid]::Parse($Value)).ToString('B').ToUpperInvariant()
    }
    catch {
        return $null
    }
}

function Normalize-Address {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    try {
        return ([System.Net.IPAddress]::Parse($Value.Trim())).ToString()
    }
    catch {
        return $null
    }
}

function Get-VersionBuild {
    return [Environment]::OSVersion.Version.Build
}

function Test-DohSupported {
    if ((Get-VersionBuild) -lt 22000) { return $false }
    $command = Get-Command Add-DnsClientDohServerAddress -ErrorAction SilentlyContinue
    return [bool]$command
}

function Get-FamilyState {
    param(
        [uint32]$InterfaceIndex,
        [string]$Family,
        [string]$InterfaceGuid
    )

    if ($Family -eq 'IPv4') {
        $registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\$InterfaceGuid"
        $addressFamily = 2
    }
    else {
        $registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\$InterfaceGuid"
        $addressFamily = 23
    }

    $nameServer = $null
    if (Test-Path -LiteralPath $registryPath) {
        try {
            $nameServer = (Get-ItemProperty -LiteralPath $registryPath -Name NameServer -ErrorAction SilentlyContinue).NameServer
        }
        catch {
            $nameServer = $null
        }
    }

    $isStatic = -not [string]::IsNullOrWhiteSpace($nameServer)
    $addresses = @()
    if ($isStatic) {
        $addresses = @($nameServer -split '[,;\s]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }
    else {
        try {
            $records = @(Get-DnsClientServerAddress -InterfaceIndex $InterfaceIndex -AddressFamily $addressFamily -ErrorAction Stop)
            foreach ($record in $records) {
                foreach ($address in @($record.ServerAddresses)) {
                    if (-not [string]::IsNullOrWhiteSpace($address)) { $addresses += $address }
                }
            }
        }
        catch {
            $addresses = @()
        }
    }

    $normalized = New-Object System.Collections.Generic.List[string]
    foreach ($address in $addresses) {
        $value = Normalize-Address $address
        if ($null -ne $value -and -not $normalized.Contains($value)) {
            $normalized.Add($value)
        }
    }

    $sorted = @($normalized | Sort-Object)
    return [ordered]@{
        isStatic        = $isStatic
        serverAddresses = @($sorted)
    }
}

function Get-Fingerprint {
    param(
        [string]$InterfaceGuid,
        [object]$Ipv4,
        [object]$Ipv6
    )

    function Get-Part([object]$State) {
        if ($State.isStatic) {
            return 'static[' + (@($State.serverAddresses) -join ',') + ']'
        }

        return 'auto'
    }

    return (Normalize-Guid $InterfaceGuid) + '|' + (Get-Part $Ipv4) + '|' + (Get-Part $Ipv6)
}

function Get-Snapshot {
    param([uint32]$InterfaceIndex)

    $adapter = Get-NetAdapter -InterfaceIndex $InterfaceIndex -ErrorAction Stop
    $guid = Normalize-Guid $adapter.InterfaceGuid
    $ipv4 = Get-FamilyState -InterfaceIndex $InterfaceIndex -Family 'IPv4' -InterfaceGuid $guid
    $ipv6 = Get-FamilyState -InterfaceIndex $InterfaceIndex -Family 'IPv6' -InterfaceGuid $guid
    $fingerprint = Get-Fingerprint -InterfaceGuid $guid -Ipv4 $ipv4 -Ipv6 $ipv6

    return [ordered]@{
        interfaceGuid  = $adapter.InterfaceGuid
        interfaceIndex = [uint32]$InterfaceIndex
        alias          = $adapter.Name
        ipv4           = $ipv4
        ipv6           = $ipv6
        fingerprint    = $fingerprint
    }
}

function Test-AdapterMatch {
    param(
        [object]$Request,
        [object]$Adapter
    )

    if ($null -eq $Adapter) { return $false }
    if ($null -ne $Request.interfaceIndex -and [uint32]$Request.interfaceIndex -ne [uint32]$Adapter.ifIndex) {
        return $false
    }

    if (-not [string]::IsNullOrWhiteSpace($Request.interfaceGuid)) {
        if ((Normalize-Guid $Request.interfaceGuid) -ne (Normalize-Guid $Adapter.InterfaceGuid)) {
            return $false
        }
    }

    return $true
}

function Get-InterfaceMetrics {
    $map = @{}
    foreach ($family in @('IPv4', 'IPv6')) {
        try {
            $interfaces = @(Get-NetIPInterface -AddressFamily $family -ErrorAction SilentlyContinue)
        }
        catch {
            $interfaces = @()
        }

        foreach ($interface in $interfaces) {
            $index = [uint32]$interface.ifIndex
            $metric = [int]$interface.InterfaceMetric
            if (-not $map.ContainsKey($index) -or $metric -lt $map[$index]) {
                $map[$index] = $metric
            }
        }
    }

    return $map
}

function Get-Adapters {
    # Route metric and interface metric are added together, which is how Windows
    # itself chooses between IPv4 and IPv6 default routes. Only the cost is used
    # for ordering, never the localized adapter name.
    $interfaceMetrics = Get-InterfaceMetrics
    $routeMetrics = @{}
    $routeCosts = @{}
    foreach ($prefix in @('0.0.0.0/0', '::/0')) {
        try {
            $routes = @(Get-NetRoute -DestinationPrefix $prefix -ErrorAction SilentlyContinue)
        }
        catch {
            $routes = @()
        }

        foreach ($route in $routes) {
            $index = [uint32]$route.ifIndex
            $metric = [int]$route.RouteMetric
            $cost = $metric
            if ($interfaceMetrics.ContainsKey($index)) {
                $cost = $cost + [int]$interfaceMetrics[$index]
            }

            if (-not $routeMetrics.ContainsKey($index) -or $metric -lt $routeMetrics[$index]) {
                $routeMetrics[$index] = $metric
            }

            if (-not $routeCosts.ContainsKey($index) -or $cost -lt $routeCosts[$index]) {
                $routeCosts[$index] = $cost
            }
        }
    }

    $result = New-Object System.Collections.Generic.List[object]
    $adapters = @(Get-NetAdapter -Physical -ErrorAction SilentlyContinue)
    foreach ($adapter in $adapters) {
        if ($adapter.Status -ne 'Up') { continue }
        $ifType = $null
        if ($null -ne $adapter.InterfaceType) { $ifType = [int]$adapter.InterfaceType }
        elseif ($null -ne $adapter.ifType) { $ifType = [int]$adapter.ifType }

        # 6 = Ethernet, 71 = Native 802.11. Everything else is virtual, a tunnel,
        # a loopback adapter or an unknown transport and must never be modified.
        if ($ifType -ne 6 -and $ifType -ne 71) { continue }

        $index = [uint32]$adapter.ifIndex
        $hasDefault = $routeCosts.ContainsKey($index)
        $metric = 999999
        $cost = 999999
        if ($routeMetrics.ContainsKey($index)) { $metric = [int]$routeMetrics[$index] }
        if ($hasDefault) { $cost = [int]$routeCosts[$index] }

        $result.Add([ordered]@{
            interfaceGuid   = $adapter.InterfaceGuid
            interfaceIndex  = $index
            alias           = $adapter.Name
            description     = $adapter.InterfaceDescription
            isUp            = ($adapter.Status -eq 'Up')
            isPhysical      = $true
            hasDefaultRoute = $hasDefault
            routeMetric     = $metric
            routeCost       = $cost
            isWireless      = ($ifType -eq 71)
        })
    }

    return @($result | Sort-Object `
        @{ Expression = { $_.routeCost }; Ascending = $true }, `
        @{ Expression = { $_.routeMetric }; Ascending = $true }, `
        @{ Expression = { $_.alias }; Ascending = $true })
}

function Invoke-NetshDns {
    param([string[]]$Arguments)

    $output = & netsh.exe @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        $message = ($output -replace '\s+', ' ').Trim()
        if ([string]::IsNullOrWhiteSpace($message)) {
            $message = "netsh kết thúc với mã $LASTEXITCODE."
        }
        throw $message
    }
}

# Set-DnsClientServerAddress has no -AddressFamily parameter, so it cannot
# switch one family to DHCP while the other keeps static servers. netsh
# operates per protocol and accepts pre-validated addresses and a numeric
# interface index as separate arguments (never a concatenated shell string).
function Set-FamilyServers {
    param(
        [uint32]$InterfaceIndex,
        [string]$Family,
        [string[]]$Addresses
    )

    $protocol = 'ipv4'
    if ($Family -eq 'IPv6') { $protocol = 'ipv6' }

    $list = @()
    if ($null -ne $Addresses) {
        $list = @($Addresses | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }

    if ($list.Count -eq 0) {
        Invoke-NetshDns -Arguments @(
            'interface', $protocol, 'set', 'dnsservers',
            "name=$InterfaceIndex", 'source=dhcp'
        )
        return
    }

    Invoke-NetshDns -Arguments @(
        'interface', $protocol, 'set', 'dnsservers',
        "name=$InterfaceIndex", 'source=static',
        "address=$($list[0])", 'register=primary', 'validate=no'
    )

    for ($i = 1; $i -lt $list.Count; $i++) {
        Invoke-NetshDns -Arguments @(
            'interface', $protocol, 'add', 'dnsservers',
            "name=$InterfaceIndex", "address=$($list[$i])",
            "index=$($i + 1)", 'validate=no'
        )
    }
}

function Set-FamilyAddresses {
    param(
        [uint32]$InterfaceIndex,
        [string]$Family,
        [object]$State
    )

    $addresses = @()
    $isStatic = $false
    if ($null -ne $State) {
        $addresses = @($State.serverAddresses | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $isStatic = [bool]$State.isStatic
    }

    if ($isStatic -and $addresses.Count -gt 0) {
        Set-FamilyServers -InterfaceIndex $InterfaceIndex -Family $Family -Addresses $addresses
    }
    else {
        Set-FamilyServers -InterfaceIndex $InterfaceIndex -Family $Family -Addresses @()
    }
}

function Set-RequestedFamily {
    param(
        [uint32]$InterfaceIndex,
        [string]$Family,
        [object]$Addresses
    )

    $list = @()
    if ($null -ne $Addresses) { $list = @($Addresses) }
    if ($list.Count -eq 0) { return }

    Set-FamilyServers -InterfaceIndex $InterfaceIndex -Family $Family -Addresses $list
}

function Get-DohEntry {
    param([string]$ServerAddress)

    try {
        $entries = @(Get-DnsClientDohServerAddress -ErrorAction SilentlyContinue)
    }
    catch {
        return $null
    }

    foreach ($entry in $entries) {
        if ((Normalize-Address $entry.ServerAddress) -eq (Normalize-Address $ServerAddress)) {
            return $entry
        }
    }

    return $null
}

function Get-DohChanges {
    param([object]$Changes)

    if ($null -eq $Changes) { return @() }
    return @($Changes | Where-Object { $null -ne $_ })
}

function Undo-DohChanges {
    param([object]$Changes)

    $errors = New-Object System.Collections.Generic.List[string]
    foreach ($change in (Get-DohChanges $Changes)) {
        try {
            if ([bool]$change.existed) {
                Set-DnsClientDohServerAddress `
                    -ServerAddress $change.serverAddress `
                    -DohTemplate $change.dohTemplate `
                    -AutoUpgrade ([bool]$change.autoUpgrade) `
                    -AllowFallbackToUdp ([bool]$change.allowFallbackToUdp) `
                    -ErrorAction Stop | Out-Null
            }
            else {
                Remove-DnsClientDohServerAddress -ServerAddress $change.serverAddress -Confirm:$false -ErrorAction Stop | Out-Null
            }
        }
        catch {
            $errors.Add("$($change.serverAddress): $($_.Exception.Message)")
        }
    }

    return @($errors)
}

function Set-ManagedDohChanges {
    param([object]$Changes)

    $errors = New-Object System.Collections.Generic.List[string]
    foreach ($change in (Get-DohChanges $Changes)) {
        if ([string]::IsNullOrWhiteSpace($change.appliedDohTemplate)) { continue }
        try {
            $existing = Get-DohEntry $change.serverAddress
            if ($null -ne $existing) {
                Set-DnsClientDohServerAddress `
                    -ServerAddress $change.serverAddress `
                    -DohTemplate $change.appliedDohTemplate `
                    -AutoUpgrade ([bool]$change.appliedAutoUpgrade) `
                    -AllowFallbackToUdp ([bool]$change.appliedAllowFallbackToUdp) `
                    -ErrorAction Stop | Out-Null
            }
            else {
                Add-DnsClientDohServerAddress `
                    -ServerAddress $change.serverAddress `
                    -DohTemplate $change.appliedDohTemplate `
                    -AutoUpgrade ([bool]$change.appliedAutoUpgrade) `
                    -AllowFallbackToUdp ([bool]$change.appliedAllowFallbackToUdp) `
                    -ErrorAction Stop | Out-Null
            }
        }
        catch {
            $errors.Add("$($change.serverAddress): $($_.Exception.Message)")
        }
    }

    return @($errors)
}

function Enable-DohEntry {
    param(
        [string]$ServerAddress,
        [string]$Template,
        [System.Collections.ArrayList]$Changes
    )

    $existing = Get-DohEntry $ServerAddress
    $change = [ordered]@{
        serverAddress              = $ServerAddress
        existed                    = ($null -ne $existing)
        dohTemplate                = $null
        autoUpgrade                = $false
        allowFallbackToUdp         = $true
        appliedDohTemplate         = $Template
        appliedAutoUpgrade         = $true
        appliedAllowFallbackToUdp  = $false
    }

    if ($null -ne $existing) {
        $change.dohTemplate = $existing.DohTemplate
        $change.autoUpgrade = [bool]$existing.AutoUpgrade
        $change.allowFallbackToUdp = [bool]$existing.AllowFallbackToUdp
        Set-DnsClientDohServerAddress `
            -ServerAddress $ServerAddress `
            -DohTemplate $Template `
            -AutoUpgrade $true `
            -AllowFallbackToUdp $false `
            -ErrorAction Stop | Out-Null
    }
    else {
        Add-DnsClientDohServerAddress `
            -ServerAddress $ServerAddress `
            -DohTemplate $Template `
            -AutoUpgrade $true `
            -AllowFallbackToUdp $false `
            -ErrorAction Stop | Out-Null
    }

    [void]$Changes.Add([pscustomobject]$change)
}

function Get-EffectiveAddresses {
    param(
        [uint32]$InterfaceIndex,
        [string]$Family
    )

    $addressFamily = 2
    if ($Family -eq 'IPv6') { $addressFamily = 23 }

    $result = @()
    try {
        $records = @(Get-DnsClientServerAddress -InterfaceIndex $InterfaceIndex -AddressFamily $addressFamily -ErrorAction Stop)
        foreach ($record in $records) {
            foreach ($address in @($record.ServerAddresses)) {
                $value = Normalize-Address $address
                if ($null -ne $value -and $result -notcontains $value) { $result += $value }
            }
        }
    }
    catch {
        return @()
    }

    return @($result | Sort-Object)
}

function Compare-AddressSets {
    param(
        [object]$Expected,
        [object]$Actual
    )

    $expectedList = @()
    if ($null -ne $Expected) { $expectedList = @($Expected) }

    $expectedNormalized = @($expectedList | ForEach-Object { Normalize-Address $_ } | Where-Object { $null -ne $_ } | Sort-Object -Unique)
    $actualNormalized = @($Actual | Sort-Object -Unique)

    if ($expectedNormalized.Count -ne $actualNormalized.Count) { return $false }
    for ($i = 0; $i -lt $expectedNormalized.Count; $i++) {
        if ($expectedNormalized[$i] -ne $actualNormalized[$i]) { return $false }
    }

    return $true
}

function Test-DnsResolution {
    $names = @('www.microsoft.com', 'www.google.com')
    $errors = New-Object System.Collections.Generic.List[string]

    foreach ($name in $names) {
        try {
            $answer = @(Resolve-DnsName -Name $name -Type A -DnsOnly -QuickTimeout -ErrorAction Stop)
            foreach ($record in $answer) {
                if ($null -ne $record.IPAddress -and -not [string]::IsNullOrWhiteSpace($record.IPAddress)) {
                    return [ordered]@{ ok = $true; message = "Phân giải $name thành công ($($record.IPAddress))." }
                }
            }
            $errors.Add("$name không trả về bản ghi A.")
        }
        catch {
            $errors.Add("$name : $($_.Exception.Message)")
        }
    }

    return [ordered]@{ ok = $false; message = ($errors -join ' | ') }
}

function Test-AppliedConfiguration {
    param(
        [uint32]$InterfaceIndex,
        [object]$Request
    )

    $requestV4 = @()
    if ($null -ne $Request.ipv4) { $requestV4 = @($Request.ipv4) }
    $requestV6 = @()
    if ($null -ne $Request.ipv6) { $requestV6 = @($Request.ipv6) }

    if ($requestV4.Count -gt 0) {
        $actual = Get-EffectiveAddresses -InterfaceIndex $InterfaceIndex -Family 'IPv4'
        if (-not (Compare-AddressSets -Expected $requestV4 -Actual $actual)) {
            return [ordered]@{ ok = $false; message = "IPv4 sau khi áp dụng không khớp: $($actual -join ', ')." }
        }
    }

    if ($requestV6.Count -gt 0) {
        $actual = Get-EffectiveAddresses -InterfaceIndex $InterfaceIndex -Family 'IPv6'
        if (-not (Compare-AddressSets -Expected $requestV6 -Actual $actual)) {
            return [ordered]@{ ok = $false; message = "IPv6 sau khi áp dụng không khớp: $($actual -join ', ')." }
        }
    }

    if ([bool]$Request.useDoh) {
        $template = [string]$Request.dohTemplate
        $dohAddresses = @()
        if ($null -ne $Request.dohAddresses) { $dohAddresses = @($Request.dohAddresses) }
        foreach ($address in $dohAddresses) {
            $entry = Get-DohEntry $address
            if ($null -eq $entry) {
                return [ordered]@{ ok = $false; message = "Windows chưa đăng ký DoH cho $address." }
            }

            if ([string]$entry.DohTemplate -ne $template) {
                return [ordered]@{ ok = $false; message = "DoH template không khớp cho $address." }
            }

            if (-not [bool]$entry.AutoUpgrade) {
                return [ordered]@{ ok = $false; message = "DoH AutoUpgrade chưa bật cho $address." }
            }
        }
    }

    try {
        Clear-DnsClientCache -ErrorAction SilentlyContinue
    }
    catch {
        # Cache flush is helpful but not required for correctness.
    }

    $resolution = Test-DnsResolution
    if (-not $resolution.ok) {
        return [ordered]@{ ok = $false; message = "Không thể xác minh DNS: $($resolution.message)" }
    }

    return [ordered]@{ ok = $true; message = $resolution.message }
}

function Test-RestoredConfiguration {
    param(
        [uint32]$InterfaceIndex,
        [object]$Snapshot
    )

    $current = Get-Snapshot $InterfaceIndex

    foreach ($family in @('ipv4', 'ipv6')) {
        $expected = $Snapshot.$family
        $actual = $current.$family
        if ([bool]$expected.isStatic -ne [bool]$actual.isStatic) {
            return [ordered]@{ ok = $false; message = "Trạng thái $family chưa được khôi phục." }
        }

        if ([bool]$expected.isStatic) {
            if (-not (Compare-AddressSets -Expected $expected.serverAddresses -Actual $actual.serverAddresses)) {
                return [ordered]@{ ok = $false; message = "Địa chỉ $family chưa được khôi phục chính xác." }
            }
        }
    }

    try {
        Clear-DnsClientCache -ErrorAction SilentlyContinue
    }
    catch {
        # Best effort.
    }

    $resolution = Test-DnsResolution
    if (-not $resolution.ok) {
        return [ordered]@{ ok = $false; message = "DNS đã khôi phục nhưng không phân giải được: $($resolution.message)" }
    }

    return [ordered]@{ ok = $true; message = $resolution.message }
}

function Restore-SnapshotState {
    param(
        [uint32]$InterfaceIndex,
        [object]$Snapshot
    )

    $errors = New-Object System.Collections.Generic.List[string]
    foreach ($family in @('ipv4', 'ipv6')) {
        try {
            Set-FamilyAddresses -InterfaceIndex $InterfaceIndex -Family $family -State $Snapshot.$family
        }
        catch {
            $errors.Add("$family : $($_.Exception.Message)")
        }
    }

    return @($errors)
}

function Invoke-List {
    $adapters = @(Get-Adapters)
    Write-Result -Ok $true -Code 'OK' -Message "Tìm thấy $($adapters.Count) adapter." -Adapters $adapters
}

function Invoke-Capabilities {
    $build = Get-VersionBuild
    $doh = Test-DohSupported
    $detail = if ($doh) {
        'Windows hỗ trợ DoH gốc (Add-DnsClientDohServerAddress).'
    }
    elseif ($build -lt 22000) {
        'Windows này cũ hơn bản build 22000 nên không có DoH gốc.'
    }
    else {
        'Không tìm thấy lệnh DoH gốc của Windows.'
    }

    $capabilities = [ordered]@{
        nativeDohSupported = $doh
        windowsBuild       = [int]$build
        detail             = $detail
    }

    Write-Result -Ok $true -Code 'OK' -Message $detail -Capabilities $capabilities
}

function Invoke-Snapshot {
    param([object]$Request)

    if (-not (Test-Administrator)) {
        # Reading DNS state does not require elevation.
    }

    $index = [uint32]$Request.interfaceIndex
    $adapter = Get-NetAdapter -InterfaceIndex $index -ErrorAction Stop
    if (-not (Test-AdapterMatch -Request $Request -Adapter $adapter)) {
        Write-Result -Ok $false -Code 'ADAPTER_MISMATCH' -Message 'Adapter không khớp.'
        return
    }

    $snapshot = Get-Snapshot $index
    Write-Result -Ok $true -Code 'OK' -Message 'Đã đọc cấu hình DNS.' -Snapshot $snapshot
}

function Invoke-Apply {
    param([object]$Request)

    if (-not (Test-Administrator)) {
        Write-Result -Ok $false -Code 'NOT_ELEVATED' -Message 'Không có quyền quản trị.'
        return
    }

    $index = [uint32]$Request.interfaceIndex
    $adapter = Get-NetAdapter -InterfaceIndex $index -ErrorAction Stop
    if (-not (Test-AdapterMatch -Request $Request -Adapter $adapter)) {
        Write-Result -Ok $false -Code 'ADAPTER_MISMATCH' -Message 'Adapter không khớp.'
        return
    }

    $requestV4 = @()
    if ($null -ne $Request.ipv4) { $requestV4 = @($Request.ipv4) }
    $requestV6 = @()
    if ($null -ne $Request.ipv6) { $requestV6 = @($Request.ipv6) }

    if ($requestV4.Count -eq 0 -and $requestV6.Count -eq 0) {
        Write-Result -Ok $false -Code 'INVALID_ADDRESS' -Message 'Cần ít nhất một địa chỉ DNS.'
        return
    }

    foreach ($address in ($requestV4 + $requestV6)) {
        if ($null -eq (Normalize-Address $address)) {
            Write-Result -Ok $false -Code 'INVALID_ADDRESS' -Message "Địa chỉ DNS không hợp lệ: $address"
            return
        }
    }

    $useDoh = [bool]$Request.useDoh
    $dohTemplate = [string]$Request.dohTemplate
    $dohAddresses = @()
    if ($null -ne $Request.dohAddresses) { $dohAddresses = @($Request.dohAddresses) }

    if ($useDoh) {
        if (-not (Test-DohSupported)) {
            Write-Result -Ok $false -Code 'DOH_UNSUPPORTED' -Message 'Windows này không hỗ trợ cấu hình DoH gốc.'
            return
        }

        if ([string]::IsNullOrWhiteSpace($dohTemplate) -or $dohAddresses.Count -eq 0) {
            Write-Result -Ok $false -Code 'DOH_INVALID' -Message 'Thiếu thông tin DoH.'
            return
        }

        $uri = $null
        if (-not [Uri]::TryCreate($dohTemplate, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne 'https') {
            Write-Result -Ok $false -Code 'DOH_INVALID' -Message 'DoH template không hợp lệ.'
            return
        }
    }

    $before = Get-Snapshot $index
    if (-not [string]::IsNullOrWhiteSpace($Request.expectedFingerprint) -and
        [string]$Request.expectedFingerprint -ne [string]$before.fingerprint) {
        Write-Result -Ok $false -Code 'CONFIG_CHANGED' -Message 'Cấu hình DNS đã thay đổi bên ngoài BEE DPI. Vui lòng thử lại.' -Snapshot $before
        return
    }

    $previous = Get-DohChanges $Request.previousDohChanges
    $revertErrors = Undo-DohChanges $previous
    if ($revertErrors.Count -gt 0) {
        Write-Result -Ok $false -Code 'DOH_REVERT_FAILED' -Message 'Không thể hoàn tác cấu hình DoH trước đó.' -Rollback ($revertErrors -join ' | ')
        return
    }

    $newChanges = New-Object System.Collections.ArrayList
    try {
        Set-RequestedFamily -InterfaceIndex $index -Family 'IPv4' -Addresses $requestV4
        Set-RequestedFamily -InterfaceIndex $index -Family 'IPv6' -Addresses $requestV6

        if ($useDoh) {
            foreach ($address in $dohAddresses) {
                Enable-DohEntry -ServerAddress $address -Template $dohTemplate -Changes $newChanges
            }
        }

        $verification = Test-AppliedConfiguration -InterfaceIndex $index -Request $Request
        if (-not $verification.ok) {
            throw [System.InvalidOperationException]::new($verification.message)
        }

        $after = Get-Snapshot $index
        Write-Result -Ok $true -Code 'OK' -Message 'Đã đổi DNS thành công.' -Snapshot $after -DohChanges @($newChanges) -Verification $verification.message
    }
    catch {
        $failure = $_.Exception.Message
        $rollbackErrors = New-Object System.Collections.Generic.List[string]

        $undoNew = Undo-DohChanges $newChanges
        foreach ($item in $undoNew) { $rollbackErrors.Add("DoH mới: $item") }

        $restoreErrors = Restore-SnapshotState -InterfaceIndex $index -Snapshot $before
        foreach ($item in $restoreErrors) { $rollbackErrors.Add("Địa chỉ: $item") }

        $reapplyErrors = Set-ManagedDohChanges $previous
        foreach ($item in $reapplyErrors) { $rollbackErrors.Add("DoH cũ: $item") }

        $rollbackText = 'Đã khôi phục cấu hình DNS trước đó.'
        if ($rollbackErrors.Count -gt 0) {
            $rollbackText = 'Khôi phục một phần thất bại: ' + ($rollbackErrors -join ' | ')
        }

        Write-Result -Ok $false -Code 'APPLY_FAILED' -Message ("Không thể áp dụng DNS: " + $failure) -Snapshot (Get-Snapshot $index) -Rollback $rollbackText
    }
}

function Invoke-Restore {
    param([object]$Request)

    if (-not (Test-Administrator)) {
        Write-Result -Ok $false -Code 'NOT_ELEVATED' -Message 'Không có quyền quản trị.'
        return
    }

    $index = [uint32]$Request.interfaceIndex
    $adapter = Get-NetAdapter -InterfaceIndex $index -ErrorAction Stop
    if (-not (Test-AdapterMatch -Request $Request -Adapter $adapter)) {
        Write-Result -Ok $false -Code 'ADAPTER_MISMATCH' -Message 'Adapter không khớp.'
        return
    }

    $original = [ordered]@{
        interfaceGuid  = $adapter.InterfaceGuid
        interfaceIndex = $index
        alias          = $adapter.Name
        ipv4           = $Request.originalIpv4
        ipv6           = $Request.originalIpv6
    }

    $before = Get-Snapshot $index
    if (-not [string]::IsNullOrWhiteSpace($Request.expectedFingerprint) -and
        [string]$Request.expectedFingerprint -ne [string]$before.fingerprint) {
        Write-Result -Ok $false -Code 'CONFIG_CHANGED' -Message 'Cấu hình DNS đã thay đổi bên ngoài BEE DPI. Vui lòng thử lại.' -Snapshot $before
        return
    }

    $previous = Get-DohChanges $Request.previousDohChanges
    try {
        $restoreErrors = Restore-SnapshotState -InterfaceIndex $index -Snapshot $original
        if ($restoreErrors.Count -gt 0) {
            throw [System.InvalidOperationException]::new(($restoreErrors -join ' | '))
        }

        $revertErrors = Undo-DohChanges $previous
        if ($revertErrors.Count -gt 0) {
            throw [System.InvalidOperationException]::new('DoH: ' + ($revertErrors -join ' | '))
        }

        $verification = Test-RestoredConfiguration -InterfaceIndex $index -Snapshot $original
        if (-not $verification.ok) {
            throw [System.InvalidOperationException]::new($verification.message)
        }

        $after = Get-Snapshot $index
        Write-Result -Ok $true -Code 'OK' -Message 'Đã khôi phục DNS ban đầu.' -Snapshot $after -Verification $verification.message
    }
    catch {
        $failure = $_.Exception.Message
        $rollbackErrors = New-Object System.Collections.Generic.List[string]

        $restoreErrors = Restore-SnapshotState -InterfaceIndex $index -Snapshot $before
        foreach ($item in $restoreErrors) { $rollbackErrors.Add("Địa chỉ: $item") }

        $reapplyErrors = Set-ManagedDohChanges $previous
        foreach ($item in $reapplyErrors) { $rollbackErrors.Add("DoH: $item") }

        $rollbackText = 'Đã giữ nguyên cấu hình trước khi khôi phục.'
        if ($rollbackErrors.Count -gt 0) {
            $rollbackText = 'Khôi phục một phần thất bại: ' + ($rollbackErrors -join ' | ')
        }

        Write-Result -Ok $false -Code 'RESTORE_FAILED' -Message ("Không thể khôi phục DNS: " + $failure) -Snapshot (Get-Snapshot $index) -Rollback $rollbackText
    }
}

try {
    $request = Read-Request
    $action = [string]$request.action

    switch ($action.ToLowerInvariant()) {
        'capabilities' { Invoke-Capabilities }
        'list' { Invoke-List }
        'snapshot' { Invoke-Snapshot -Request $request }
        'apply' { Invoke-Apply -Request $request }
        'restore' { Invoke-Restore -Request $request }
        'verify' {
            $index = [uint32]$request.interfaceIndex
            $snapshot = Get-Snapshot $index
            $verification = Test-DnsResolution
            Write-Result -Ok $verification.ok -Code $(if ($verification.ok) { 'OK' } else { 'VERIFY_FAILED' }) -Message $verification.message -Snapshot $snapshot -Verification $verification.message
        }
        default {
            Write-Result -Ok $false -Code 'UNKNOWN_ACTION' -Message 'Hành động DNS không hợp lệ.'
        }
    }
}
catch {
    Write-Result -Ok $false -Code 'SCRIPT_ERROR' -Message ('Lỗi xử lý DNS: ' + $_.Exception.Message) -Rollback ($_.ScriptStackTrace)
}
finally {
    try {
        if (Test-Path -LiteralPath $script:RequestFile) {
            Remove-Item -LiteralPath $script:RequestFile -Force -ErrorAction SilentlyContinue
        }
    }
    catch {
        # Best effort.
    }
}
