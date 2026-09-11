# Generates the armor paper-doll sprites: a neutral body silhouette plus one
# tintable (white) overlay per fine armor zone. Band overlays (throat, thorax,
# stomach, pelvis) are reused by the front and back figure; sides, arm and leg
# segments exist only on the front figure. Drawn at 128x256 (~2x display size).
# Rerun after tweaks, then rebuild.

Add-Type -AssemblyName System.Drawing

$iconDir = Join-Path $PSScriptRoot "..\..\ArenaMode.Client\Hud\InfoPanels\Icons"
New-Item -ItemType Directory -Force -Path $iconDir | Out-Null

$W = 128
$H = 256

$baseColor = [System.Drawing.Color]::FromArgb(210, 96, 101, 110)
$white = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)

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

function Fill-Rounded($g, $brush, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-RoundedRectPath $x $y $w $h $r
    $g.FillPath($brush, $p)
    $p.Dispose()
}

# --- Base silhouette parts ---

function Draw-BaseSilhouette($g, $brush) {
    $g.FillEllipse($brush, 43, 4, 42, 42)                    # head
    $g.FillRectangle($brush, 56, 42, 16, 14)                 # neck
    Fill-Rounded $g $brush 38 54 52 118 12                   # torso
    Draw-ArmTransformed $g "left"  { param($gg, $bb) Fill-Rounded $gg $bb -9 0 18 92 9 } $brush
    Draw-ArmTransformed $g "right" { param($gg, $bb) Fill-Rounded $gg $bb -9 0 18 92 9 } $brush
    Fill-Rounded $g $brush 41 176 20 76 10                   # left leg
    Fill-Rounded $g $brush 67 176 20 76 10                   # right leg
}

function Draw-ArmTransformed($g, [string]$side, [scriptblock]$draw, $brush) {
    $state = $g.Save()
    if ($side -eq "left") { $g.TranslateTransform(27, 58); $g.RotateTransform(7) }
    else { $g.TranslateTransform(101, 58); $g.RotateTransform(-7) }
    & $draw $g $brush
    $g.Restore($state)
}

# --- Fine zone shapes (inset against the base so seams stay visible) ---

$zones = [ordered]@{
    "head"           = { param($g, $b) $g.FillEllipse($b, 46, 7, 36, 36) }
    "throat"         = { param($g, $b) Fill-Rounded $g $b 57 43 14 12 3 }
    "thorax"         = { param($g, $b) Fill-Rounded $g $b 48 57 32 50 5 }
    "stomach"        = { param($g, $b) Fill-Rounded $g $b 48 110 32 32 5 }
    "pelvis"         = { param($g, $b) Fill-Rounded $g $b 48 145 32 24 5 }
    "side_left"      = { param($g, $b) Fill-Rounded $g $b 40 57 6 85 3 }
    "side_right"     = { param($g, $b) Fill-Rounded $g $b 82 57 6 85 3 }
    "arm_upper_left" = { param($g, $b) Draw-ArmTransformed $g "left"  { param($gg, $bb) Fill-Rounded $gg $bb -7 3 14 42 7 } $b }
    "arm_fore_left"  = { param($g, $b) Draw-ArmTransformed $g "left"  { param($gg, $bb) Fill-Rounded $gg $bb -7 49 14 40 7 } $b }
    "arm_upper_right"= { param($g, $b) Draw-ArmTransformed $g "right" { param($gg, $bb) Fill-Rounded $gg $bb -7 3 14 42 7 } $b }
    "arm_fore_right" = { param($g, $b) Draw-ArmTransformed $g "right" { param($gg, $bb) Fill-Rounded $gg $bb -7 49 14 40 7 } $b }
    "thigh_left"     = { param($g, $b) Fill-Rounded $g $b 43 179 16 33 8 }
    "calf_left"      = { param($g, $b) Fill-Rounded $g $b 43 215 16 34 8 }
    "thigh_right"    = { param($g, $b) Fill-Rounded $g $b 69 179 16 33 8 }
    "calf_right"     = { param($g, $b) Fill-Rounded $g $b 69 215 16 34 8 }
}

# NOTE: parameter must not be named like variables referenced inside passed
# scriptblocks (PowerShell dynamic scoping would resolve them to this
# parameter and recurse infinitely).
function New-Png([string]$name, [scriptblock]$render) {
    $bmp = New-Object System.Drawing.Bitmap $W, $H
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    & $render $g
    $g.Dispose()
    $bmp.Save((Join-Path $iconDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$baseBrush = New-Object System.Drawing.SolidBrush $baseColor
$whiteBrush = New-Object System.Drawing.SolidBrush $white

New-Png "armor_base.png" { param($g) Draw-BaseSilhouette $g $baseBrush }
foreach ($entry in $zones.GetEnumerator()) {
    $draw = $entry.Value
    New-Png "armor_zone_$($entry.Key).png" { param($g) & $draw $g $whiteBrush }
}

# --- Preview: front + back figure with a gradient-like tint spread ---

function Tint([int]$r, [int]$gr, [int]$b) {
    return New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(242, $r, $gr, $b))
}

$sheet = New-Object System.Drawing.Bitmap 340, 190
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$sg.Clear([System.Drawing.Color]::FromArgb(255, 18, 20, 24))

function Draw-PreviewFigure($g, [float]$ox, [float]$oy, [float]$scale, $tints) {
    $state = $g.Save()
    $g.TranslateTransform($ox, $oy)
    $g.ScaleTransform($scale, $scale)
    Draw-BaseSilhouette $g $baseBrush
    foreach ($key in $tints.Keys) {
        $brush = $tints[$key]
        & $zones[$key] $g $brush
    }
    $g.Restore($state)
}

$green = Tint 88 199 91
$yellow = Tint 233 202 60
$orange = Tint 235 137 52
$red = Tint 224 74 58

# Front: healthy thorax, worn stomach, damaged pelvis, mixed arms/sides/legs
Draw-PreviewFigure $sg 25 15 0.62 ([ordered]@{
    "head" = $green; "throat" = $yellow; "thorax" = $green; "stomach" = $yellow;
    "pelvis" = $red; "side_left" = $orange; "side_right" = $green;
    "arm_upper_left" = $green; "arm_fore_left" = $orange;
    "arm_upper_right" = $yellow; "thigh_left" = $green; "calf_right" = $red
})
# Back: bands only
Draw-PreviewFigure $sg 130 15 0.62 ([ordered]@{
    "throat" = $green; "thorax" = $orange; "stomach" = $green; "pelvis" = $yellow
})
# 1x display-size check
Draw-PreviewFigure $sg 235 25 0.47 ([ordered]@{
    "head" = $green; "throat" = $red; "thorax" = $green; "stomach" = $yellow;
    "pelvis" = $red; "side_left" = $orange; "arm_fore_left" = $orange; "thigh_right" = $yellow
})

$sg.Dispose()
$sheet.Save((Join-Path $iconDir "puppet_preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
$baseBrush.Dispose(); $whiteBrush.Dispose()
$green.Dispose(); $yellow.Dispose(); $orange.Dispose(); $red.Dispose()

Write-Host "Puppet sprites written to $iconDir"
