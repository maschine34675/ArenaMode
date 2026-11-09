#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../../ArenaMode.Client/Assets/SessionMenu'),
    [string]$PreviewDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$output = [IO.Directory]::CreateDirectory($OutputDirectory).FullName
$width = 2352
$height = 1008
$jpegCodec = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
    Where-Object MimeType -eq 'image/jpeg'

function Write-Jpeg([Drawing.Bitmap]$Bitmap, [string]$Path) {
    $parameters = [Drawing.Imaging.EncoderParameters]::new(1)
    try {
        $parameters.Param[0] = [Drawing.Imaging.EncoderParameter]::new([Drawing.Imaging.Encoder]::Quality, [long]89)
        $Bitmap.Save($Path, $jpegCodec, $parameters)
    } finally { $parameters.Dispose() }
}

function Draw-Plate([Drawing.Bitmap]$Canvas, [Drawing.Image]$Source, [Drawing.Rectangle]$Destination) {
    $g = [Drawing.Graphics]::FromImage($Canvas)
    $attr = [Drawing.Imaging.ImageAttributes]::new()
    try {
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $attr.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
        $g.DrawImage($Source, $Destination, 0, 0, $Source.Width, $Source.Height,
            [Drawing.GraphicsUnit]::Pixel, $attr)
    } finally { $attr.Dispose(); $g.Dispose() }
}

function Save-Layer([string]$Name, [Drawing.Rectangle]$Destination) {
    $master = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot "masters/$Name.png"))
    $canvas = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        Draw-Plate $canvas $master $Destination
        if ($Name -eq 'hall') { Write-Jpeg $canvas (Join-Path $output 'hall.jpg') }
        else { $canvas.Save((Join-Path $output "$Name.png"), [Drawing.Imaging.ImageFormat]::Png) }
    } finally { $canvas.Dispose(); $master.Dispose() }
}

$full = [Drawing.Rectangle]::new(0, 0, $width, $height)
Save-Layer 'hall' $full
# The distant concrete barrier sits on the right-hand floor plane; its near end continues out of frame.
Save-Layer 'barrier' ([Drawing.Rectangle]::new(1454, 693, 1104, 368))
# The close gate extends beyond both vertical edges and into a 16:9 center crop.
Save-Layer 'fence' ([Drawing.Rectangle]::new(-8, -139, 1072, 1608))

$splash = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
try {
    foreach ($file in 'hall.jpg', 'barrier.png', 'fence.png') {
        $layer = [Drawing.Image]::FromFile((Join-Path $output $file))
        try { Draw-Plate $splash $layer $full } finally { $layer.Dispose() }
    }
    Write-Jpeg $splash (Join-Path $output 'splash.jpg')
    if ($PreviewDirectory) {
        $preview = [IO.Directory]::CreateDirectory($PreviewDirectory).FullName
        foreach ($aspect in @(@(1120, 480, '21x9'), @(960, 540, '16x9'))) {
            $w, $h, $label = $aspect
            $image = [Drawing.Bitmap]::new($w, $h, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
            try {
                $scale = [Math]::Max($w / $width, $h / $height)
                $dw = [int]($width * $scale)
                $dh = [int]($height * $scale)
                Draw-Plate $image $splash ([Drawing.Rectangle]::new([int](($w - $dw) / 2), [int](($h - $dh) / 2), $dw, $dh))
                Write-Jpeg $image (Join-Path $preview "factory-$label.jpg")
            } finally { $image.Dispose() }
        }
    }
} finally { $splash.Dispose() }
