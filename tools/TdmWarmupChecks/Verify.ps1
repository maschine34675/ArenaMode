param()
$ErrorActionPreference = 'Stop'
$tdmWarmupRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$tdmWarmupSource = Join-Path $tdmWarmupRepo 'ArenaMode.Client/ArenaController.cs'
$tdmWarmupText = [IO.File]::ReadAllText($tdmWarmupSource)
$tdmWarmupHash = (Get-FileHash -LiteralPath $tdmWarmupSource).Hash
$tdmPreloaderSource = Join-Path $tdmWarmupRepo 'ArenaMode.Client/Kits/KitBundlePreloader.cs'
$tdmPreloaderText = [IO.File]::ReadAllText($tdmPreloaderSource)
$tdmPreloaderHash = (Get-FileHash -LiteralPath $tdmPreloaderSource).Hash
$tdmWarmupArtifact = Join-Path $tdmWarmupRepo ('artifacts/tdm-warmup-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($tdmWarmupArtifact)
$tdmWarmupProject = Join-Path $PSScriptRoot 'TdmWarmupChecks.csproj'
function Invoke-WarmupCheck([string] $Source, [string] $Configuration, [string] $Property = 'ControllerSource') {
    $tdmPrevious = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $tdmOutput = & dotnet run --project $tdmWarmupProject -c $Configuration "-p:${Property}=$Source" 2>&1 | Out-String
        return @{ Code = $LASTEXITCODE; Output = $tdmOutput }
    } finally { $ErrorActionPreference = $tdmPrevious }
}
$tdmBaseline = Invoke-WarmupCheck $tdmWarmupSource 'Release'
if ($tdmBaseline.Code -ne 0 -or $tdmBaseline.Output -notmatch 'TDM_WARMUP_PRODUCTION_PASSED') { throw $tdmBaseline.Output }
$tdmFaults = @(
    @{ Name='IgnoreReadiness'; Find='|| !team.InitialRosterReady'; Replace='|| false'; Failure='slow TDM assembly never starts optional catalog work' },
    @{ Name='IgnoreRespawn'; Find='|| Respawn.ArenaRespawnDriver.IsRespawnInProgress'; Replace='|| false'; Failure='active player pipeline blocks optional TDM warmup' },
    @{ Name='IgnoreOpeningPool'; Find='|| Kits.KitBundlePreloader.IsPreloading'; Replace='|| false'; Failure='initial kit pool loading blocks optional TDM catalog before pipeline flag' },
    @{ Name='DisableDeferral'; Find='string.Equals(Session.ArenaSession.ActiveMode, "teamdeathmatch",'; Replace='false && string.Equals(Session.ArenaSession.ActiveMode, "teamdeathmatch",'; Failure='latched TDM with no match does not warm catalog' },
    @{ Name='NoRelease'; Find='|| !team.InitialRosterReady'; Replace='|| true'; Failure='ready TDM starts the deferred catalog once without another eight-second delay' },
    @{ Name='MissingOwnership'; Property='PreloaderSource'; Find='_activePoolLoads++;'; Replace='// missing increment'; Failure='opening preload owns active native pool work before pipeline flag' },
    @{ Name='NoLoadRelease'; Property='PreloaderSource'; Find='_activePoolLoads--;'; Replace='_activePoolLoads -= 0;'; Failure='successful native load releases its epoch ownership' },
    @{ Name='OldEpochRelease'; Property='PreloaderSource'; Find='if (epoch == _raidEpoch)'; Replace='if (true)'; Failure='old completion cannot release the new raid load' },
    @{ Name='BooleanOwnership'; Property='PreloaderSource'; Find='_activePoolLoads++;'; Replace='_activePoolLoads = 1;'; Failure='one completed native load cannot hide another current load' },
    @{ Name='NoLoadReset'; Property='PreloaderSource'; Find='_activePoolLoads = 0;'; Replace='// missing reset'; Failure='reset clears prior raid load ownership' }
)
# Removing only the missing-match branch cannot preserve C# definite assignment
# of team, so the whole-deferral fault is the decisive integration control.
$tdmCount = 0
foreach ($tdmFault in $tdmFaults) {
    $tdmInput = if ($tdmFault.Property -eq 'PreloaderSource') { $tdmPreloaderText } else { $tdmWarmupText }
    $tdmProperty = if ($tdmFault.Property) { $tdmFault.Property } else { 'ControllerSource' }
    if ($tdmInput.Split(@($tdmFault.Find), [StringSplitOptions]::None).Length -ne 2) { throw ('Ambiguous fault anchor: ' + $tdmFault.Name) }
    $tdmCopy = Join-Path $tdmWarmupArtifact ($tdmFault.Name + '.cs')
    [IO.File]::WriteAllText($tdmCopy, $tdmInput.Replace($tdmFault.Find, $tdmFault.Replace))
    $tdmResult = Invoke-WarmupCheck $tdmCopy ('Fault' + $tdmFault.Name) $tdmProperty
    [IO.File]::WriteAllText((Join-Path $tdmWarmupArtifact ($tdmFault.Name + '.log')), $tdmResult.Output)
    if ($tdmResult.Code -eq 0 -or -not $tdmResult.Output.Contains($tdmFault.Failure)) { throw ('Fault missed: ' + $tdmFault.Name + [Environment]::NewLine + $tdmResult.Output) }
    $tdmCount++
    Write-Output ('Rejected source fault: ' + $tdmFault.Name)
}
$tdmFinal = Invoke-WarmupCheck $tdmWarmupSource 'Release'
if ($tdmFinal.Code -ne 0 -or $tdmFinal.Output -notmatch 'TDM_WARMUP_PRODUCTION_PASSED') { throw $tdmFinal.Output }
if ((Get-FileHash -LiteralPath $tdmWarmupSource).Hash -ne $tdmWarmupHash) { throw 'Production controller changed during checks' }
if ((Get-FileHash -LiteralPath $tdmPreloaderSource).Hash -ne $tdmPreloaderHash) { throw 'Production preloader changed during checks' }
Write-Output $tdmFinal.Output.Trim()
Write-Output "TDM_WARMUP_CHECKS_PASSED: $tdmCount source faults; $tdmWarmupArtifact"
