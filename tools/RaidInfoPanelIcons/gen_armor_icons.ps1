# Generates the two armor-row glyphs for the status panel: Skoll's Kevlar vest
# (CC BY 3.0; see Assets/ArenaMode/HUD-ICON-LICENSES.md) and the original combat helmet.
# Deliberately a separate script, in the style of gen_grenades.ps1: rerunning
# the four imported generators would re-encode all of the source-identical
# icons and break their recorded hashes (tools/RaidInfoPanelChecks/SOURCE-IMPORT.json).
# This one writes only its own two files. Their hashes live in
# tools/RaidInfoPanelChecks/ARENA-ICONS.json; rerun, then update that record,
# then rebuild.
#
# Unlike the grenade icons (which keep their own baked-in color and are only
# ever alpha-dimmed), these two are drawn white-on-transparent: StatRowView
# tints whatever icon it is given with the row's live text color
# (health/weight/warning), the same way the native HP and weight icons behave.
# A colored glyph here would fight that tint. Alpha alone (not hue) gives each
# shape its internal shading, so the tint still reads as one clean color.
#
# NOTE: scriptblock parameters below are named distinctly from every outer
# variable they might otherwise capture (PowerShell dynamic scoping would
# resolve a same-named parameter to the enclosing scope and recurse forever;
# see gen_puppet.ps1). GraphicsPath shapes here never self-intersect, so the
# default Alternate FillMode never punches an unwanted hole; a shape that ever
# needs to (e.g. a visor cutout) must set Winding explicitly.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$iconDir = Join-Path $PSScriptRoot "..\..\ArenaMode.Client\Hud\InfoPanels\Icons"
# Previews stay out of the source tree (artifacts/ is ignored).
$previewDir = Join-Path $PSScriptRoot "..\..\artifacts\raid-panel-icons"
New-Item -ItemType Directory -Force -Path $iconDir, $previewDir | Out-Null

$S = 64

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

function Fill-RoundedRect($gfx, $brush, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-RoundedRectPath $x $y $w $h $r
    $gfx.FillPath($brush, $path)
    $path.Dispose()
}

function White([int]$alpha) {
    return New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($alpha, 255, 255, 255))
}

function New-Icon([string]$name, [scriptblock]$render) {
    $bmp = New-Object System.Drawing.Bitmap $S, $S
    $canvas = [System.Drawing.Graphics]::FromImage($bmp)
    $canvas.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $canvas.Clear([System.Drawing.Color]::Transparent)
    & $render $canvas
    $canvas.Dispose()
    $path = Join-Path $iconDir $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $path
}

# Body: rasterize the checked-in, unchanged Game-icons.net SVG. Its open neck,
# separate shoulder straps and paired side fasteners remain readable at 20px.
# WPF reads this SVG's one path (including arcs) without a third-party rasterizer;
# it is an offline generator dependency only, never a game/runtime dependency.
# Keep the old main silhouette's alpha (232), neutral white tint and 64px canvas.
[xml]$vestSvg = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'kevlar-vest.svg') -Raw
$vestGeometry = [System.Windows.Media.Geometry]::Parse($vestSvg.svg.path.d)
$vestVisual = [System.Windows.Media.DrawingVisual]::new()
$vestDrawing = $vestVisual.RenderOpen()
$vestDrawing.PushTransform([System.Windows.Media.ScaleTransform]::new($S / 512.0, $S / 512.0))
$vestBrush = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromArgb(232, 255, 255, 255))
$vestDrawing.DrawGeometry($vestBrush, $null, $vestGeometry)
$vestDrawing.Pop()
$vestDrawing.Close()
$vestBitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($S, $S, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
$vestBitmap.Render($vestVisual)
$vestEncoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
$vestEncoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($vestBitmap))
$bodyPath = Join-Path $iconDir 'armor_body.png'
$vestStream = [System.IO.File]::Create($bodyPath)
try { $vestEncoder.Save($vestStream) }
finally { $vestStream.Dispose() }

# Helmet: a combat-helmet dome with a brow band across the front (brighter,
# with the original two-tone shading) and two solid ear-pro pads built
# into its lower sides. Filled pads instead of thin strap strokes: a hairline
# at 64px all but disappears once downscaled to ~20px and read as stray legs
# rather than part of the helmet (first pass, see armor_preview.png history).
$helmetPath = New-Icon "armor_helmet.png" {
    param($canvas)
    $dome = White 232
    $canvas.FillPie($dome, 12, 9, 40, 38, 180, 180)
    Fill-RoundedRect $canvas $dome 12 27 40 9 4
    $dome.Dispose()

    $ear = White 220
    Fill-RoundedRect $canvas $ear 9 29 9 16 4
    Fill-RoundedRect $canvas $ear 46 29 9 16 4
    $ear.Dispose()

    $brow = White 255
    Fill-RoundedRect $canvas $brow 16 29 32 6 3
    $brow.Dispose()
}

# --- Preview: 2x and true HUD size, on the panel's dark glass tone, each
# swatch also shown tinted the way StatRowView actually uses it (weight gray,
# warning red) so a color clash would show up here before the game does. ---
$sheet = New-Object System.Drawing.Bitmap 320, 170
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$sg.Clear([System.Drawing.Color]::FromArgb(255, 20, 23, 27))
$font = New-Object System.Drawing.Font "Bahnschrift", 12
$fg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 232, 236, 240))

function Draw-TintedCopy($sg, [string]$file, [float]$x, [float]$y, [float]$size, [System.Drawing.Color]$tint) {
    $src = [System.Drawing.Image]::FromFile($file)
    $attrs = New-Object System.Drawing.Imaging.ImageAttributes
    $matrix = New-Object System.Drawing.Imaging.ColorMatrix
    $matrix.Matrix00 = $tint.R / 255.0; $matrix.Matrix11 = $tint.G / 255.0; $matrix.Matrix22 = $tint.B / 255.0
    $attrs.SetColorMatrix($matrix)
    $destRect = New-Object System.Drawing.Rectangle ([int]$x), ([int]$y), ([int]$size), ([int]$size)
    $sg.DrawImage($src, $destRect, 0, 0, $src.Width, $src.Height, [System.Drawing.GraphicsUnit]::Pixel, $attrs)
    $src.Dispose(); $attrs.Dispose()
}

$weightGray = [System.Drawing.Color]::FromArgb(255, 209, 219, 230)
$warningRed = [System.Drawing.Color]::FromArgb(255, 250, 89, 71)
$x = 20
foreach ($p in @($bodyPath, $helmetPath)) {
    $img = [System.Drawing.Image]::FromFile($p)
    $sg.DrawImage($img, (New-Object System.Drawing.RectangleF $x, 14, 72, 72))
    $img.Dispose()
    Draw-TintedCopy $sg $p ($x + 4) 100 20 $weightGray
    Draw-TintedCopy $sg $p ($x + 32) 100 20 $warningRed
    $sg.DrawString("20px @ weight / warning", $font, $fg, ($x - 4), 128)
    $x += 150
}
$sg.Dispose()
$sheet.Save((Join-Path $previewDir "armor_preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
$fg.Dispose()

Write-Host "Armor icons written to $iconDir; preview in $previewDir"
