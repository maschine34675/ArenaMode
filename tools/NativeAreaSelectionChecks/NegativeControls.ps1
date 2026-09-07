param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSourcePath = Join-Path $arenaRepo 'ArenaMode.Client\Session\ArenaMenuSelection.cs'
$arenaSource = [IO.File]::ReadAllText($arenaSourcePath)
$arenaScratch = Join-Path ([IO.Path]::GetTempPath()) ('arena-area-menu-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($arenaScratch)
$arenaMutations = @(
    @{ Name = 'HiddenCustoms'; From = 'public static bool IsMapDiscoverable(string map) => AreaSelection(map).Available;';
       To = 'public static bool IsMapDiscoverable(string map) => IsMapAvailable(map);';
       Failure = 'Customs stays discoverable after F12 area downgrade' },
    @{ Name = 'WrongOwner'; From = 'if (!IsSelectedFor(owner)) return false;';
       To = 'if (owner == null) return false;'; Failure = 'Different setup owner cannot repair Arena settings' },
    @{ Name = 'FailedWrite'; From = 'proposal.Available && Config.Settings.TrySelectArea(proposal.SelectedId)';
       To = 'proposal.Available && (Config.Settings.TrySelectArea(proposal.SelectedId) || true)';
       # Strict IsMapAvailable still rejects Customs. Verify refusal on a valid
       # current map too, where it must not be promoted into a successful click.
       Failure = 'A refused write must not be promoted to successful preparation' }
)
foreach ($arenaMutation in $arenaMutations) {
    if (!$arenaSource.Contains($arenaMutation.From)) { throw "Missing mutation anchor: $($arenaMutation.Name)" }
    $arenaMutant = Join-Path $arenaScratch ($arenaMutation.Name + '.cs')
    [IO.File]::WriteAllText($arenaMutant, $arenaSource.Replace($arenaMutation.From, $arenaMutation.To))
    # Windows PowerShell surfaces the intentionally failing process's stderr
    # as error records. Inspect its exit and assertion instead of aborting here.
    $arenaPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arenaOutput = & dotnet run --project (Join-Path $arenaRepo 'tools\NativeMenuChecks') -c ('AreaMenuMutant' + $arenaMutation.Name) `
            "-p:SPTPath=$SptRoot" "-p:ProductionMenuSelectionSource=$arenaMutant" 2>&1 | ForEach-Object { $_.ToString() } | Out-String
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaPreference }
    if ($arenaExit -eq 0 -or !$arenaOutput.Contains($arenaMutation.Failure)) {
        throw "Area menu mutant failed to expose its intended regression: $($arenaMutation.Name)`n$arenaOutput"
    }
    Write-Output "Rejected area menu mutant: $($arenaMutation.Name)"
}
if ([IO.File]::ReadAllText($arenaSourcePath) -cne $arenaSource) { throw 'Production source changed during negative controls.' }
Write-Output 'ARENA_AREA_MENU_NEGATIVE_CONTROLS_PASSED'
