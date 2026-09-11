# Generates the three hand-grenade glyphs for the weapon panel: frag, flash and
# smoke. Deliberately a separate script: rerunning the four imported generators
# would re-encode all 26 source-identical icons and break their recorded hashes
# (tools/RaidInfoPanelChecks/SOURCE-IMPORT.json). This one writes only its own
# three files. Their hashes live in tools/RaidInfoPanelChecks/ARENA-ICONS.json;
# rerun, then update that record, then rebuild.
#
# The three differ on shape, color and symbol at once, so they stay apart at
# ~22px: a round olive body, a bright canister with a burst, a gray canister
# with a cloud.

Add-Type -AssemblyName System.Drawing

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

function Fill-Brush($g, [System.Drawing.Color]$c, [scriptblock]$shape) {
    $b = New-Object System.Drawing.SolidBrush $c
    & $shape $g $b
    $b.Dispose()
}

function New-Icon([string]$name, [scriptblock]$render) {
    $bmp = New-Object System.Drawing.Bitmap $S, $S
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    & $render $g
    $g.Dispose()
    $path = Join-Path $iconDir $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $path
}

$metal = [System.Drawing.Color]::FromArgb(255, 150, 156, 164)
$dark = [System.Drawing.Color]::FromArgb(255, 44, 48, 44)

# Frag: round olive body with a fuze head, spoon and pin ring.
$fragPath = New-Icon "grenade_frag.png" {
    param($g)
    Fill-Brush $g ([System.Drawing.Color]::FromArgb(255, 108, 124, 70)) {
        param($gg, $b) $gg.FillEllipse($b, 12, 20, 38, 40)
    }
    # Segment grooves read as "pineapple" even when small.
    $groove = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(150, 52, 62, 30)), 2.4
    $g.DrawLine($groove, 31, 22, 31, 58)
    $g.DrawLine($groove, 14, 40, 48, 40)
    $groove.Dispose()
    Fill-Brush $g $metal { param($gg, $b) $gg.FillRectangle($b, 24, 11, 14, 11) }
    # Spoon down the right flank
    Fill-Brush $g $metal {
        param($gg, $b)
        $p = New-RoundedRectPath 37 12 7 30 3
        $gg.FillPath($b, $p); $p.Dispose()
    }
    $ring = New-Object System.Drawing.Pen $metal, 3
    $g.DrawEllipse($ring, 10, 4, 14, 14)
    $ring.Dispose()
}

# Flash: bright canister with a yellow burst on its face.
$flashPath = New-Icon "grenade_flash.png" {
    param($g)
    Fill-Brush $g ([System.Drawing.Color]::FromArgb(255, 226, 228, 222)) {
        param($gg, $b)
        $p = New-RoundedRectPath 18 16 28 44 7
        $gg.FillPath($b, $p); $p.Dispose()
    }
    Fill-Brush $g $metal { param($gg, $b) $gg.FillRectangle($b, 25, 8, 14, 10) }
    $burst = @()
    for ($i = 0; $i -lt 16; $i++) {
        $angle = [Math]::PI * 2 * $i / 16
        $radius = if ($i % 2 -eq 0) { 15 } else { 6 }
        $burst += New-Object System.Drawing.PointF (32 + $radius * [Math]::Cos($angle)), (38 + $radius * [Math]::Sin($angle))
    }
    Fill-Brush $g ([System.Drawing.Color]::FromArgb(255, 245, 196, 44)) {
        param($gg, $b) $gg.FillPolygon($b, $burst)
    }
}

# Smoke: gray canister with a cloud rising from the fuze.
$smokePath = New-Icon "grenade_smoke.png" {
    param($g)
    Fill-Brush $g ([System.Drawing.Color]::FromArgb(255, 96, 102, 110)) {
        param($gg, $b)
        $p = New-RoundedRectPath 20 30 24 32 6
        $gg.FillPath($b, $p); $p.Dispose()
    }
    Fill-Brush $g ([System.Drawing.Color]::FromArgb(255, 60, 64, 70)) {
        param($gg, $b) $gg.FillRectangle($b, 20, 42, 24, 5)
    }
    Fill-Brush $g $metal { param($gg, $b) $gg.FillRectangle($b, 26, 24, 12, 8) }
    Fill-Brush $g ([System.Drawing.Color]::FromArgb(245, 206, 212, 218)) {
        param($gg, $b)
        $gg.FillEllipse($b, 14, 8, 18, 16)
        $gg.FillEllipse($b, 26, 2, 20, 18)
        $gg.FillEllipse($b, 38, 10, 16, 14)
    }
}

# --- Preview: 2x and true HUD size, on the panel's dark glass tone ---
$sheet = New-Object System.Drawing.Bitmap 420, 170
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$sg.Clear([System.Drawing.Color]::FromArgb(255, 20, 23, 27))
$font = New-Object System.Drawing.Font "Bahnschrift", 13
$fg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 232, 236, 240))
$x = 26
foreach ($p in @($fragPath, $flashPath, $smokePath)) {
    $img = [System.Drawing.Image]::FromFile($p)
    $sg.DrawImage($img, (New-Object System.Drawing.RectangleF $x, 14, 72, 72))
    $sg.DrawImage($img, (New-Object System.Drawing.RectangleF $x, 110, 22, 22))
    $sg.DrawString("2", $font, $fg, ($x + 26), 110)
    $img.Dispose()
    $x += 130
}
$sg.Dispose()
$sheet.Save((Join-Path $previewDir "grenade_preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()

Write-Host "Grenade icons written to $iconDir; preview in $previewDir"
