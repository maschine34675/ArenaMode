# Generates the two armor-row glyphs for the status panel: Skoll's Kevlar vest
# and Stahlhelm (CC BY 3.0; see Assets/Ui/NOTICE.md).
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
# A colored glyph here would fight that tint. Both keep the previous main
# silhouette's alpha (232), neutral white tint and 64px canvas.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$iconDir = Join-Path $PSScriptRoot "..\..\ArenaMode.Client\Hud\InfoPanels\Icons"
# Previews stay out of the source tree (artifacts/ is ignored).
$previewDir = Join-Path $PSScriptRoot "..\..\artifacts\raid-panel-icons"
New-Item -ItemType Directory -Force -Path $iconDir, $previewDir | Out-Null

$S = 64

# Rasterize the two checked-in, unchanged Game-icons.net SVGs.
# WPF reads each SVG's one path (including arcs) without a third-party rasterizer;
# it is an offline generator dependency only, never a game/runtime dependency.
function New-ArmorIcon([string]$sourceName, [string]$outputName) {
    [xml]$sourceSvg = Get-Content -LiteralPath (Join-Path $PSScriptRoot $sourceName) -Raw
    $geometry = [System.Windows.Media.Geometry]::Parse($sourceSvg.svg.path.d)
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform([System.Windows.Media.ScaleTransform]::new($S / 512.0, $S / 512.0))
    $brush = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromArgb(232, 255, 255, 255))
    $drawing.DrawGeometry($brush, $null, $geometry)
    $drawing.Pop()
    $drawing.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($S, $S, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $outputPath = Join-Path $iconDir $outputName
    $stream = [System.IO.File]::Create($outputPath)
    try { $encoder.Save($stream) }
    finally { $stream.Dispose() }
    return $outputPath
}

$bodyPath = New-ArmorIcon 'kevlar-vest.svg' 'armor_body.png'
$helmetPath = New-ArmorIcon 'stahlhelm.svg' 'armor_helmet.png'

# --- Preview: 72px and true HUD size, on the panel's dark glass tone, each
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
    $sg.DrawString("20px gray / red", $font, $fg, ($x - 4), 128)
    $x += 150
}
$sg.Dispose()
$sheet.Save((Join-Path $previewDir "armor_preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
$fg.Dispose()

Write-Host "Armor icons written to $iconDir; preview in $previewDir"
