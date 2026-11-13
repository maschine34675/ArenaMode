#Requires -Version 7.0
# Decode the shipped resources, including transparency inside a 16:9 crop of the wide image.
[CmdletBinding()] param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sentinel = Join-Path $repo 'artifacts/session-menu-art/must-not-exist'
if (Test-Path -LiteralPath $sentinel) { throw "No-deploy sentinel exists." }
$noDeploy = Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets'
Add-Type -AssemblyName System.Drawing
$projects = @('ArenaMode.Client','ArenaMode.Session')
foreach ($project in $projects) {
    & dotnet build (Join-Path $repo "$project/$project.csproj") -c Release --nologo -v:quiet '-p:SPTModType=NoDeploy' "-p:SPTUserMods=$sentinel" "-p:CustomAfterMicrosoftCommonTargets=$noDeploy"
    if ($LASTEXITCODE -ne 0) { throw "$project build failed." }
}
$folder = Join-Path $repo 'ArenaMode.Client/Assets/SessionMenu'
$approved = Join-Path $repo 'tools/ArenaUiArtwork/SessionMenu/masters/streiflicht-approved-21x9.jpg'
$approvedHash = (Get-FileHash -LiteralPath $approved).Hash
if ((Get-FileHash -LiteralPath (Join-Path $folder 'splash.jpg')).Hash -cne $approvedHash) {
    throw "The flat artwork differs from the selected Streiflicht preview."
}
$client = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $repo 'ArenaMode.Client/bin/Release/maschine-ArenaMode.Client.dll')))
$helper = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $repo 'ArenaMode.Session/bin/Release/maschine-ArenaMode.Session.exe')))
$sha = [Security.Cryptography.SHA256]::Create()
$size = $null
$composition = $null
$graphics = $null
try {
    foreach ($name in 'hall.jpg','barrier.png','fence.png','splash.jpg') {
        $resource = 'ArenaMode.SessionMenu.' + $name
        $diskHash = (Get-FileHash -LiteralPath (Join-Path $folder $name)).Hash
        $stream = $client.GetManifestResourceStream($resource)
        try {
            if (!$stream -or [Convert]::ToHexString($sha.ComputeHash($stream)) -cne $diskHash) { throw "Client payload differs: $name" }
            $stream.Position = 0
            $bitmap = [Drawing.Bitmap]::new($stream)
            try {
                if ($null -eq $size) {
                    $size = $bitmap.Size
                    $composition = [Drawing.Bitmap]::new($size.Width,$size.Height)
                    $graphics = [Drawing.Graphics]::FromImage($composition)
                }
                if ($bitmap.Size -ne $size -or $bitmap.Width -le $bitmap.Height) { throw "Layers do not share a landscape viewport: $name" }
                if ($name -ne 'splash.jpg') { $graphics.DrawImageUnscaled($bitmap,0,0) }
                else {
                    # The still and the three depth layers should depict the same shot at rest.
                    # Allow small channel differences from JPEG encoding of the reconstructed backplate.
                    $errorSum = 0L
                    $samples = 0
                    $outliers = 0
                    for ($y=3; $y -lt $size.Height; $y+=11) {
                        for ($x=5; $x -lt $size.Width; $x+=13) {
                            $got = $composition.GetPixel($x,$y)
                            $want = $bitmap.GetPixel($x,$y)
                            foreach ($difference in ([Math]::Abs($got.R-$want.R), [Math]::Abs($got.G-$want.G), [Math]::Abs($got.B-$want.B))) {
                                $errorSum += $difference
                                if ($difference -gt 4) { $outliers++ }
                            }
                            $samples += 3
                        }
                    }
                    $mean = $errorSum / $samples
                    if ($mean -gt .7 -or $outliers -gt .01*$samples) {
                        throw "The menu at rest differs from the selected splash (mean RGB error $mean / 255, $outliers of $samples samples off by more than four)."
                    }
                }
                if ($name.EndsWith('.png')) {
                    $left = [int](($bitmap.Width - $bitmap.Height * 16 / 9) / 2)
                    $visible = 0; $clear = 0
                    for ($y = 0; $y -lt $bitmap.Height; $y += 32) {
                        for ($x = $left; $x -lt $bitmap.Width - $left; $x += 32) {
                            $alpha = $bitmap.GetPixel($x,$y).A
                            if ($alpha -ge 192) { $visible++ }
                            if ($alpha -le 16) { $clear++ }
                        }
                    }
                    if (!$visible -or !$clear) { throw "Moving layer disappears or covers the hall inside 16:9: $name" }
                }
            } finally { $bitmap.Dispose() }
        } finally { if ($stream) { $stream.Dispose() } }
    }
    $header = $helper.GetManifestResourceStream('ArenaMode.Launcher.thumbnail.png')
    try {
        $thumbnailHash = (Get-FileHash -LiteralPath (Join-Path $repo 'assets/thumbnail.png')).Hash
        if (!$header -or [Convert]::ToHexString($sha.ComputeHash($header)) -cne $thumbnailHash) { throw 'Direct-start image differs from the ArenaMode thumbnail.' }
    } finally { if ($header) { $header.Dispose() } }
} finally {
    if ($graphics) { $graphics.Dispose() }
    if ($composition) { $composition.Dispose() }
    $sha.Dispose()
}
Write-Output "SESSION_MENU_ART_PASSED $($size.Width)x$($size.Height), 4 client resources and matching launcher thumbnail"
