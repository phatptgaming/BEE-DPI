# Generates the BEE DPI application icon and logo raster from an original
# BirenDev design. No upstream logo assets are used.
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\assets")
)

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function New-BeeBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $scale = $Size / 256.0
    $g.ScaleTransform($scale, $scale)

    # Hexagon badge
    $center = 128.0
    $radius = 118.0
    $points = New-Object System.Drawing.PointF[] 6
    for ($i = 0; $i -lt 6; $i++) {
        $angle = ([Math]::PI / 180.0) * (60.0 * $i - 90.0)
        $points[$i] = New-Object System.Drawing.PointF(
            [float]($center + $radius * [Math]::Cos($angle)),
            [float]($center + $radius * [Math]::Sin($angle)))
    }

    $hexPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $hexPath.AddPolygon($points)

    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(40, 20)),
        (New-Object System.Drawing.PointF(216, 240)),
        [System.Drawing.Color]::FromArgb(255, 255, 214, 64),
        [System.Drawing.Color]::FromArgb(255, 240, 166, 0))
    $g.FillPath($gradient, $hexPath)

    # Rounded-looking rim
    $rim = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 255, 205, 40), 10)
    $rim.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($rim, $hexPath)

    # Everything inside the badge is clipped to the hexagon
    $g.SetClip($hexPath, [System.Drawing.Drawing2D.CombineMode]::Intersect)

    # Wings (behind the body)
    $wingBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 255, 247, 220))
    $wingPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(70, 80, 55, 0), 3)

    $state = $g.Save()
    $g.TranslateTransform(121, 104)
    $g.RotateTransform(-28)
    $g.FillEllipse($wingBrush, -64, -20, 74, 40)
    $g.DrawEllipse($wingPen, -64, -20, 74, 40)
    $g.Restore($state)

    $state = $g.Save()
    $g.TranslateTransform(135, 104)
    $g.RotateTransform(28)
    $g.FillEllipse($wingBrush, -10, -20, 74, 40)
    $g.DrawEllipse($wingPen, -10, -20, 74, 40)
    $g.Restore($state)

    # Body + head
    $beeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 16, 16, 18))
    $g.FillEllipse($beeBrush, 100, 104, 56, 86)
    $g.FillEllipse($beeBrush, 105, 80, 46, 46)

    # Stripes clipped to the body
    $bodyClip = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bodyClip.AddEllipse(100, 104, 56, 86)
    $state = $g.Save()
    $g.SetClip($bodyClip, [System.Drawing.Drawing2D.CombineMode]::Intersect)
    $stripeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 199, 44))
    $g.FillRectangle($stripeBrush, 92, 126, 72, 13)
    $g.FillRectangle($stripeBrush, 92, 153, 72, 14)
    $g.FillRectangle($stripeBrush, 92, 181, 72, 13)
    $g.Restore($state)

    # Antennae
    $antennaPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 16, 16, 18), 7)
    $antennaPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $antennaPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawBezier($antennaPen, 114, 90, 96, 70, 84, 58, 72, 50)
    $g.DrawBezier($antennaPen, 142, 90, 160, 70, 172, 58, 184, 50)

    # Stinger
    $stinger = New-Object System.Drawing.Drawing2D.GraphicsPath
    $stingerPoints = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(120, 184)),
        (New-Object System.Drawing.PointF(136, 184)),
        (New-Object System.Drawing.PointF(128, 202)))
    $stinger.AddPolygon($stingerPoints)
    $g.FillPath($beeBrush, $stinger)

    # Face highlight
    $eyeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 211, 60))
    $g.FillEllipse($eyeBrush, 116, 94, 9, 9)
    $g.FillEllipse($eyeBrush, 132, 94, 9, 9)

    $g.ResetClip()

    $eyeBrush.Dispose()
    $stinger.Dispose()
    $antennaPen.Dispose()
    $stripeBrush.Dispose()
    $bodyClip.Dispose()
    $beeBrush.Dispose()
    $wingPen.Dispose()
    $wingBrush.Dispose()
    $rim.Dispose()
    $gradient.Dispose()
    $hexPath.Dispose()
    $g.Dispose()

    return $bmp
}

# Master raster
$master = New-BeeBitmap -Size 256
$pngPath = Join-Path $OutputDirectory "bee-logo.png"
$master.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$master.Dispose()

# Multi-size ICO with PNG-compressed frames
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($s in $sizes) {
    $bmp = New-BeeBitmap -Size $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@($s, $ms.ToArray())
    $bmp.Dispose()
    $ms.Dispose()
}

$icoPath = Join-Path $OutputDirectory "bee.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$frames.Count)

$offset = 6 + (16 * $frames.Count)
foreach ($frame in $frames) {
    $size = $frame[0]
    $data = $frame[1]
    $bw.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $bw.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}

foreach ($frame in $frames) {
    $bw.Write($frame[1])
}

$bw.Flush()
$bw.Dispose()
$fs.Dispose()

Write-Host "Generated $pngPath and $icoPath"

