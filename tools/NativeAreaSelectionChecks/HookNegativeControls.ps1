$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSourcePath = Join-Path $arenaRepo 'ArenaMode.Client\Session\ArenaLocationSelectionPatch.cs'
$arenaSource = [IO.File]::ReadAllText($arenaSourcePath)
$arenaScratch = Join-Path ([IO.Path]::GetTempPath()) ('arena-area-hooks-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($arenaScratch)
foreach ($arenaMutation in @(
    @{ Name = 'LateWrite'; From = 'if (!ArenaMenuSelection.TryPrepareMapArea(settings, target?.Id)) location = null;';
       To = '// Mutant: let the setter reject this too late.';
       Failure = 'rejected Customs area write enters native null branch, not its later dereference' },
    @{ Name = 'PftMissingRows'; From = 'location?.Id, ArenaMenuSelection.IsMapConfigured);';
       To = 'location?.Id, ArenaMenuSelection.IsMapDiscoverable);';
       Failure = 'PFT wrong saved mode must not prevent native map materialization before postfix mode repair' },
    @{ Name = 'StaleCleanup'; From = 'if (!ReferenceEquals(state.Generation, generation)) return;';
       To = 'if (!ReferenceEquals(state.Generation, generation)) { state.Restore(); return; }';
       Failure = 'stale updater generation must not destroy newer screen views' }
)) {
    if (!$arenaSource.Contains($arenaMutation.From)) { throw "Missing hook mutation anchor: $($arenaMutation.Name)" }
    $arenaMutant = Join-Path $arenaScratch ($arenaMutation.Name + '.cs')
    [IO.File]::WriteAllText($arenaMutant, $arenaSource.Replace($arenaMutation.From, $arenaMutation.To))
    $arenaPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arenaOutput = & dotnet run --project (Join-Path $arenaRepo 'tools\NativeAreaHookChecks') -c ('AreaHookMutant' + $arenaMutation.Name) `
            "-p:ProductionLocationPatchSource=$arenaMutant" 2>&1 | ForEach-Object { $_.ToString() } | Out-String
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaPreference }
    if ($arenaExit -eq 0 -or !$arenaOutput.Contains($arenaMutation.Failure)) {
        throw "Area hook mutant failed to expose its intended regression: $($arenaMutation.Name)`n$arenaOutput"
    }
    Write-Output "Rejected area hook mutant: $($arenaMutation.Name)"
}
if ([IO.File]::ReadAllText($arenaSourcePath) -cne $arenaSource) { throw 'Production hook changed during negative controls.' }
& dotnet run --project (Join-Path $arenaRepo 'tools\NativeAreaHookChecks') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Production hook baseline did not recover after negative controls.' }
Write-Output 'ARENA_AREA_HOOK_NEGATIVE_CONTROLS_PASSED'
