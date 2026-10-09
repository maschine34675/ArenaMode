param()
$ErrorActionPreference = 'Stop'
$warmupRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$warmupProject = Join-Path $PSScriptRoot 'ZombieKitWarmupChecks.csproj'
$warmupArtifact = Join-Path $warmupRepo ('artifacts/zombie-kit-warmup-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($warmupArtifact)
$warmupSources = @{
    ControllerSource = Join-Path $warmupRepo 'ArenaMode.Client/ArenaController.cs'
    LoadingSource = Join-Path $warmupRepo 'ArenaMode.Client/Kits/KitBundlePreloader.LoadingScreen.cs'
    PolicySource = Join-Path $warmupRepo 'ArenaMode.Client/Kits/ZombieKitWarmupPolicy.cs'
    PreloaderSource = Join-Path $warmupRepo 'ArenaMode.Client/Kits/KitBundlePreloader.cs'
}
$warmupTexts = @{}; $warmupHashes = @{}
foreach ($key in $warmupSources.Keys) {
    $warmupTexts[$key] = [IO.File]::ReadAllText($warmupSources[$key])
    $warmupHashes[$key] = (Get-FileHash -LiteralPath $warmupSources[$key]).Hash
}
function Invoke-Warmup([string] $Configuration, [string] $Property = '', [string] $Source = '') {
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arguments = @('run', '--project', $warmupProject, '-c', $Configuration)
        if ($Property) { $arguments += "-p:${Property}=$Source" }
        $resultText = & dotnet @arguments 2>&1 | Out-String
        return @{ Code = $LASTEXITCODE; Output = $resultText }
    } finally { $ErrorActionPreference = $previousPreference }
}
$baseline = Invoke-Warmup 'Release'
[IO.File]::WriteAllText((Join-Path $warmupArtifact 'baseline.log'), $baseline.Output)
if ($baseline.Code -ne 0 -or $baseline.Output -notmatch 'ZOMBIE_KIT_WARMUP_CHECKS_PASSED') { throw $baseline.Output }
$faults = @(
    @{ Name='background-zombie-work'; Source='ControllerSource'; Find='if (!Kits.ZombieKitWarmupPolicy.AllowsOptionalCatalogue(Session.ArenaSession.ActiveMode))'; Replace='if (false)'; Failure='Zombie never schedules optional background catalog work' },
    @{ Name='mutable-background-mode'; Source='ControllerSource'; Find='AllowsOptionalCatalogue(Session.ArenaSession.ActiveMode)'; Replace='AllowsOptionalCatalogue(Session.ArenaSession.ArmedMode)'; Failure='Zombie never schedules optional background catalog work' },
    @{ Name='whole-zombie-catalog'; Source='LoadingSource'; Find='scope = ZombieKitWarmupPolicy.LoadingScope(scope, mode);'; Replace='// no scope restriction'; Failure='Zombie loading requests only its opening kit, never the catalog' },
    @{ Name='unlatched-loading-mode'; Source='LoadingSource'; Find='LoadingScope(scope, mode)'; Replace='LoadingScope(scope, Session.ArenaSession.ActiveMode)'; Failure='Zombie loading requests only its opening kit, never the catalog' },
    @{ Name='own-gear-random-draw'; Source='LoadingSource'; Find='if (Session.ArenaSession.ZombieOwnGearArmed)'; Replace='if (false)'; Failure='Zombie loading requests only its opening kit, never the catalog' },
    @{ Name='unarmed-loading-work'; Source='LoadingSource'; Find='scope == KitPreloadScope.Off || !Session.ArenaSession.ArmedForNextRaid'; Replace='scope == KitPreloadScope.Off'; Failure='ordinary loading does not prewarm or reserve kits' },
    @{ Name='all-modes-capped'; Source='PolicySource'; Find='!AllowsOptionalCatalogue(mode) && requested == KitPreloadScope.WholeCatalogue'; Replace='requested == KitPreloadScope.WholeCatalogue'; Failure='non-Zombie loading scope unchanged' },
    @{ Name='opening-not-loaded'; Source='PolicySource'; Find='? KitPreloadScope.InitialKit'; Replace='? KitPreloadScope.Off'; Failure='Zombie loading requests only its opening kit, never the catalog' },
    @{ Name='required-preload-suppressed'; Source='PreloaderSource'; Find='if (kit == null || !IsCurrentRaid(epoch))'; Replace='if (kit == null || !IsCurrentRaid(epoch) || Session.ArenaSession.ActiveMode == "zombie")'; Failure='loading Off retains required cold opening preload' },
    @{ Name='failed-load-counted-warm'; Source='PreloaderSource'; Find='if (task != null && task.IsFaulted)'; Replace='if (false)'; Failure='failed required load must not be counted warm' }
)
foreach ($fault in $faults) {
    $sourceText = $warmupTexts[$fault.Source]
    if ($sourceText.Split(@($fault.Find), [StringSplitOptions]::None).Length -ne 2) { throw "Ambiguous mutation anchor: $($fault.Name)" }
    $copy = Join-Path $warmupArtifact ($fault.Name + '.cs')
    [IO.File]::WriteAllText($copy, $sourceText.Replace($fault.Find, $fault.Replace))
    $result = Invoke-Warmup ('Fault-' + $fault.Name) $fault.Source $copy
    [IO.File]::WriteAllText((Join-Path $warmupArtifact ($fault.Name + '.log')), $result.Output)
    if ($result.Code -eq 0 -or $result.Output -match ': error CS\d+' -or -not $result.Output.Contains($fault.Failure)) {
        throw "Fault not rejected by its behavioral assertion: $($fault.Name)`n$($result.Output)"
    }
    Write-Output "Rejected compiled fault: $($fault.Name)"
}
$final = Invoke-Warmup 'Release'
if ($final.Code -ne 0 -or $final.Output -notmatch 'ZOMBIE_KIT_WARMUP_CHECKS_PASSED') { throw $final.Output }
foreach ($key in $warmupSources.Keys) {
    if ((Get-FileHash -LiteralPath $warmupSources[$key]).Hash -ne $warmupHashes[$key]) { throw "Production source changed during checks: $key" }
}
[IO.File]::WriteAllText((Join-Path $warmupArtifact 'final.log'), $final.Output)
Write-Output $final.Output.Trim()
Write-Output "ZOMBIE_KIT_WARMUP_FAULTS_PASSED: $($faults.Count) compiled faults; $warmupArtifact"
