# Generates the panel chrome sprites: a rounded-rectangle fill and a matching
# 1px rim outline, both white so they can be tinted at runtime. Drawn at 64x64
# and used as 9-sliced sprites, so the corner radius stays constant no matter
# how large a panel gets. Rerun after tweaks, then rebuild.

Add-Type -AssemblyName System.Drawing

$iconDir = Join-Path $PSScriptRoot "..\..\ArenaMode.Client\Hud\InfoPanels\Icons"
New-Item -ItemType Directory -Force -Path $iconDir | Out-Null

$size = 64
$radius = 14
# Must stay >= radius so the 9-slice corners contain the whole curve.
$border = 20

function New-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-PanelSprite([string]$name, [scriptblock]$render) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    & $render $g
    $g.Dispose()
    $bmp.Save((Join-Path $iconDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

# Solid fill, inset by half a pixel so the antialiased edge stays clean.
New-PanelSprite "panel_bg.png" {
    param($g)
    $p = New-RoundedRectPath 0.5 0.5 ($size - 1) ($size - 1) $radius
    $g.FillPath($white, $p)
    $p.Dispose()
}

# Rim light: a hairline stroke along the same silhouette.
New-PanelSprite "panel_stroke.png" {
    param($g)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White, 1.6)
    $p = New-RoundedRectPath 1 1 ($size - 2) ($size - 2) $radius
    $g.DrawPath($pen, $p)
    $p.Dispose(); $pen.Dispose()
}

$white.Dispose()

# --- Preview: old flat box vs new glass panel over a bright-ish scene ---

$sheet = New-Object System.Drawing.Bitmap 460, 150
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
# Fake game background so translucency is judgeable
$bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
    (New-Object System.Drawing.Rectangle 0, 0, 460, 150),
    [System.Drawing.Color]::FromArgb(255, 96, 104, 92),
    [System.Drawing.Color]::FromArgb(255, 38, 42, 46),
    35)
$sg.FillRectangle($bgBrush, 0, 0, 460, 150)

$textFont = New-Object System.Drawing.Font "Bahnschrift", 13, ([System.Drawing.FontStyle]::Regular)
$titleFont = New-Object System.Drawing.Font "Bahnschrift", 14, ([System.Drawing.FontStyle]::Bold)
$textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245, 235, 238, 242))
$dimBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(190, 200, 208, 218))

# Old: two stacked hard-edged rectangles, heavy alpha
$oldA = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(199, 5, 8, 10))
$oldB = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(89, 31, 41, 51))
$sg.FillRectangle($oldA, 24, 30, 190, 90)
$sg.FillRectangle($oldB, 24, 30, 190, 90)
$sg.DrawString("M4A1", $titleFont, $textBrush, 34, 40)
$sg.DrawString("29/30 +1", $textFont, $textBrush, 34, 64)
$sg.DrawString("5.56x45 | Single", $textFont, $dimBrush, 34, 88)

# New: single rounded translucent panel with a rim light
$newFill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(140, 10, 13, 17))
$rimPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(46, 255, 255, 255)), 1.6
$panel = New-RoundedRectPath 246 30 190 90 14
$sg.FillPath($newFill, $panel)
$sg.DrawPath($rimPen, $panel)
$sg.DrawString("M4A1", $titleFont, $textBrush, 258, 40)
$sg.DrawString("29/30 +1", $textFont, $textBrush, 258, 64)
$sg.DrawString("5.56x45 | Single", $textFont, $dimBrush, 258, 88)
$panel.Dispose()

$sg.Dispose()
$sheet.Save((Join-Path $iconDir "panel_preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()

Write-Host "Panel sprites written to $iconDir (border for 9-slice: $border)"
