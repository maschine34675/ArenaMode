param([string]$SptRoot='D:\SPT41',[switch]$ProductionOnly)
$ErrorActionPreference='Stop'
$arenaRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaProject=Join-Path $PSScriptRoot 'AreaTeamRuntimeChecks.csproj'
$arenaOutput=Join-Path $PSScriptRoot 'artifacts'
$arenaWork=Join-Path $arenaOutput ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaWork -Force | Out-Null
$arenaRuntime=[IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client\Areas\ArenaAreaTeamRuntime.cs'))
$arenaInitial=[IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client\Areas\ArenaAreaInitialSpawnPatch.cs'))
$arenaPrepared=[IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client\Areas\ArenaAreaRespawn.cs'))
$arenaMain=[IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client\Areas\ArenaAreaRuntime.cs'))
$arenaStart=$arenaMain.IndexOf('    internal static (Vector3 position, Quaternion rotation) PickRespawn(')
$arenaEnd=$arenaMain.IndexOf('    internal static void RequirePosition(', $arenaStart)
if($arenaStart -lt 0 -or $arenaEnd -le $arenaStart){throw 'Exact production respawn selection boundaries changed.'}
# Compile the actual whole method verbatim, never a test-side reimplementation.
# The remaining main-runtime lifecycle/navigation methods are explicit host seams.
$arenaPick="using System;`nusing System.Linq;`nusing ArenaMode.Shared;`nusing EFT;`nusing UnityEngine;`nnamespace ArenaMode.Areas;`ninternal static partial class ArenaAreaRuntime {`n"+$arenaMain.Substring($arenaStart,$arenaEnd-$arenaStart)+"`n}"
[IO.File]::WriteAllText((Join-Path $arenaOutput 'PickRespawn.cs'),$arenaPick)

function Invoke-Check([string[]]$Extra=@()){
    $arenaPreference=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        $arenaBuild=@(& dotnet build $arenaProject -t:Rebuild -c Release --nologo @Extra 2>&1)
        $arenaBuildExit=$LASTEXITCODE
        if($arenaBuildExit -ne 0){throw ($arenaBuild -join [Environment]::NewLine)}
        $arenaLines=@(& dotnet run --project $arenaProject -c Release --no-build -- $SptRoot 2>&1)
        $arenaExit=$LASTEXITCODE
    } finally {$ErrorActionPreference=$arenaPreference}
    return @{Exit=$arenaExit;Text=($arenaLines|ForEach-Object{$_.ToString()}) -join [Environment]::NewLine}
}
function Assert-Production {
    $arenaResult=Invoke-Check
    Write-Output $arenaResult.Text
    if($arenaResult.Exit -ne 0 -or $arenaResult.Text -notmatch 'ARENA_TEAM_RUNTIME_CONTRACTS_PASSED'){throw 'Production team runtime checks failed.'}
}
function Test-Mutant([string]$Name,[string]$Before,[string]$After,[string]$Assertion,[string]$Property='RuntimeSource'){
    $arenaSource=if($Property -eq 'RuntimeSource'){$arenaRuntime}elseif($Property -eq 'PreparedSource'){$arenaPrepared}else{$arenaPick}
    if(-not $arenaSource.Contains($Before)){throw "Missing exact mutation anchor: $Name"}
    $arenaPath=Join-Path $arenaWork ($Name+'.cs')
    [IO.File]::WriteAllText($arenaPath,$arenaSource.Replace($Before,$After))
    $arenaResult=Invoke-Check @("-p:${Property}=$arenaPath")
    [IO.File]::WriteAllText((Join-Path $arenaWork ($Name+'.log')),$arenaResult.Text)
    if($arenaResult.Exit -eq 0 -or -not $arenaResult.Text.Contains('ASSERT: '+$Assertion)){
        Write-Output $arenaResult.Text;throw "Fault must fail at named behavioral assertion: $Name"
    }
    Write-Output "Team runtime negative control rejected: $Name -> $Assertion"
}
try {
    Assert-Production
    if(-not $ProductionOnly){
        Test-Mutant 'unused-opponent-pmc-required' 'players.Length < requiredPlayers' 'players.Length < 1' 'exact roster needs no opponent PMC or surplus ally bot:'
        Test-Mutant 'surplus-allied-bot-required' 'int requiredBots = _teamSize - requiredPlayers;' 'int requiredBots = _teamSize;' 'exact roster needs no opponent PMC or surplus ally bot:'
        Test-Mutant 'required-ally-undercounted' 'int requiredBots = _teamSize - requiredPlayers;' 'int requiredBots = _teamSize - requiredPlayers - (localTeam ? 1 : 0);' 'one missing required ally rejects readiness'
        Test-Mutant 'required-opponent-undercounted' 'int requiredBots = _teamSize - requiredPlayers;' 'int requiredBots = _teamSize - requiredPlayers - (localTeam ? 0 : 1);' 'missing native dispatch marker rejects readiness'
        Test-Mutant 'initial-foreign-collection' '|| !ReferenceEquals(system._spawnPoints, _loadingCollection)' '' 'initial native pass-through for collection'
        Test-Mutant 'initial-wrong-faction' '_area.TeamStarts.Allows(side == EPlayerSide.Usec, true, p.Id)' '_area.TeamStarts.Allows(true, true, p.Id)' 'initial PMC uses faction start pool'
        Test-Mutant 'initial-grouping-removed' '(!_teamStarting || _area.TeamStarts.Allows(usec, false, point.Id))' 'true' 'initial bots use their assigned side only'
        Test-Mutant 'expired-leases-accepted' '&& Time.time < Expires' '' 'reservation expires at exact deadline'
        Test-Mutant 'stale-token-accepted' '&& Token == _token' '' 'new exact raid token rejects old lease'
        Test-Mutant 'far-arrival-accepted' '&& (player.Position - Position).sqrMagnitude <= 4f' '' 'displaced arrival rejects reserved marker'
        Test-Mutant 'wrong-side-arrival-accepted' '&& (!_requireFaction || player.Profile?.Info?.Side == (Usec ? EPlayerSide.Usec : EPlayerSide.Bear))' '' 'wrong-faction arrival rejects lease'
        Test-Mutant 'bot-enemy-ranking-reversed' '.OrderByDescending(p => NearestEnemyDistance(p.Position, usec))' '.OrderBy(p => NearestEnemyDistance(p.Position, usec))' 'bot respawn maximizes nearest live enemy distance'
        Test-Mutant 'reservation-overlap-ignored' 'return !TeamReservations.Values.Any(' 'return true || !TeamReservations.Values.Any(' 'pending reservations excluded from preflight selection'
        Test-Mutant 'player-faction-reversed' '_game.Profile.Info.Side == EPlayerSide.Usec, avoid' '_game.Profile.Info.Side != EPlayerSide.Usec, avoid' 'player respawn maximizes live enemy distance before death distance' 'PickSource'
        Test-Mutant 'readiness-ignores-native-zone-pool' '&& dispatchable.Any(d => d != null && d.Id == p.Id && (d.Position-p.Position).sqrMagnitude < 0.01f)' '' 'missing native dispatch marker rejects readiness'
        Test-Mutant 'prepared-player-occupancy-ignored' '_reservation != null && !_reservation.CanReleasePlayer' 'false' 'late occupant blocks final prepared player release' 'PreparedSource'
        Test-Mutant 'mutable-marker-position-accepted' '&& (Point.Position - Position).sqrMagnitude < 0.0001f' '' 'native marker position mutation expires reserved arrival'
        Test-Mutant 'mutable-marker-identity-accepted' '&& Point.Id == PointId' '' 'native marker identity mutation expires reservation'
        Test-Mutant 'mutable-marker-disposal-key' 'TeamReservations.Remove(PointId);' 'TeamReservations.Remove(Point.Id);' 'reservation disposal uses frozen identity after native marker rename'
    }
} finally {Assert-Production}
Write-Output "Team runtime check evidence: $arenaWork"
Write-Output 'ARENA_TEAM_RUNTIME_CHECKS_PASSED'
