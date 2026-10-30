# BEE DPI installer (1.2.1-rc)
#
# Copies the portable package to Program Files, creates the Start Menu shortcut
# with the BEE DPI icon and registers a normal uninstall entry. Windows shows a
# UAC prompt because Program Files and the machine-wide Start Menu are shared.
#
# This script never changes DNS, proxy, routes, hosts or any network setting and
# never registers BEE DPI to start with Windows.

[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:ProgramFiles "BEE DPI"),
    [switch]$NoLaunch,
    [switch]$DesktopShortcut
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Start-ElevatedSelf {
    $arguments = @(
        "-NoLogo",
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", ('"{0}"' -f $PSCommandPath),
        "-InstallDirectory", ('"{0}"' -f $InstallDirectory)
    )

    if ($NoLaunch) { $arguments += "-NoLaunch" }
    if ($DesktopShortcut) { $arguments += "-DesktopShortcut" }

    Write-Host "BEE DPI cần quyền quản trị để cài đặt. Hãy chấp nhận hộp thoại UAC." -ForegroundColor Yellow
    Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $arguments | Out-Null
}

if (-not (Test-IsAdministrator)) {
    Start-ElevatedSelf
    return
}

$source = $PSScriptRoot
$appSource = Join-Path $source "BeeDPI.exe"
if (-not (Test-Path -LiteralPath $appSource)) {
    throw "Không tìm thấy BeeDPI.exe. Hãy chạy bộ cài từ thư mục đã giải nén của BEE DPI."
}

if (Get-Process -Name "BeeDPI" -ErrorAction SilentlyContinue) {
    throw "BEE DPI đang chạy. Hãy thoát BEE DPI (chuột phải biểu tượng khay > Thoát BEE DPI) rồi chạy lại bộ cài."
}

$InstallDirectory = [System.IO.Path]::GetFullPath($InstallDirectory)
Write-Host ""
Write-Host "BEE DPI 1.2.1-rc" -ForegroundColor Yellow
Write-Host "  Cài đặt vào: $InstallDirectory"
Write-Host ""

New-Item -ItemType Directory -Force -Path $InstallDirectory | Out-Null

# robocopy keeps the layout identical to the portable package. Exit codes below
# 8 mean success (1 = files copied, 2 = extra files, 3 = both).
& robocopy $source $InstallDirectory /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "Không thể sao chép tệp BEE DPI (mã lỗi robocopy $LASTEXITCODE)."
}

$target = Join-Path $InstallDirectory "BeeDPI.exe"
$icon = Join-Path $InstallDirectory "assets\bee.ico"
$shell = New-Object -ComObject WScript.Shell

$startMenuFolder = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs"
New-Item -ItemType Directory -Force -Path $startMenuFolder | Out-Null
$startMenuShortcut = Join-Path $startMenuFolder "BEE DPI.lnk"
$shortcut = $shell.CreateShortcut($startMenuShortcut)
$shortcut.TargetPath = $target
$shortcut.WorkingDirectory = $InstallDirectory
$shortcut.IconLocation = "$icon,0"
$shortcut.Description = "BEE DPI"
$shortcut.Save()

if ($DesktopShortcut) {
    $desktopShortcut = Join-Path ([Environment]::GetFolderPath("CommonDesktopDirectory")) "BEE DPI.lnk"
    $shortcut = $shell.CreateShortcut($desktopShortcut)
    $shortcut.TargetPath = $target
    $shortcut.WorkingDirectory = $InstallDirectory
    $shortcut.IconLocation = "$icon,0"
    $shortcut.Description = "BEE DPI"
    $shortcut.Save()
}

$uninstallKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\BEE DPI"
New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value "BEE DPI"
Set-ItemProperty -Path $uninstallKey -Name "DisplayVersion" -Value "1.2.1-rc"
Set-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "BirenDev"
Set-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value "$target,0"
Set-ItemProperty -Path $uninstallKey -Name "InstallLocation" -Value $InstallDirectory
Set-ItemProperty -Path $uninstallKey -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name "NoRepair" -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name "UninstallString" -Value ('powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "{0}"' -f (Join-Path $InstallDirectory "uninstall.ps1"))

Write-Host "Đã cài đặt BEE DPI." -ForegroundColor Green
Write-Host "  Start Menu: BEE DPI"
Write-Host "  Gỡ cài đặt: Settings > Apps > BEE DPI"
Write-Host ""

# Launch through the existing shell so the UI keeps running as the normal user
# (BEE DPI itself is asInvoker; only the engine host asks for elevation later).
if (-not $NoLaunch) {
    Start-Process -FilePath "explorer.exe" -ArgumentList ('"{0}"' -f $target)
}
