# Generates the three spare-magazine icons (full / partial / backpack) as PNGs
# under ArenaMode.Client/Hud/InfoPanels/Icons. Embedded at build time; rerun this
# script after tweaking the geometry below, then rebuild.
#
# Icons are drawn at 96x120 (4x the ~24x30 display size) so the bilinear
# downscale in-game keeps the edges smooth.

Add-Type -AssemblyName System.Drawing

$iconDir = Join-Path $PSScriptRoot "..\..\ArenaMode.Client\Hud\InfoPanels\Icons"
New-Item -ItemType Directory -Force -Path $iconDir | Out-Null

$W = 96
$H = 120

# Palette
$magWall   = [System.Drawing.Color]::FromArgb(255, 208, 213, 221)
$magInner  = [System.Drawing.Color]::FromArgb(235, 26, 28, 32)
$basePlate = [System.Drawing.Color]::FromArgb(255, 160, 166, 176)
$brass     = [System.Drawing.Color]::FromArgb(255, 202, 158, 62)
$tipColor  = [System.Drawing.Color]::FromArgb(255, 216, 216, 208)
$packBody  = [System.Drawing.Color]::FromArgb(255, 196, 148, 78)
$packDark  = [System.Drawing.Color]::FromArgb(255, 58, 46, 30)
$packRing  = [System.Drawing.Color]::FromArgb(255, 14, 15, 18)

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

function Draw-Cartridge($g, [float]$centerY, [int]$alpha) {
    $brassBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($alpha, $brass))
    $tipBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($alpha, $tipColor))
    $case = New-RoundedRectPath 25 ($centerY - 6) 33 12 3
    $g.FillPath($brassBrush, $case)
    $g.FillEllipse($tipBrush, 52, ($centerY - 6), 19, 12)
    $case.Dispose(); $brassBrush.Dispose(); $tipBrush.Dispose()
}

function Draw-MagazineBase($g) {
    # Body wall + dark interior
    $bodyOuter = New-RoundedRectPath 17 5 62 100 10
    $bodyInner = New-RoundedRectPath 23 11 50 88 6
    $wallBrush = New-Object System.Drawing.SolidBrush $magWall
    $innerBrush = New-Object System.Drawing.SolidBrush $magInner
    $g.FillPath($wallBrush, $bodyOuter)
    $g.FillPath($innerBrush, $bodyInner)
    # Baseplate
    $plate = New-RoundedRectPath 11 100 74 16 6
    $plateBrush = New-Object System.Drawing.SolidBrush $basePlate
    $g.FillPath($plateBrush, $plate)
    $bodyOuter.Dispose(); $bodyInner.Dispose(); $plate.Dispose()
    $wallBrush.Dispose(); $innerBrush.Dispose(); $plateBrush.Dispose()
}

function Draw-BackpackBadge($g) {
    # The badge must stay upright and corner-anchored, so it is drawn without
    # the magazine's lean.
    $g.ResetTransform()
    # Dark ring first so the badge separates cleanly from the magazine behind it
    $ring = New-RoundedRectPath 38 56 58 62 16
    $ringBrush = New-Object System.Drawing.SolidBrush $packRing
    $g.FillPath($ringBrush, $ring)
    # Pack body
    $body = New-RoundedRectPath 44 68 46 44 12
    $bodyBrush = New-Object System.Drawing.SolidBrush $packBody
    $g.FillPath($bodyBrush, $body)
    # Top flap (dome)
    $g.FillPie($bodyBrush, 49, 58, 36, 30, 180, 180)
    # Straps
    $strapPen = New-Object System.Drawing.Pen ($packDark, 5)
    $g.DrawLine($strapPen, 57, 74, 57, 108)
    $g.DrawLine($strapPen, 77, 74, 77, 108)
    $ring.Dispose(); $body.Dispose(); $ringBrush.Dispose(); $bodyBrush.Dispose(); $strapPen.Dispose()
}

function New-Icon([string]$name, [scriptblock]$draw) {
    $bmp = New-Object System.Drawing.Bitmap $W, $H
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    # Slight rightward lean anchored at the baseplate so the silhouette reads
    # as a curved rifle magazine rather than a battery.
    $shear = New-Object System.Drawing.Drawing2D.Matrix (1, 0, 0.14, 1, (-0.14 * $H), 0)
    $g.Transform = $shear
    & $draw $g
    $g.ResetTransform()
    $g.Dispose()
    $path = Join-Path $iconDir $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $path
}

# Cartridge row centers, bottom-up inside the interior (y 11..99)
$rows = @(91, 75, 59, 43, 27)

$fullPath = New-Icon "mag_full.png" {
    param($g)
    Draw-MagazineBase $g
    foreach ($y in $rows) { Draw-Cartridge $g $y 255 }
}

$partialPath = New-Icon "mag_partial.png" {
    param($g)
    Draw-MagazineBase $g
    Draw-Cartridge $g $rows[0] 255
    Draw-Cartridge $g $rows[1] 255
}

$backpackPath = New-Icon "mag_backpack.png" {
    param($g)
    Draw-MagazineBase $g
    # Dimmed cartridges: "not empty", but the badge is the message
    Draw-Cartridge $g $rows[0] 110
    Draw-Cartridge $g $rows[1] 110
    Draw-Cartridge $g $rows[2] 110
    Draw-BackpackBadge $g
}

# Loose rounds: for shell-by-shell weapons (shotguns, bolt actions) there are
# no spare magazines to show, so the bar shows carried cartridges instead.
# Drawn upright - the shear that gives magazines their curve would only make
# single cartridges look skewed.
$roundsReadyPath = New-Icon "rounds_ready.png" {
    param($g)
    $g.ResetTransform()
    Draw-Cartridge $g 34 255
    Draw-Cartridge $g 58 255
    Draw-Cartridge $g 82 255
}

$roundsBackpackPath = New-Icon "rounds_backpack.png" {
    param($g)
    $g.ResetTransform()
    Draw-Cartridge $g 34 110
    Draw-Cartridge $g 58 110
    Draw-Cartridge $g 82 110
    Draw-BackpackBadge $g
}

# Contact sheet: 4x view plus 1x-scale row on a HUD-dark background
$sheet = New-Object System.Drawing.Bitmap 780, 300
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.Clear([System.Drawing.Color]::FromArgb(255, 18, 20, 24))
$sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$x = 30
foreach ($p in @($fullPath, $partialPath, $backpackPath, $roundsReadyPath, $roundsBackpackPath)) {
    $icon = [System.Drawing.Image]::FromFile($p)
    $sg.DrawImage($icon, (New-Object System.Drawing.RectangleF $x, 20, 96, 120))
    $sg.DrawImage($icon, (New-Object System.Drawing.RectangleF ($x + 36), 200, 24, 30))
    $icon.Dispose()
    $x += 150
}
$sheetPath = Join-Path $iconDir "preview.png"
$sg.Dispose()
$sheet.Save($sheetPath, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()

Write-Host "Icons written to $iconDir"
