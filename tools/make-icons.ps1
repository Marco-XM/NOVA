<#
.SYNOPSIS
    Renders NOVA's app and tray icons. Each size is drawn directly (not downscaled) so small sizes stay crisp.

.EXAMPLE
    ./tools/make-icons.ps1
    Writes src/Nova.App/Assets/nova.ico, nova-256.png, nova-tray.ico and nova-tray-light.ico.
#>
param(
    [string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) "src/Nova.App/Assets"),
    # Write only icon-preview.png (a contact sheet) to OutDir.
    [switch]$Preview
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationCore, WindowsBase

function Color([string]$hex, [double]$alpha = 1) {
    $c = [System.Windows.Media.ColorConverter]::ConvertFromString($hex)
    $c.A = [byte][math]::Round(255 * $alpha)
    return $c
}

function Freeze($f) { $f.Freeze(); return $f }

# App icon: a gradient squircle with the dynamic island (a dark pill) near the top and a soft glow under it.
function Draw-AppIcon([int]$s) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $m = if ($s -le 24) { [math]::Max(0.5, $s * 0.03) } else { $s * 0.06 }
    $box = $s - 2 * $m
    $rect = New-Object System.Windows.Rect($m, $m, $box, $box)
    $radius = $box * 0.27

    $bg = New-Object System.Windows.Media.LinearGradientBrush
    $bg.StartPoint = New-Object System.Windows.Point(0, 0)
    $bg.EndPoint = New-Object System.Windows.Point(1, 1)
    $bg.GradientStops.Add((New-Object System.Windows.Media.GradientStop((Color "#7C5CFF"), 0)))
    $bg.GradientStops.Add((New-Object System.Windows.Media.GradientStop((Color "#4C7DFF"), 0.55)))
    $bg.GradientStops.Add((New-Object System.Windows.Media.GradientStop((Color "#2BC8F0"), 1)))
    $dc.DrawRoundedRectangle((Freeze $bg), $null, $rect, $radius, $radius)

    # Soft top sheen.
    $sheen = New-Object System.Windows.Media.LinearGradientBrush
    $sheen.StartPoint = New-Object System.Windows.Point(0.5, 0)
    $sheen.EndPoint = New-Object System.Windows.Point(0.5, 0.6)
    $sheen.GradientStops.Add((New-Object System.Windows.Media.GradientStop((Color "#FFFFFF" 0.20), 0)))
    $sheen.GradientStops.Add((New-Object System.Windows.Media.GradientStop((Color "#FFFFFF" 0), 1)))
    $dc.DrawRoundedRectangle((Freeze $sheen), $null, $rect, $radius, $radius)

    # The island, snapped to whole pixels.
    $pw = [math]::Round($box * 0.56)
    $ph = [math]::Max(3, [math]::Round($box * 0.19))
    $px = [math]::Round(($s - $pw) / 2)
    $py = [math]::Round($m + $box * 0.25)

    if ($s -ge 32) {
        $glow = New-Object System.Windows.Media.RadialGradientBrush
        $glow.GradientStops.Add((New-Object System.Windows.Media.GradientStop((Color "#FFFFFF" 0.30), 0)))
        $glow.GradientStops.Add((New-Object System.Windows.Media.GradientStop((Color "#FFFFFF" 0), 1)))
        $center = New-Object System.Windows.Point(($s / 2), ($py + $ph + $box * 0.06))
        $dc.DrawEllipse((Freeze $glow), $null, $center, ($pw * 0.62), ($box * 0.16))
    }

    $pill = New-Object System.Windows.Rect($px, $py, $pw, $ph)
    $dc.DrawRoundedRectangle((Freeze (New-Object System.Windows.Media.SolidColorBrush((Color "#0B0D14")))), $null, $pill, ($ph / 2), ($ph / 2))

    if ($s -ge 48) {
        $edge = New-Object System.Windows.Media.Pen((Freeze (New-Object System.Windows.Media.SolidColorBrush((Color "#FFFFFF" 0.16)))), [math]::Max(1, $s / 128))
        $inset = $edge.Thickness / 2
        $dc.DrawRoundedRectangle($null, (Freeze $edge), (New-Object System.Windows.Rect(($m + $inset), ($m + $inset), ($box - 2 * $inset), ($box - 2 * $inset))), $radius, $radius)
    }
    $dc.Close()
    return $dv
}

# Tray glyph: a rounded "screen" outline with the island inside, in one color.
function Draw-TrayIcon([int]$s, [string]$hex) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $brush = Freeze (New-Object System.Windows.Media.SolidColorBrush((Color $hex)))
    $t = [math]::Max(1.5, [math]::Round($s / 13 * 2) / 2)
    $m = [math]::Round($s * 0.06) + $t / 2
    $box = $s - 2 * $m
    $radius = $box * 0.3
    $pen = Freeze (New-Object System.Windows.Media.Pen($brush, $t))
    $dc.DrawRoundedRectangle($null, $pen, (New-Object System.Windows.Rect($m, $m, $box, $box)), $radius, $radius)
    $pw = [math]::Round($s * 0.5)
    $ph = [math]::Max(2, [math]::Round($s * 0.19))
    $px = [math]::Round(($s - $pw) / 2)
    $py = [math]::Round($m + $t / 2 + $s * 0.1)
    $dc.DrawRoundedRectangle($brush, $null, (New-Object System.Windows.Rect($px, $py, $pw, $ph)), ($ph / 2), ($ph / 2))
    $dc.Close()
    return $dv
}

function To-Png($visual, [int]$s) {
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($s, $s, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return , $ms.ToArray()
}

# ICO with PNG-compressed entries (supported by Windows Vista and later).
function Write-Ico([string]$path, [int[]]$sizes, [scriptblock]$draw) {
    $images = foreach ($s in $sizes) { , (To-Png (& $draw $s) $s) }
    $fs = [System.IO.File]::Create($path)
    $w = New-Object System.IO.BinaryWriter($fs)
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]
        $dim = if ($s -ge 256) { 0 } else { $s }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $w.Write($img) }
    $w.Close()
}

if ($Preview) {
    # Contact sheet: app icon and tray glyphs at their real sizes on dark and light backgrounds.
    $sheet = New-Object System.Windows.Media.DrawingVisual
    $dc = $sheet.RenderOpen()
    $dc.DrawRectangle((New-Object System.Windows.Media.SolidColorBrush((Color "#1F1F23"))), $null, (New-Object System.Windows.Rect(0, 0, 720, 300)))
    $dc.DrawRectangle((New-Object System.Windows.Media.SolidColorBrush((Color "#F3F3F3"))), $null, (New-Object System.Windows.Rect(0, 300, 720, 300)))
    $x = 16
    foreach ($s in 16, 24, 32, 48, 64, 256) {
        foreach ($row in 0, 1) {
            $y = $row * 300 + 20
            $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($s, $s, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
            $bmp.Render((Draw-AppIcon $s))
            $dc.DrawImage($bmp, (New-Object System.Windows.Rect($x, $y, $s, $s)))
            if ($s -le 64) {
                $tray = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($s, $s, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
                $tray.Render((Draw-TrayIcon $s $(if ($row -eq 0) { "#FFFFFF" } else { "#1B1B1F" })))
                $dc.DrawImage($tray, (New-Object System.Windows.Rect($x, ($y + 200), $s, $s)))
            }
        }
        $x += $s + 24
    }
    $dc.Close()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(720, 600, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($sheet)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $fs = [System.IO.File]::Create((Join-Path $OutDir "icon-preview.png")); $enc.Save($fs); $fs.Close()
    Write-Host "Preview written to $OutDir"
    return
}

Write-Ico (Join-Path $OutDir "nova.ico") @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256) { param($s) Draw-AppIcon $s }
[System.IO.File]::WriteAllBytes((Join-Path $OutDir "nova-256.png"), (To-Png (Draw-AppIcon 256) 256))
Write-Ico (Join-Path $OutDir "nova-tray.ico") @(16, 20, 24, 32, 40, 48, 64) { param($s) Draw-TrayIcon $s "#FFFFFF" }
Write-Ico (Join-Path $OutDir "nova-tray-light.ico") @(16, 20, 24, 32, 40, 48, 64) { param($s) Draw-TrayIcon $s "#1B1B1F" }
Write-Host "Icons written to $OutDir"
