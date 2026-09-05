$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSource = Join-Path $arenaRepo 'ArenaMode.Client\Bots\CorpseCleanupService.cs'
$arenaProject = Join-Path $PSScriptRoot 'CorpseCleanupChecks.csproj'
$arenaOriginal = [IO.File]::ReadAllText($arenaSource)
function Get-ArenaSha256([string] $Path) {
    $arenaSha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($arenaSha.ComputeHash([IO.File]::ReadAllBytes($Path))).Replace('-', '') }
    finally { $arenaSha.Dispose() }
}
$arenaHash = Get-ArenaSha256 $arenaSource
$arenaMutantDir = Join-Path ([IO.Path]::GetTempPath()) ('arena-cleanup-mutants-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($arenaMutantDir)
$arenaPair = @'
                        try
                        {
                            result.Value.RaiseEvents(controller, EFT.InventoryLogic.CommandStatus.Begin);
                        }
                        finally
                        {
                            result.Value.RaiseEvents(controller, EFT.InventoryLogic.CommandStatus.Succeed);
                        }
'@
$arenaUnprotectedPair = @'
                        result.Value.RaiseEvents(controller, EFT.InventoryLogic.CommandStatus.Begin);
                        result.Value.RaiseEvents(controller, EFT.InventoryLogic.CommandStatus.Succeed);
'@
$arenaNormalized = $arenaOriginal.Replace("`r`n", "`n")
$arenaMutants = @(
    @{ Name = 'missing-begin'; From = 'result.Value.RaiseEvents(controller, EFT.InventoryLogic.CommandStatus.Begin);'; To = '// mutation: missing Begin'; Failure = 'successful cleanup has no missing Begin activity' },
    @{ Name = 'missing-completion'; From = 'result.Value.RaiseEvents(controller, EFT.InventoryLogic.CommandStatus.Succeed);'; To = '// mutation: missing completion'; Failure = 'successful cleanup leaves no active activities' },
    @{ Name = 'existing-activity'; From = 'controller.SelectEvents(item).Any()'; To = 'false'; Failure = 'pre-existing same-item activity is preserved' },
    @{ Name = 'missing-finally'; From = $arenaPair.Replace("`r`n", "`n"); To = $arenaUnprotectedPair.Replace("`r`n", "`n"); Failure = 'tested failure boundary leaves no unpaired activity: begin' }
)
foreach ($arenaMutant in $arenaMutants) {
    if (-not $arenaNormalized.Contains($arenaMutant.From)) { throw "Missing mutation anchor: $($arenaMutant.Name)" }
    $arenaPath = Join-Path $arenaMutantDir ($arenaMutant.Name + '.cs')
    [IO.File]::WriteAllText($arenaPath, $arenaNormalized.Replace($arenaMutant.From, $arenaMutant.To))
    $arenaPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arenaOutput = & dotnet run --project $arenaProject -c Release "-p:CleanupSource=$arenaPath" 2>&1 | Out-String
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
Write-Output 'ARENA_CORPSE_CLEANUP_REGRESSION_PASSED'
