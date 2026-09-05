$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSource = Join-Path $arenaRepo 'ArenaMode.Client\Audio\LabyrinthAlarmPatches.cs'
$arenaProject = Join-Path $PSScriptRoot 'LabyrinthAlarmChecks.csproj'
$arenaOriginal = Get-Content -LiteralPath $arenaSource -Raw
function Get-ArenaSha256([string] $Path) {
    $arenaSha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($arenaSha.ComputeHash([IO.File]::ReadAllBytes($Path))).Replace('-', '') }
    finally { $arenaSha.Dispose() }
}
$arenaHash = Get-ArenaSha256 $arenaSource
$arenaMutantDir = Join-Path ([IO.Path]::GetTempPath()) ('arena-alarm-mutants-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($arenaMutantDir)

$arenaMutants = @(
    @{ Name = 'raid-ownership'; From = '&& ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, world)'; To = '&& true'; Failure = 'stale world keeps vanilla output' },
    @{ Name = 'source-membership'; From = '&& Array.IndexOf(player._sources, source) >= 0'; To = '&& true'; Failure = 'not a registered source remains untouched' },
    @{ Name = 'source-clip'; From = '&& ReferenceEquals(source.clip, player._clip)'; To = '&& true'; Failure = 'source reused for another clip remains untouched' }
)
foreach ($arenaMutant in $arenaMutants) {
    if (-not $arenaOriginal.Contains($arenaMutant.From)) { throw "Mutation anchor missing: $($arenaMutant.Name)" }
    $arenaMutantPath = Join-Path $arenaMutantDir ($arenaMutant.Name + '.cs')
    [IO.File]::WriteAllText($arenaMutantPath, $arenaOriginal.Replace($arenaMutant.From, $arenaMutant.To))
    $arenaPreviousPreference = $ErrorActionPreference
    try {
        # Windows PowerShell wraps expected native stderr in ErrorRecords.
        $ErrorActionPreference = 'Continue'
        $arenaOutput = & dotnet run --project $arenaProject -c Release "-p:AlarmSource=$arenaMutantPath" 2>&1 | Out-String
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaPreviousPreference }
    if ($arenaExit -eq 0 -or -not $arenaOutput.Contains($arenaMutant.Failure)) {
        throw "Mutant was not rejected by its intended assertion: $($arenaMutant.Name)`n$arenaOutput"
    }
    Write-Output "Rejected mutant: $($arenaMutant.Name)"
}
if ((Get-ArenaSha256 $arenaSource) -ne $arenaHash) { throw 'Production source changed' }
& dotnet run --project $arenaProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Production regression checks failed' }
Write-Output 'ARENA_LABYRINTH_ALARM_REGRESSION_PASSED'
