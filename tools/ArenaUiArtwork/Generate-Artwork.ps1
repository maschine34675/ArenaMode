# Offline renderer for this small, explicit SVG subset. No game/network/runtime dependency.
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\ArenaMode.Client\Assets\Ui'),
    [string]$PreviewDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\ui-art-replacement-20261016\artwork')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Drawing
$invariant = [System.Globalization.CultureInfo]::InvariantCulture
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$PreviewDirectory = [System.IO.Path]::GetFullPath($PreviewDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory, $PreviewDirectory | Out-Null
$guns = @('assault-rifle', 'carbine', 'sniper', 'marksman', 'machinegun', 'shotgun', 'pistol', 'smg')
$symbols = @('kill', 'death', 'assist', 'headshot', 'grenade', 'melee', 'bleeding', 'fire', 'barbed-wire')
$names = @($guns) + @($symbols) + @('team-panel')
function Number([string]$text) { return [double]::Parse($text, $invariant) }
function New-SvgBrush($node, $svg) {
    $fill = [string]$node.GetAttribute('fill')
    if ($fill -eq '#ffffff') {
        $brush = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Colors]::White)
    } elseif ($fill -eq 'url(#fade)') {
        $gradient = $svg.SelectSingleNode("*[local-name()='defs']/*[local-name()='linearGradient' and @id='fade']")
        if ($null -eq $gradient) { throw 'Missing fade gradient.' }
        $brush = [System.Windows.Media.LinearGradientBrush]::new()
        $brush.MappingMode = [System.Windows.Media.BrushMappingMode]::Absolute
        $brush.StartPoint = [System.Windows.Point]::new((Number $gradient.x1), (Number $gradient.y1))
        $brush.EndPoint = [System.Windows.Point]::new((Number $gradient.x2), (Number $gradient.y2))
        foreach ($stop in $gradient.ChildNodes) {
            if ($stop.LocalName -ne 'stop' -or $stop.GetAttribute('stop-color') -ne '#ffffff') { throw 'Only white alpha gradient stops are supported.' }
            $alpha = [byte][Math]::Round(255 * (Number $stop.GetAttribute('stop-opacity')))
            $brush.GradientStops.Add([System.Windows.Media.GradientStop]::new(
                [System.Windows.Media.Color]::FromArgb($alpha, 255, 255, 255), (Number $stop.offset)))
        }
    } else { throw "Unsupported SVG fill '$fill'." }
    $opacity = $node.GetAttribute('opacity')
    if ($opacity) { $brush.Opacity = Number $opacity }
    return $brush
}
$assets = [System.Collections.Generic.List[object]]::new()
foreach ($name in $names) {
    $sourceFile = "tools/ArenaUiArtwork/sources/$name.svg"
    $sourcePath = Join-Path $repo $sourceFile
    [xml]$document = Get-Content -LiteralPath $sourcePath -Raw
    $svg = $document.DocumentElement
    $width = [int]$svg.width; $height = [int]$svg.height
    $expectedWidth = if ($name -eq 'team-panel') { 688 } elseif ($guns -contains $name) { 128 } else { 64 }
    $expectedHeight = if ($name -eq 'team-panel') { 107 } else { 64 }
    if ($width -ne $expectedWidth -or $height -ne $expectedHeight -or $svg.viewBox -ne "0 0 $width $height") { throw "Unexpected SVG dimensions for $name." }
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    foreach ($node in $svg.ChildNodes) {
        if ($node.LocalName -in @('title', 'desc', 'defs')) { continue }
        if ($node.LocalName -ne 'path') { throw "Unsupported SVG element $($node.LocalName)." }
        $geometry = [System.Windows.Media.Geometry]::Parse($node.GetAttribute('d')).CloneCurrentValue()
        $geometry.FillRule = if ($node.GetAttribute('fill-rule') -eq 'evenodd') { [System.Windows.Media.FillRule]::EvenOdd } else { [System.Windows.Media.FillRule]::Nonzero }
        $drawing.DrawGeometry((New-SvgBrush $node $svg), $null, $geometry)
    }
    $drawing.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($width, $height, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    # Preserve WPF's exact coverage/gradient alpha, but use straight white RGB.
    # Direct Pbgra32 encoding otherwise rounds some antialiased whites to254.
    $stride = $width * 4
    $pixels = [byte[]]::new($stride * $height)
    $bitmap.CopyPixels($pixels, $stride, 0)
    for ($pixel = 0; $pixel -lt $pixels.Length; $pixel += 4) {
        $pixels[$pixel] = 255; $pixels[$pixel + 1] = 255; $pixels[$pixel + 2] = 255
    }
    $straight = [System.Windows.Media.Imaging.BitmapSource]::Create($width, $height, 96, 96,
        [System.Windows.Media.PixelFormats]::Bgra32, $null, $pixels, $stride)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($straight))
    $outputPath = Join-Path $OutputDirectory "$name.png"
    $stream = [System.IO.File]::Create($outputPath)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    $assets.Add([ordered]@{
        file = "$name.png"
        sha256 = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash
        pixelSize = @($width, $height)
        author = 'maschine / ArenaMode contributors'
        license = 'MIT'
        sourceURL = "urn:arenamode:original-ui-artwork:20261016:$name"
        sourceFile = $sourceFile
        sourceSha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
        modifications = 'Original geometric SVG, authored for ArenaMode with Codex assistance; rasterized offline with WPF, straight RGB normalized to white while retaining rendered alpha. No game artwork, tracing or third-party visual source.'
    })
}
[ordered]@{
    schemaVersion = 2
    source = 'Original ArenaMode categorical UI artwork'
    rights = 'MIT; full permission and copyright notice in MIT-LICENSE.txt. No extracted game artwork.'
    generator = 'tools/ArenaUiArtwork/Generate-Artwork.ps1'
    assets = @($assets)
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SOURCES.json') -Encoding utf8

# Review-only contact sheet; the labeled HUD swatches are rendered at real pixels.
$sheet = [System.Drawing.Bitmap]::new(1080, 990, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$canvas = [System.Drawing.Graphics]::FromImage($sheet)
$canvas.Clear([System.Drawing.Color]::FromArgb(255, 22, 26, 31))
$canvas.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$canvas.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$font = [System.Drawing.Font]::new('Segoe UI', 10)
$titleFont = [System.Drawing.Font]::new('Segoe UI', 16, [System.Drawing.FontStyle]::Bold)
$light = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(240, 240, 244))
$muted = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(175, 185, 197))
$dark = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(16, 21, 27))
$bright = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(193, 197, 187))
$panel = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(220, 20, 25, 31))
$canvas.DrawString('ARENA  /  Original categorical UI artwork', $titleFont, $light, 22, 16)
$canvas.DrawString('Left: source size. Right: actual HUD pixels on dark / bright-scene glass, including team tints.', $font, $muted, 22, 48)
function Draw-Icon([string]$name, [int]$x, [int]$y, [int]$width, [int]$height, [System.Drawing.Color]$tint) {
    $image = [System.Drawing.Image]::FromFile((Join-Path $OutputDirectory "$name.png"))
    $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
    $matrix = [System.Drawing.Imaging.ColorMatrix]::new()
    $matrix.Matrix00 = $tint.R / 255.0; $matrix.Matrix11 = $tint.G / 255.0; $matrix.Matrix22 = $tint.B / 255.0
    $attributes.SetColorMatrix($matrix)
    try { $canvas.DrawImage($image, [System.Drawing.Rectangle]::new($x, $y, $width, $height), 0, 0, $image.Width, $image.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes) }
    finally { $image.Dispose(); $attributes.Dispose() }
}
$white = [System.Drawing.Color]::White
$blue = [System.Drawing.Color]::FromArgb(107, 185, 244)
$red = [System.Drawing.Color]::FromArgb(243, 104, 98)
for ($i = 0; $i -lt $guns.Count; $i++) {
    $x = 22 + 530 * ($i % 2); $y = 84 + 94 * [Math]::Floor($i / 2)
    $canvas.FillRectangle($dark, $x, $y, 505, 86)
    $canvas.DrawString($guns[$i], $font, $muted, $x + 9, $y + 5)
    Draw-Icon $guns[$i] ($x + 8) ($y + 20) 128 64 $white
    $canvas.DrawString('44x22', $font, $muted, $x + 166, $y + 8)
    Draw-Icon $guns[$i] ($x + 170) ($y + 38) 44 22 $white
    $canvas.FillRectangle($bright, $x + 252, $y + 28, 90, 44)
    $canvas.FillRectangle($panel, $x + 256, $y + 32, 82, 36)
    Draw-Icon $guns[$i] ($x + 275) ($y + 39) 44 22 $white
    Draw-Icon $guns[$i] ($x + 368) ($y + 39) 44 22 $blue
    Draw-Icon $guns[$i] ($x + 444) ($y + 39) 44 22 $red
}
for ($i = 0; $i -lt $symbols.Count; $i++) {
    $x = 22 + 352 * ($i % 3); $y = 474 + 103 * [Math]::Floor($i / 3)
    $canvas.FillRectangle($dark, $x, $y, 329, 94)
    $canvas.DrawString($symbols[$i], $font, $muted, $x + 8, $y + 4)
    Draw-Icon $symbols[$i] ($x + 8) ($y + 25) 64 64 $white
    $canvas.DrawString('18px', $font, $muted, $x + 92, $y + 29)
    Draw-Icon $symbols[$i] ($x + 102) ($y + 54) 18 18 $white
    $canvas.FillRectangle($bright, $x + 153, $y + 44, 44, 38)
    $canvas.FillRectangle($panel, $x + 157, $y + 48, 36, 30)
    Draw-Icon $symbols[$i] ($x + 166) ($y + 54) 18 18 $white
    Draw-Icon $symbols[$i] ($x + 224) ($y + 54) 18 18 $blue
    Draw-Icon $symbols[$i] ($x + 286) ($y + 54) 18 18 $red
}
$canvas.DrawString('Kill confirmation 32x32', $font, $muted, 22, 802)
Draw-Icon 'kill' 73 829 32 32 $white
$canvas.DrawString('Team banner 688x107, tinted blue; no underlying text baked in', $font, $muted, 270, 802)
Draw-Icon 'team-panel' 270 827 688 107 $blue
$canvas.DrawString('Preview only: mock backgrounds/tints, no game capture. Runtime placements and team colors are unchanged.', $font, $muted, 22, 957)
$canvas.Dispose()
$sheet.Save((Join-Path $PreviewDirectory 'contact-sheet.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
$font.Dispose(); $titleFont.Dispose(); $light.Dispose(); $muted.Dispose(); $dark.Dispose(); $bright.Dispose(); $panel.Dispose()
Write-Output "ARENA_UI_ARTWORK_GENERATED: $($assets.Count) PNGs and SOURCES.json; previews in $PreviewDirectory"
