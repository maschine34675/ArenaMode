param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSpt = (Resolve-Path -LiteralPath $SptRoot).Path
$arenaEvidence = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $arenaEvidence -Force | Out-Null
$arenaNative = Join-Path $arenaRepo 'tools\NativeMenuChecks\NativeMenuChecks.csproj'
$arenaView = Join-Path $PSScriptRoot 'AreaTeamMenuChecks.csproj'
$arenaHooks = Join-Path $arenaRepo 'tools\NativeAreaHookChecks\NativeAreaHookChecks.csproj'
function Invoke-AreaMenuCheck([string]$Project, [string]$Name, [string]$Override = '') {
    $arenaArguments = @('run','--project',$Project,'-c','Release',"-p:SPTPath=$arenaSpt")
    if ($Override) { $arenaArguments += $Override }
    $arenaOld = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arenaOutput = (& dotnet @arenaArguments 2>&1 | ForEach-Object { "$_" }) -join "`n"
        $arenaCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaOld }
    $arenaOutput | Set-Content -LiteralPath (Join-Path $arenaEvidence ($Name + '.log'))
    return [pscustomobject]@{ Code=$arenaCode; Output=$arenaOutput }
}
$arenaChecks = @(
    @($arenaNative,'native','ARENA_MENU_CHECKS_PASSED'),
    @($arenaHooks,'hooks','NATIVE_AREA_HOOK_CHECKS_PASSED'),
    @($arenaView,'view','ARENA_TEAM_AREA_VIEW_CHECKS_PASSED'))
foreach ($arenaCheck in $arenaChecks) {
    $arenaResult = Invoke-AreaMenuCheck $arenaCheck[0] $arenaCheck[1]
    if ($arenaResult.Code -ne 0 -or -not $arenaResult.Output.Contains($arenaCheck[2])) { throw $arenaResult.Output }
    Write-Output $arenaResult.Output
}

# Mutate compiler inputs only; never rewrite production files or build/deploy a
# mod. Failure must be the expected behavioral assertion, not a compile error.
$arenaFaults = @(
    @('wrong-ack','ArenaMode.Client\Session\ArenaServerClient.cs',
      'parsed["areaTeamSize"].Value<int>() == selectedSize','true',
      $arenaNative,'ProductionServerClientSource','missing, wrong or coercible size acknowledgement'),
    @('lost-frozen-size','ArenaMode.Client\Session\LocalRaidStartedPatch.cs',
      'Areas.ArenaAreaRuntime.Arm(area, ArenaServerClient.PendingArmToken, areaTeamSize);',
      'Areas.ArenaAreaRuntime.Arm(area, ArenaServerClient.PendingArmToken, 0);',
      $arenaNative,'ProductionLocalStartSource','real negotiation arms exact menu size'),
    @('only-two-rows','ArenaMode.Client\Session\ArenaAreaSelectionView.cs',
      'private const int MaxRows = 3;','private const int MaxRows = 2;',
      $arenaView,'ProductionAreaViewSource','at most three visible choices'),
    @('retired-size-row','ArenaMode.Client\Session\ArenaAreaSelectionView.cs',
      '|| !_sizeRows.Contains(row)) return;','|| false) return;',
      $arenaView,'ProductionAreaViewSource','same-view Close/Show replacement cannot inherit old row ownership'))
try {
    foreach ($arenaFault in $arenaFaults) {
        $arenaSource = Get-Content -Raw -LiteralPath (Join-Path $arenaRepo $arenaFault[1])
        if (($arenaSource.Split([string[]]@($arenaFault[2]),[StringSplitOptions]::None)).Count -ne 2) {
            throw "Expected one mutation site: $($arenaFault[0])"
        }
        $arenaMutant = Join-Path $arenaEvidence ($arenaFault[0] + '.cs')
        [IO.File]::WriteAllText($arenaMutant,$arenaSource.Replace($arenaFault[2],$arenaFault[3]))
        $arenaResult = Invoke-AreaMenuCheck $arenaFault[4] $arenaFault[0] ("-p:" + $arenaFault[5] + '=' + $arenaMutant)
        if ($arenaResult.Code -eq 0 -or -not $arenaResult.Output.Contains($arenaFault[6])) { throw $arenaResult.Output }
        Write-Output "NEGATIVE CONTROL PASSED: $($arenaFault[0])"
    }
}
finally {
    # Mutated linked files may remain cached at the same output path. Force an
    # ordinary-source rebuild before restoring and rerunning all checks.
    foreach ($arenaProject in @($arenaNative,$arenaView)) {
        & dotnet build $arenaProject -c Release -t:Rebuild "-p:SPTPath=$arenaSpt" --nologo -v:quiet
        if ($LASTEXITCODE -ne 0) { throw 'Could not restore ordinary standalone compiler inputs.' }
    }
}
foreach ($arenaCheck in $arenaChecks) {
    $arenaResult = Invoke-AreaMenuCheck $arenaCheck[0] ($arenaCheck[1] + '-restored')
    if ($arenaResult.Code -ne 0 -or -not $arenaResult.Output.Contains($arenaCheck[2])) { throw $arenaResult.Output }
}
Write-Output 'ARENA_TEAM_MENU_CHECKS_PASSED'
