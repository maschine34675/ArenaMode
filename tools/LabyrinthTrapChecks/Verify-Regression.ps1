$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSource = Join-Path $arenaRepo 'ArenaMode.Client\Session\LabyrinthSpawnTraps.cs'
$arenaProject = Join-Path $PSScriptRoot 'LabyrinthTrapChecks.csproj'
$arenaOriginal = [IO.File]::ReadAllText($arenaSource)
function Get-ArenaSha256([string] $Path) {
    $arenaSha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($arenaSha.ComputeHash([IO.File]::ReadAllBytes($Path))).Replace('-', '') }
    finally { $arenaSha.Dispose() }
}
$arenaHash = Get-ArenaSha256 $arenaSource
$arenaMutantDir = Join-Path ([IO.Path]::GetTempPath()) ('arena-trap-mutants-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($arenaMutantDir)
$arenaMutants = @(
    @{ Name = 'raid-ownership'; From = '!ArenaSession.IsExactActiveRaid(game, world)'; To = 'false'; Failure = 'stale world damage remains vanilla' },
    @{ Name = 'visual-state'; From = 'foreach (var animator in plan.Animators) animator._animatorActions[0].Execute(animator._target);'; To = '// mutation: skipped animator states'; Failure = 'toxic pool native drain animation requested' },
    @{ Name = 'recursive-fade'; From = 'loop.StopSound();'; To = 'loop.StopSound(); loop.OnLoopSourceFadeOut();'; Failure = 'only start coroutine cancelled; native fade and end timer retained' },
    @{ Name = 'end-timer-cancelled'; From = 'loop.StopCoroutine(pendingStart);'; To = 'loop.StopAllCoroutines();'; Failure = 'previously triggered native stop keeps its cleanup timers' },
    @{ Name = 'pool-not-owned'; From = 'new("DamageZone1", "toxicZoneEnter1_2612876931"'; To = 'new("DamageZone1", "not_owned_pool_entry"'; Failure = 'toxic pool damage prevented before drain animation' },
    @{ Name = 'pool-not-drained'; From = 'foreach (var animator in plan.Animators) animator._animatorActions[0].Execute(animator._target);'; To = 'foreach (var animator in plan.Animators) if (animator.gameObject.name != "INTERACTIVE_Labyrinth_water_02") animator._animatorActions[0].Execute(animator._target);'; Failure = 'toxic pool native drain animation requested' },
    @{ Name = 'shared-pool-control-id'; From = 'x._triggerId == id && x.gameObject.name == name'; To = 'x._triggerId == id'; Failure = 'all twelve entry trigger IDs disabled' }
)
foreach ($arenaMutant in $arenaMutants) {
    if (-not $arenaOriginal.Contains($arenaMutant.From)) { throw "Missing mutation anchor: $($arenaMutant.Name)" }
    $arenaPath = Join-Path $arenaMutantDir ($arenaMutant.Name + '.cs')
    [IO.File]::WriteAllText($arenaPath, $arenaOriginal.Replace($arenaMutant.From, $arenaMutant.To))
    $arenaPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arenaOutput = & dotnet run --project $arenaProject -c Release "-p:TrapSource=$arenaPath" 2>&1 | Out-String
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaPreference }
    if ($arenaExit -eq 0 -or -not $arenaOutput.Contains($arenaMutant.Failure)) {
        throw "Mutant was not rejected by its intended assertion: $($arenaMutant.Name)`n$arenaOutput"
    }
    Write-Output "Rejected mutant: $($arenaMutant.Name)"
}
if ((Get-ArenaSha256 $arenaSource) -ne $arenaHash) { throw 'Production source changed' }
& dotnet run --project $arenaProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Production checks failed' }
Write-Output 'ARENA_LABYRINTH_TRAP_REGRESSION_PASSED'
