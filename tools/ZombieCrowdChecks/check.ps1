$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$out = Join-Path $repo 'artifacts/zombie-crowd-checks'
New-Item -ItemType Directory -Force $out | Out-Null
$runtime = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Bots/ZombieCrowdRuntime.cs'))
$recall = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Areas/ArenaAreaCrowdRecall.cs'))
$mutants = @(
    @{ Name='raw-candidate'; Source=$recall; Property='RecallSource'; Old='placed = sample;'; New='placed = candidate;'; Failure='teleports corrected sample' },
    @{ Name='residual-offset'; Source=$runtime; Property='CrowdSource'; Old='avoidance.DropOffset();'; New='// mutant: residual offsets survive'; Failure='both offsets cleared' },
    @{ Name='off-still-runs'; Source=$runtime; Property='CrowdSource'; Old='!Enabled() || !ZombieNativeEventBridge.IsActiveZombieRaid()'; New='!ZombieNativeEventBridge.IsActiveZombieRaid()'; Failure='OFF never moves/stops/searches' },
    @{ Name='occupied-slot'; Source=$runtime; Property='CrowdSource'; Old='if (Occupied(world, body, placed, separation)) continue;'; New='// mutant: occupied destination accepted'; Failure='all ring candidates occupied' },
    @{ Name='no-clearance'; Source=$runtime; Property='CrowdSource'; Old='return HasRoom(placed);'; New='return true;'; Failure='physical clearance checked' }
)
foreach ($mutant in $mutants) {
    if (!$mutant.Source.Contains($mutant.Old)) { throw "Mutation anchor missing: $($mutant.Name)" }
    $file = Join-Path $out ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($file, $mutant.Source.Replace($mutant.Old, $mutant.New))
    $log = Join-Path $out ($mutant.Name + '.log')
    & dotnet run --project $PSScriptRoot -c Release "-p:$($mutant.Property)=$file" *> $log
    $result = $LASTEXITCODE
    $text = [IO.File]::ReadAllText($log)
    if ($result -eq 0 -or !$text.Contains($mutant.Failure) -or $text.Contains('error CS')) {
        throw "Mutant did not fail for the intended behavioral assertion: $($mutant.Name); see $log"
    }
    Write-Output "REJECTED $($mutant.Name)"
}
& dotnet run --project $PSScriptRoot -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output 'ZOMBIE_CROWD_MUTANTS_REJECTED (5)'
