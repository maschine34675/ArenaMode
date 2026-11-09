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
$client = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $repo 'ArenaMode.Client/bin/Release/maschine-ArenaMode.Client.dll')))
$helper = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $repo 'ArenaMode.Session/bin/Release/maschine-ArenaMode.Session.exe')))
$sha = [Security.Cryptography.SHA256]::Create()
$size = $null
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
                if ($null -eq $size) { $size = $bitmap.Size }
                if ($bitmap.Size -ne $size -or $bitmap.Width -le $bitmap.Height) { throw "Layers do not share a landscape viewport: $name" }
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
        if ($name -eq 'splash.jpg') {
            $header = $helper.GetManifestResourceStream($resource)
            try {
                if (!$header -or [Convert]::ToHexString($sha.ComputeHash($header)) -cne $diskHash) { throw 'Direct-start image differs from EFT splash.' }
            } finally { if ($header) { $header.Dispose() } }
        }
    }
} finally { $sha.Dispose() }
Write-Output "SESSION_MENU_ART_PASSED $($size.Width)x$($size.Height), 4 client resources and matching helper splash"
