$ErrorActionPreference = 'Stop'
$arenaProject = Join-Path $PSScriptRoot 'ArenaExitChecks.csproj'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arenaScratch = Join-Path $arenaRepo ('artifacts/exit-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($arenaScratch)
& dotnet run --project $arenaProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Exit safety baseline failed' }
$arenaMutants = @(
 @{Name='NoCancellation'; Source='Pickup'; Anchor='context.PickupAction = null;'; Change=''; Failure='exact pending pickup cancellation: owned'},
 @{Name='CancelPreActive'; Source='Pickup'; Anchor='|| __instance.BotState == EBotState.PreActive'; Change=''; Failure='exact pending pickup cancellation: preactive'},
 @{Name='OrdinaryPickup'; Source='Pickup'; Anchor='|| !ArenaBotLifecycleScope.Owns(__instance)'; Change=''; Failure='exact pending pickup cancellation: ordinary'},
 @{Name='ForeignBody'; Source='Pickup'; Anchor='|| !ReferenceEquals(pending.owner, player)'; Change=''; Failure='exact pending pickup cancellation: foreign-body'},
 @{Name='ForeignTaker'; Source='Pickup'; Anchor='|| !ReferenceEquals(pending.botItemTaker_0, __instance.ItemTaker)'; Change=''; Failure='exact pending pickup cancellation: foreign-taker'},
 @{Name='SkipInitialized'; Source='Disable'; Anchor='|| _activation.GetValue(__instance) != null'; Change=''; Failure='disable original admission: initialized'},
 @{Name='OrdinaryDisable'; Source='Disable'; Anchor='|| !ArenaBotLifecycleScope.Owns(owner)'; Change=''; Failure='disable original admission: ordinary'},
 @{Name='LoseCoroutines'; Source='Disable'; Anchor='__instance.StopAllCoroutines();'; Change=''; Failure='only guarded missing activation retains coroutine cleanup'},
 @{Name='AcceptChangedBody'; Source='Disable'; Anchor='return il?.Length == 19'; Change='return true || il?.Length == 19'; Failure='changed disable body rejected'}
)
foreach($arenaMutant in $arenaMutants) {
 $arenaName = if($arenaMutant.Source -eq 'Pickup') { 'ArenaPickupTeardownSafety' } else { 'ArenaSainDisableSafety' }
 $arenaText = [IO.File]::ReadAllText((Join-Path $arenaRepo ('ArenaMode.Client/Bots/'+$arenaName+'.cs')))
 if ([regex]::Matches($arenaText,[regex]::Escape($arenaMutant.Anchor)).Count -ne 1) { throw ('Fault anchor ambiguous: '+$arenaMutant.Name) }
 $arenaFile = Join-Path $arenaScratch ($arenaMutant.Name+'.cs')
 [IO.File]::WriteAllText($arenaFile,$arenaText.Replace($arenaMutant.Anchor,$arenaMutant.Change))
 $arenaOutput = & dotnet run --project $arenaProject -c ('ExitFault'+$arenaMutant.Name) "-p:$($arenaMutant.Source)Source=$arenaFile" 2>&1 | Out-String
 [IO.File]::WriteAllText((Join-Path $arenaScratch ($arenaMutant.Name+'.log')),$arenaOutput)
 if($LASTEXITCODE -eq 0 -or !$arenaOutput.Contains($arenaMutant.Failure)) { throw ('Wrong fault result: '+$arenaMutant.Name+"`n"+$arenaOutput) }
 Write-Output ('Rejected exit fault: '+$arenaMutant.Name)
}
& dotnet run --project $arenaProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Exit safety final baseline failed' }
Write-Output "ARENA_EXIT_REGRESSION_PASSED ($($arenaMutants.Count) compiled faults; $arenaScratch)"
