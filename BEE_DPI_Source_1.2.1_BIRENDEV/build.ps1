<#
.SYNOPSIS
    Builds the BEE DPI Release x64 package and verifies the bundled engine.

.DESCRIPTION
    Produces a runnable folder (default: dist\BEE DPI 1.2.1-rc3) containing the WPF
    UI, the elevated engine host, the fixed DNS helper script and the unmodified
    GoodbyeDPI 0.2.3rc3 engine (WinDivert 2.2).
    Both executables are published as self-contained single-file .NET 10 apps, so the
    target machine does not need a separate .NET runtime.

.PARAMETER FrameworkDependent
    Publish framework-dependent binaries instead (requires .NET Desktop
    Runtime 10 on the target machine). The engine host is then also
    framework-dependent.

.EXAMPLE
    .\build.ps1

.EXAMPLE
    .\build.ps1 -FrameworkDependent -OutputPath .\dist\bee-dpi-fd
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputPath,
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$appProject = Join-Path $root "src\BeeDPI.App\BeeDPI.App.csproj"
$hostProject = Join-Path $root "src\BeeDPI.EngineHost\BeeDPI.EngineHost.csproj"
$engineDir = Join-Path $root "engine"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root "dist\BEE DPI 1.2.1-rc3"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

function Find-DotNet {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles "dotnet\dotnet.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "dotnet\dotnet.exe")
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    throw ".NET SDK not found. Install .NET SDK 10 and try again."
}

function Assert-EngineFile {
    param(
        [string]$Path,
        [string]$ExpectedHash
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing engine file: $Path"
    }

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actual -ne $ExpectedHash) {
        throw "Engine file hash mismatch: $Path`nExpected: $ExpectedHash`nActual:   $actual"
    }

    Write-Host ("  OK  {0}" -f (Split-Path -Leaf $Path))
}

function Assert-OutputFile {
    param([string]$RelativePath)

    $full = Join-Path $OutputPath $RelativePath
    if (-not (Test-Path -LiteralPath $full)) {
        throw "Published package is missing: $RelativePath"
    }
}

function Assert-Utf8Bom {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing file: $Path"
    }

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) {
        throw "Windows PowerShell 5.1 requires UTF-8 BOM for this script: $Path"
    }

    Write-Host ("  OK  UTF-8 BOM  {0}" -f (Split-Path -Leaf $Path))
}

function Assert-ProductVersion {
    param(
        [string]$Path,
        [string]$Expected
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing executable: $Path"
    }

    $info = (Get-Item -LiteralPath $Path).VersionInfo
    if (-not $info.FileVersion.StartsWith($Expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected file version for $Path`nExpected prefix: $Expected`nActual: $($info.FileVersion)"
    }

    Write-Host ("  OK  version {0}  {1}" -f $info.FileVersion, (Split-Path -Leaf $Path))
}

$dotnet = Find-DotNet
$selfContained = if ($FrameworkDependent) { "false" } else { "true" }
$buildStarted = Get-Date

Write-Host ""
Write-Host "BEE DPI build" -ForegroundColor Yellow
Write-Host "  dotnet:        $dotnet"
Write-Host "  configuration: $Configuration"
Write-Host "  runtime:       $Runtime"
Write-Host "  self-contained: $selfContained"
Write-Host "  output:        $OutputPath"
Write-Host ""

Write-Host "Verifying pristine engine files..." -ForegroundColor Yellow
Assert-EngineFile (Join-Path $engineDir "goodbyedpi.exe") "8D412B094BB9C137FF25BA9A794D1122ECC84BB776DEBFF6C249723A13CC31CD"
Assert-EngineFile (Join-Path $engineDir "WinDivert.dll") "6110BFA44667405179C3E15E12AF1B62037E447ED59B054B19042032995E6C7E"
Assert-EngineFile (Join-Path $engineDir "WinDivert64.sys") "E69B5BA3F0CD6CFB2983E442636E7F0B342B61B15264B0328317D4559C82CF50"

Write-Host ""
Write-Host "Publishing BEE DPI UI (single-file self-contained)..." -ForegroundColor Yellow
& $dotnet publish $appProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained $selfContained `
    -p:Platform=x64 `
    -o $OutputPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for BeeDPI.App (exit code $LASTEXITCODE)."
}

if (-not $FrameworkDependent) {
    Write-Host ""
    Write-Host "Publishing elevated engine host (single-file self-contained)..." -ForegroundColor Yellow
    & $dotnet publish $hostProject `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:Platform=x64 `
        -o $OutputPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for BeeDPI.EngineHost (exit code $LASTEXITCODE)."
    }
}

Write-Host ""
Write-Host "Verifying package layout..." -ForegroundColor Yellow
$required = @(
    "BeeDPI.exe",
    "BeeDPI.EngineHost.exe",
    "Engine\goodbyedpi.exe",
    "Engine\WinDivert.dll",
    "Engine\WinDivert64.sys",
    "assets\bee.ico",
    "LICENSE\LICENSE-Apache-2.0.txt",
    "LICENSE\LICENSE-goodbyedpi.txt",
    "LICENSE\LICENSE-windivert.txt",
    "LICENSE\LICENSE-getline.txt",
    "LICENSE\LICENSE-uthash.txt",
    "LICENSE\LICENSE-BeVietnamPro-OFL.txt",
    "THIRD-PARTY-NOTICES.txt",
    "README.md",
    "CHANGELOG.md",
    "REPORT-1.2.1-rc3.md",
    "install.ps1",
    "uninstall.ps1",
    "Cai dat BEE DPI.cmd",
    "tools\dns-manager.ps1",
    "docs\DNS-PROVIDERS.md",
    "docs\ENGINE-COMPATIBILITY.md",
    "docs\screenshots\main.png",
    "docs\screenshots\dns.png"
)

foreach ($item in $required) {
    Assert-OutputFile $item
    Write-Host ("  OK  {0}" -f $item)
}

Assert-Utf8Bom (Join-Path $OutputPath "tools\dns-manager.ps1")
Assert-Utf8Bom (Join-Path $OutputPath "install.ps1")
Assert-Utf8Bom (Join-Path $OutputPath "uninstall.ps1")
Assert-ProductVersion (Join-Path $OutputPath "BeeDPI.exe") "1.2.1"
Assert-ProductVersion (Join-Path $OutputPath "BeeDPI.EngineHost.exe") "1.2.1"

# The published engine must still be byte-identical to the verified source.
Assert-EngineFile (Join-Path $OutputPath "Engine\goodbyedpi.exe") "8D412B094BB9C137FF25BA9A794D1122ECC84BB776DEBFF6C249723A13CC31CD"
Assert-EngineFile (Join-Path $OutputPath "Engine\WinDivert.dll") "6110BFA44667405179C3E15E12AF1B62037E447ED59B054B19042032995E6C7E"
Assert-EngineFile (Join-Path $OutputPath "Engine\WinDivert64.sys") "E69B5BA3F0CD6CFB2983E442636E7F0B342B61B15264B0328317D4559C82CF50"

# Older release folders must stay byte-for-byte untouched.
$legacyPackages = @(
    (Join-Path $root "dist\BEE DPI"),
    (Join-Path $root "dist\BEE DPI 1.1.0-rc"),
    (Join-Path $root "dist\BEE DPI 1.2.0-rc"),
    (Join-Path $root "dist\BEE DPI 1.2.1-rc"),
    (Join-Path $root "dist\BEE DPI 1.2.1-rc2")
)
foreach ($legacy in $legacyPackages) {
    if (-not (Test-Path -LiteralPath $legacy)) {
        continue
    }

    $touched = @(Get-ChildItem -LiteralPath $legacy -Recurse -File |
        Where-Object { $_.LastWriteTime -gt $buildStarted })
    if ($touched.Count -gt 0) {
        throw ("Previous package was modified: {0} ({1} file(s))" -f $legacy, $touched.Count)
    }

    Write-Host ("  OK  untouched  {0}" -f (Split-Path -Leaf $legacy))
}

$size = (Get-ChildItem -LiteralPath $OutputPath -Recurse -File | Measure-Object -Property Length -Sum).Sum
$sizeMb = [Math]::Round($size / 1MB, 1)

Write-Host ""
Write-Host "Build complete." -ForegroundColor Green
Write-Host "  Package: $OutputPath"
Write-Host "  Size:    $sizeMb MB"
Write-Host ""
