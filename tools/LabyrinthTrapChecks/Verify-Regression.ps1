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
    @{ Name = 'visual-state'; From = 'foreach (var animator in plan.Animators) animator._animatorActions[0].Execute(animator._target);'; To = '// mutation: skipped animator states'; Failure = 'native switch animation state applied' },
    @{ Name = 'recursive-fade'; From = 'loop.StopSound();'; To = 'loop.StopSound(); loop.OnLoopSourceFadeOut();'; Failure = 'only start coroutine cancelled; native fade and end timer retained' },
    @{ Name = 'end-timer-cancelled'; From = 'loop.StopCoroutine(pendingStart);'; To = 'loop.StopAllCoroutines();'; Failure = 'previously triggered native stop keeps its cleanup timers' }
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
