# BEE DPI uninstaller (1.2.0-rc)
#
# Removes the Start Menu shortcut, the uninstall entry and the installed files.
# It does not change DNS, proxy, routes, hosts or any other network setting, and
# it keeps the user's DNS backups and logs in %LOCALAPPDATA%\BeeDPI so a future
# install can still restore the original DNS.
#
# Usage: run from Settings > Apps > BEE DPI, or:
#   powershell -ExecutionPolicy Bypass -File uninstall.ps1

[CmdletBinding()]
param(
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    $arguments = @(
        "-NoLogo",
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", ('"{0}"' -f $PSCommandPath)
    )
    if ($Quiet) { $arguments += "-Quiet" }

    Write-Host "BEE DPI cần quyền quản trị để gỡ cài đặt. Hãy chấp nhận hộp thoại UAC." -ForegroundColor Yellow
    Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $arguments | Out-Null
    return
}

$installDirectory = $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $installDirectory "BeeDPI.exe"))) {
    throw "Không tìm thấy BeeDPI.exe trong thư mục cài đặt."
}

if (Get-Process -Name "BeeDPI" -ErrorAction SilentlyContinue) {
    if ($Quiet) {
        throw "BEE DPI đang chạy."
    }

    Write-Host "BEE DPI đang chạy. Hãy thoát BEE DPI trước khi gỡ cài đặt." -ForegroundColor Yellow
    Write-Host "Chuột phải biểu tượng BEE DPI trong khay hệ thống > Thoát BEE DPI."
    return
}

$startMenuShortcut = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\BEE DPI.lnk"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("CommonDesktopDirectory")) "BEE DPI.lnk"

if (Test-Path -LiteralPath $startMenuShortcut) {
    Remove-Item -LiteralPath $startMenuShortcut -Force
}

if (Test-Path -LiteralPath $desktopShortcut) {
    Remove-Item -LiteralPath $desktopShortcut -Force
}

$uninstallKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\BEE DPI"
if (Test-Path -LiteralPath $uninstallKey) {
    Remove-Item -LiteralPath $uninstallKey -Recurse -Force
}

Write-Host "Đã gỡ BEE DPI." -ForegroundColor Green
Write-Host "  Bản sao lưu DNS và nhật ký vẫn được giữ trong %LOCALAPPDATA%\BeeDPI."
Write-Host ""

# The script cannot delete the folder it is running from. Hand the cleanup to a
# short-lived cmd.exe that waits for this process to exit.
Set-Location -LiteralPath $env:TEMP
$cleanup = Join-Path $env:TEMP ("beedpi-uninstall-" + [Guid]::NewGuid().ToString("N") + ".cmd")
$content = "@echo off`r`ntimeout /t 2 /nobreak >nul`r`nrmdir /s /q `"$installDirectory`"`r`ndel /q `"%~f0`"`r`n"
[System.IO.File]::WriteAllText($cleanup, $content, [System.Text.Encoding]::Default)
Start-Process -FilePath "cmd.exe" -ArgumentList "/c", ('"{0}"' -f $cleanup) -WindowStyle Hidden
