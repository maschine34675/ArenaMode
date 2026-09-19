$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'AnnouncementIntegrationChecks.csproj'
$source = Join-Path $root 'ArenaMode.Client'
$bound = @('Audio/ArenaAnnouncementSnapshot.cs','Audio/ArenaAnnouncementPolicy.cs','Audio/ArenaAnnouncementDirector.cs','Modes/ArenaAnnouncerCue.cs','Modes/DeathmatchRules.cs','Modes/TeamDeathmatchMatchPolicy.cs','Modes/RoundsMode.Announcements.cs','Modes/TeamDeathmatchMode.Announcements.cs','Modes/TacticalMode.Announcements.cs','Modes/GunGameMode.Announcements.cs')
$hashes = @{}
foreach ($file in $bound) { $hashes[$file] = (Get-FileHash -LiteralPath (Join-Path $source $file)).Hash }
$scratch = Join-Path $root ('artifacts/announcement-integration/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
function Run-Probe([string]$name, [string]$sourceRoot, [string]$expectedFailure) {
    $output = & dotnet run --project $project -c Release "-p:AnnouncementSourceRoot=$sourceRoot" "-p:OutputPath=$scratch/$name/out/" "-p:IntermediateOutputPath=$scratch/$name/obj/" 2>&1
    $code = $LASTEXITCODE
    $output | Set-Content -LiteralPath (Join-Path $scratch ($name + '.log'))
    $text = $output -join "`n"
    if ($expectedFailure) {
        if ($code -eq 0 -or !$text.Contains($expectedFailure)) { throw "Wrong fault failure: $name; $text" }
        Write-Output "Rejected $name at $expectedFailure"
    } elseif ($code -ne 0 -or !$text.Contains('ANNOUNCEMENT INTEGRATION PASSED')) { throw $text }
    else { $output | Select-Object -Last 1 | Write-Output }
}
Run-Probe 'production' $source ''
$faults = @(
    @('points-for-kills','Modes/TeamDeathmatchMode.Announcements.cs','.Kills;','.Score;','TDM uses raw team kills rather than points'),
    @('early-tactical-live','Modes/TacticalMode.Announcements.cs','(_teamArena || _soloCombatReady)','true','Solo Tactical assembly never spends or announces combat time'),
    @('unearned-final-weapon','Modes/GunGameMode.Announcements.cs','!equipping && player?.HealthController?.IsAlive == true','player?.HealthController?.IsAlive == true','Earned but not equipped final weapon stays silent'),
    @('foreign-world','Audio/ArenaAnnouncementDirector.cs','|| !ArenaSession.IsExactActiveRaid(game, world)','|| false','Foreign world cancels and cannot publish progress'),
    @('old-dm-epoch','Modes/RoundsMode.Announcements.cs','Epoch = epoch','Epoch = _rules.RoundNumber','DM waiting-to-first-live keeps preparation instead of canceling it'),
    @('intermission-not-preparing','Modes/RoundsMode.Announcements.cs','bool preparing = WaitingForStart || InIntermission;','bool preparing = WaitingForStart;','DM intermission prepares the next epoch while retaining the actual prior draw'),
    @('draw-consumed-in-preparation','Audio/ArenaAnnouncementPolicy.cs','            _drawSpoken = false;','','DM terminal draw remains audible after a preceding intermission draw')
)
foreach ($fault in $faults) {
    $faultRoot = Join-Path $scratch ($fault[0] + '/source')
    foreach ($file in $bound) {
        $dest = Join-Path $faultRoot $file
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($dest)) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $source $file) -Destination $dest
    }
    $path = Join-Path $faultRoot $fault[1]
    $text = [IO.File]::ReadAllText($path)
    if (!$text.Contains($fault[2])) { throw 'Mutation target missing' }
    [IO.File]::WriteAllText($path, $text.Replace($fault[2], $fault[3]))
    Run-Probe $fault[0] $faultRoot $fault[4]
}
foreach ($file in $bound) { if ((Get-FileHash -LiteralPath (Join-Path $source $file)).Hash -ne $hashes[$file]) { throw 'Source changed during verification' } }
Run-Probe 'clean-rerun' $source ''
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $scratch 'source-hashes.json')
Write-Output "ANNOUNCEMENT INTEGRATION PASSED; $($faults.Count) compiled faults rejected; evidence: $scratch"
