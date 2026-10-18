#Requires -Version 7.0
[CmdletBinding()]
param([string]$SptPath = (Join-Path $PSScriptRoot '../../../..'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$run = Join-Path $repo ('artifacts/bigbrain-library/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
function Run-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($Arguments -join ' ')" }
}
function Snapshot {
    $state = [ordered]@{}
    foreach ($relative in @('BepInEx/plugins/maschine-ArenaMode', 'BepInEx/plugins/ArenaMode',
        'SPT_Runtime/user/mods/ArenaMode', 'BepInEx/plugins/maschine-Redeploy.Core.dll',
        'BepInEx/config/com.maschine.ArenaMode.cfg', 'BepInEx/plugins/maschine-ArenaMode.Client.dll',
        'BepInEx/plugins/maschine-ArenaMode.BigBrainCompat.dll')) {
        $path = Join-Path $SptPath $relative
        if (Test-Path -LiteralPath $path) {
            Get-ChildItem -LiteralPath $path -Recurse -File | Sort-Object FullName | ForEach-Object {
                $state[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        } else { $state[$path] = 'ABSENT' }
    }
    $state | ConvertTo-Json -Compress
}
$before = Snapshot
try {
    $override = Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets'
    Run-Dotnet @('build', "$repo/ArenaMode.BigBrainCompat/ArenaMode.BigBrainCompat.csproj", '-c', 'Release', '--nologo',
        "-p:SPTPath=$SptPath", "-p:CustomAfterMicrosoftCommonTargets=$override")
    . "$repo/tools/Assert-BigBrainLibrary.ps1"
    Assert-BigBrainLibrary "$repo/ArenaMode.Client/bin/Release/maschine-ArenaMode.Client.dll" `
        "$repo/ArenaMode.BigBrainCompat/bin/Release/maschine-ArenaMode.BigBrainCompat.dll" "$SptPath/BepInEx/core/Mono.Cecil.dll"
    Run-Dotnet @('build', "$PSScriptRoot/BigBrainCompatibilityChecks.csproj", '-c', 'Release', '--nologo')
    $runner = "$PSScriptRoot/bin/Release/net10.0/BigBrainCompatibilityChecks.dll"
    foreach ($variant in @('good', 'bad-api', 'wrong-version', 'legacy-file', 'wrong-name')) {
        $output = Join-Path $run $variant
        $props = @("-p:OutputPath=$output/", '-p:BuildProjectReferences=false')
        if ($variant -eq 'bad-api') { $props += '-p:DefineConstants=BAD_API' }
        if ($variant -eq 'legacy-file') { $props += '-p:DefineConstants=LEGACY_PLUGIN' }
        if ($variant -eq 'wrong-version') { $props += '-p:AssemblyVersion=2.0.0.0' }
        if ($variant -eq 'wrong-name') { $props += '-p:AssemblyName=Unrelated.Library' }
        Run-Dotnet (@('build', "$PSScriptRoot/HostFixture/HostFixture.csproj", '-c', 'Release', '--nologo') + $props)
        if ($variant -eq 'wrong-name') {
            Copy-Item -LiteralPath "$output/Unrelated.Library.dll" -Destination "$output/maschine-ArenaMode.BigBrainCompat.dll"
        }
        if ($variant -eq 'legacy-file') {
            $rejected = $false
            try {
                Assert-BigBrainLibrary "$repo/ArenaMode.Client/bin/Release/maschine-ArenaMode.Client.dll" `
                    "$output/maschine-ArenaMode.BigBrainCompat.dll" "$SptPath/BepInEx/core/Mono.Cecil.dll"
            } catch {
                if ($_.Exception.Message -notlike 'Companion still contains a BepInEx plugin:*') { throw }
                $rejected = $true
            }
            if (-not $rejected) { throw 'Metadata guard accepted the legacy plugin fixture.' }
            Write-Output 'BIGBRAIN_LEGACY_PLUGIN_REJECTED'
        }
        foreach ($scenario in $(if ($variant -eq 'good') { @('good','init-false','init-throw','tick-throw','shutdown-throw') } else { @($variant) })) {
            Run-Dotnet @($runner, '--host', $scenario, "$output/maschine-ArenaMode.Client.dll")
        }
    }
    foreach ($harness in @('PickupObjectiveChecks','MolotovChecks','TeamArenaExpansionChecks')) {
        Run-Dotnet @('run', '--project', "$repo/tools/$harness", '-c', 'Release')
    }
} finally {
    if ((Snapshot) -cne $before) { throw 'Installed Arena/Core files changed during the offline checks.' }
}
Write-Output 'BIGBRAIN_LIBRARY_BINARY_CHECKS_PASSED'
