param([switch]$SkipNegativeProbes)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$scratch=Join-Path $root ('artifacts/tactical-polish-checks/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$boundPaths=@('ArenaMode.Client/Modes/TacticalMode.cs','ArenaMode.Client/Modes/ArenaMatch.cs',
    'ArenaMode.Client/Modes/TacticalMode.AssemblyStatus.cs','ArenaMode.Client/Hud/TacticalBuyPanel.cs',
    'ArenaMode.Client/Modes/SoloTacticalSpawnPolicy.cs','ArenaMode.Client/Modes/SoloTacticalRoundStartPolicy.cs',
    'ArenaMode.Client/Combat/ArenaCombatAwards.cs','ArenaMode.Client/Combat/ArenaKill.cs','ArenaMode.Client/Combat/ScoreRules.cs')
$boundPaths+=Get-ChildItem -LiteralPath $PSScriptRoot -File|ForEach-Object {'tools/TacticalPolishChecks/'+$_.Name}
$before=@{};foreach($relative in $boundPaths){$before[$relative]=(Get-FileHash -LiteralPath (Join-Path $root $relative) -Algorithm SHA256).Hash}
$tactical=[IO.File]::ReadAllText((Join-Path $root 'ArenaMode.Client/Modes/TacticalMode.cs'))
$panel=[IO.File]::ReadAllText((Join-Path $root 'ArenaMode.Client/Hud/TacticalBuyPanel.cs'))
$matchSource=[IO.File]::ReadAllText((Join-Path $root 'ArenaMode.Client/Modes/ArenaMatch.cs'))
function Extract-Method([string]$source,[string]$name){
    $pattern='(?ms)^    (?:public|private|internal) (?:static )?\w+[?]? '+[regex]::Escape($name)+'\([^;]*?^    \{.*?^    \}'
    $matches=[regex]::Matches($source,$pattern)
    if($matches.Count -ne 1){throw "Expected exactly one production method $name, found $($matches.Count)"}
    return $matches[0].Value
}
$methodNames=@('Tick','TickLive','StartRound','RequestSoloSquad','SelectOpeningKit','IssueSoloKit','TryBuy','Affordable','EnterBuy','ReadyUp','RetireBot','RetireSquad')
$methods=@{};foreach($name in $methodNames){$methods[$name]=Extract-Method $tactical $name}
$properties=@('AllowsCountdownMenu','InBuyPhase','KeepsCurrentGear','NoPurchaseDescription')|ForEach-Object {
    $match=[regex]::Match($tactical,'(?ms)^    (?:public|internal) \w+ '+$_+'\s*=>.*?;[ \t]*\r?$')
    if(!$match.Success){throw "Production property $_ not found"};$match.Value
}
$matchMethods=@('ReportAnnouncerCue','BeginAwardRound','RaiseSafe')|ForEach-Object {Extract-Method $matchSource $_}
$matchSlice="`npublic partial class ArenaMatch {`n"+($matchMethods -join "`n")+"`n}"
function Write-Slice([hashtable]$parts,[string]$file){
    $header="using System; using System.Collections.Generic; using System.Linq; using ArenaMode.Kits; using ArenaMode.Bots; using ArenaMode.Combat; using ArenaMode.Config; using EFT; using UnityEngine;`nnamespace ArenaMode.Modes; public partial class TacticalMode {`n"
    [IO.File]::WriteAllText($file,$header+($properties -join "`n")+"`n"+(($methodNames|ForEach-Object {$parts[$_]}) -join "`n")+"`n}"+$matchSlice)
}
function Run-Probe([string]$name,[hashtable]$parts,[string]$panelText,[string]$part,[string]$failure){
    $dir=Join-Path $scratch $name;New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $slice=Join-Path $dir 'TacticalSlice.cs';$panelFile=Join-Path $dir 'TacticalBuyPanel.cs'
    Write-Slice $parts $slice;[IO.File]::WriteAllText($panelFile,$panelText)
    $output=& dotnet build (Join-Path $PSScriptRoot 'TacticalPolishChecks.csproj') -c Release "-p:TacticalSlice=$slice" "-p:PanelSource=$panelFile" "-p:BaseIntermediateOutputPath=$dir/obj/" "-p:OutputPath=$dir/out/" --nologo 2>&1
    if($LASTEXITCODE -ne 0){$output|Out-String|Write-Host;throw "$name did not compile"}
    $run=& dotnet (Join-Path $dir 'out/TacticalPolishChecks.dll') $part 2>&1
    $code=$LASTEXITCODE;$text=$run|Out-String
    [IO.File]::WriteAllText((Join-Path $dir 'result.txt'),$text)
    if($failure){if($code -eq 0 -or !$text.Contains($failure)){throw "$name did not reject its intended behavior: $text"};Write-Host "Rejected $name at: $failure"}
    elseif($code -ne 0){throw "$name failed: $text"}else{Write-Host $text.Trim()}
}
# Admission/teardown boundaries outside the extracted adapter remain source checks;
# full compilation against installed EFT is the separate client-build gate.
if(!$tactical.Contains('KitCatalog.KitProvider = IssueSoloKit;') -or !$tactical.Contains('_starter = SelectOpeningKit()')){throw 'Opening supplier is not wired to the production mode'}
$onEnd=Extract-Method $tactical 'OnMatchEnd'
foreach($guard in @('_squadEpoch++;','_phase = Phase.Ended;','KitCatalog.KitProvider = null;')){if(!$onEnd.Contains($guard)){throw "Teardown lost $guard"}}
$team=Extract-Method $tactical 'TickTeamStaging'
$freeze=$team.IndexOf('TryFreezeTeamEnemies(');$hostility=$team.IndexOf('EnsureHostility(');$cue=$team.IndexOf('ArenaAnnouncerCue.RoundStart')
if($freeze -lt 0 -or $hostility -lt 0 -or $cue -lt $freeze -or $cue -lt $hostility){throw 'PFT cue precedes readiness or its guard is missing'}
Run-Probe 'production' $methods $panel 'all' ''
if(!$SkipNegativeProbes){
    $historical=(& git -C $root show '8e7f9ea:ArenaMode.Client/Modes/TacticalMode.cs') -join "`n"
    if($LASTEXITCODE -ne 0){throw 'Historical Tactical baseline unavailable'}
    $cue=$methods.Clone();$cue['StartRound']=Extract-Method $historical 'StartRound'
    Run-Probe 'historical-dispatch-cue' $cue $panel 'cue' 'dispatch must not announce round start'
    $provider=[regex]::Match($historical,'(?ms)KitCatalog.KitProvider = \(\) =>\s*(\{.*?^        \});')
    if(!$provider.Success){throw 'Historical opening provider not found'}
    $kit=$methods.Clone();$kit['IssueSoloKit']='    private KitDefinition IssueSoloKit() '+$provider.Groups[1].Value
    Run-Probe 'historical-random-free-kit' $kit $panel 'kit' 'opening kit must be the cheapest buildable starter'
    $oldPanel=(& git -C $root show '8e7f9ea:ArenaMode.Client/Hud/TacticalBuyPanel.cs') -join "`n"
    if($LASTEXITCODE -ne 0){throw 'Historical buy panel unavailable'}
    # The old panel has no Escape bridge/API. These no-op shims make that exact
    # absence observable by the new caller; old Update/OnGUI remain untouched.
    $shim="`n    internal static bool IsNativeMenuOpen => false;`n    internal static bool TryOpenCountdownMenu(EFT.EftGamePlayerOwner owner,EFT.InputSystem.ECommand command) => false;`n    internal static void ApplyInputPatch() {}"
    $at=$oldPanel.LastIndexOf('}');$oldPanel=$oldPanel.Insert($at,$shim)
    Run-Probe 'historical-countdown-escape' $methods $oldPanel 'escape' 'dead countdown owner must reach native Escape menu'
    $originalMatchSlice=$matchSlice
    $matchSlice=$matchSlice.Replace(' && beginAwardRound','')
    Run-Probe 'fault-cue-resets-awards' $methods $panel 'cue' 'delayed cue preserves early First Blood and multi-kill chain'
    $matchSlice=$originalMatchSlice
}
[ordered]@{files=$before;check='production method bodies and full panel/policy/award sources';negativeProbesSkipped=$SkipNegativeProbes.IsPresent}|
    ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $scratch 'source-binding.json') -Encoding utf8
foreach($relative in $boundPaths){
    if((Get-FileHash -LiteralPath (Join-Path $root $relative) -Algorithm SHA256).Hash -ne $before[$relative]){
        throw "Source changed during checks; rerun required: $relative"
    }
}
Write-Host "TACTICAL POLISH CHECKS PASSED; evidence: $scratch"
