# Generates the status-effect icons (light bleed, heavy bleed, fracture).
# The vanilla EFT icons for these are nearly indistinguishable at HUD size, so
# these differ on three axes at once: shape, droplet count and color.
# Drawn at 64x64 (~2.5x display size). Rerun after tweaks, then rebuild.

Add-Type -AssemblyName System.Drawing

$iconDir = Join-Path $PSScriptRoot "..\..\ArenaMode.Client\Hud\InfoPanels\Icons"
New-Item -ItemType Directory -Force -Path $iconDir | Out-Null

$S = 64

$lightRed = [System.Drawing.Color]::FromArgb(255, 232, 96, 84)
$heavyRed = [System.Drawing.Color]::FromArgb(255, 198, 28, 28)
$boneWhite = [System.Drawing.Color]::FromArgb(255, 236, 232, 220)

function New-DropPath([float]$cx, [float]$cy, [float]$r) {
    # Circular belly plus a triangular tip whose base is the chord of the
    # circle at 0.38r above centre, so the two shapes meet without a seam.
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    # Winding, otherwise the overlap of belly and tip is punched back out.
    $p.FillMode = [System.Drawing.Drawing2D.FillMode]::Winding
    $p.AddEllipse(($cx - $r), ($cy - $r), (2 * $r), (2 * $r))
    $halfBase = $r * 0.925
    $baseY = $cy - $r * 0.38
    $pts = @(
        (New-Object System.Drawing.PointF ($cx - $halfBase), $baseY),
        (New-Object System.Drawing.PointF $cx, ($cy - $r * 2.35)),
        (New-Object System.Drawing.PointF ($cx + $halfBase), $baseY)
    )
    $p.AddPolygon($pts)
    return $p
}

function Add-BoneHalf($g, $brush, [float]$x, [float]$y, [float]$shaftLen, [bool]$knobsLeft) {
    # Shaft plus the two knuckles at one end; the other end is the break.
    $g.FillRectangle($brush, $x, ($y - 4), $shaftLen, 8)
    $knobX = $(if ($knobsLeft) { $x - 9 } else { $x + $shaftLen - 8 })
    $g.FillEllipse($brush, $knobX, ($y - 13), 17, 14)
    $g.FillEllipse($brush, $knobX, ($y - 1), 17, 14)
}

function New-Icon([string]$name, [scriptblock]$render) {
    $bmp = New-Object System.Drawing.Bitmap $S, $S
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    & $render $g
    $g.Dispose()
    $bmp.Save((Join-Path $iconDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# One medium drop.
New-Icon "status_bleed_light.png" {
    param($g)
    $b = New-Object System.Drawing.SolidBrush $lightRed
    $p = New-DropPath 32 40 16
    $g.FillPath($b, $p)
    $p.Dispose(); $b.Dispose()
}

# Two drops, larger and darker: reads as "more" even in peripheral vision.
New-Icon "status_bleed_heavy.png" {
    param($g)
    $b = New-Object System.Drawing.SolidBrush $heavyRed
    $p1 = New-DropPath 24 42 15
    $g.FillPath($b, $p1)
    $p2 = New-DropPath 48 30 10
    $g.FillPath($b, $p2)
    $p1.Dispose(); $p2.Dispose(); $b.Dispose()
}

# Two bone halves, offset and kinked apart: a snapped bone stays readable at
# 20px, where a punched-out hairline crack just turns into mush.
New-Icon "status_fracture.png" {
    param($g)
    $b = New-Object System.Drawing.SolidBrush $boneWhite

    $state = $g.Save()
    $g.TranslateTransform(32, 32)
    $g.RotateTransform(-25)
    $g.TranslateTransform(-32, -32)
    Add-BoneHalf $g $b 12 26 20 $true
    $g.Restore($state)

    $state = $g.Save()
    $g.TranslateTransform(32, 32)
    $g.RotateTransform(8)
    $g.TranslateTransform(-32, -32)
    Add-BoneHalf $g $b 36 40 20 $false
    $g.Restore($state)

    $b.Dispose()
}

# --- Preview: icons at 2x and at true HUD size on a dark panel ---
$sheet = New-Object System.Drawing.Bitmap 380, 160
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$sg.Clear([System.Drawing.Color]::FromArgb(255, 20, 23, 27))

$font = New-Object System.Drawing.Font "Bahnschrift", 11
$fg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 225, 230, 236))
$x = 24
foreach ($pair in @(@("status_bleed_light.png", "L.ARM"), @("status_bleed_heavy.png", "CHEST"), @("status_fracture.png", "R.LEG"))) {
    $img = [System.Drawing.Image]::FromFile((Join-Path $iconDir $pair[0]))
    $sg.DrawImage($img, (New-Object System.Drawing.RectangleF $x, 20, 56, 56))
    # true size, as it appears next to the label in the HUD
    $sg.DrawImage($img, (New-Object System.Drawing.RectangleF $x, 100, 20, 20))
    $sg.DrawString($pair[1], $font, $fg, ($x + 24), 103)
    $img.Dispose()
    $x += 118
}
$sg.Dispose()
$sheet.Save((Join-Path $iconDir "status_preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()

Write-Host "Status icons written to $iconDir"
