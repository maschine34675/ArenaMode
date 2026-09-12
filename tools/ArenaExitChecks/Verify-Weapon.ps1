$ErrorActionPreference='Stop'
$arenaRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arenaProject=Join-Path $PSScriptRoot 'ArenaExitChecks.csproj'
$arenaSource=[IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client/Bots/ArenaWeaponCallbackSafety.cs'))
$arenaScratch=Join-Path $arenaRepo ('artifacts/weapon-callback-checks/'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($arenaScratch)
& dotnet run --project $arenaProject -c Release
if($LASTEXITCODE -ne 0) { throw 'Weapon callback baseline failed' }
$arenaFaults=@(
 @{Name='Dead'; From='owner.IsDead || '; To=''; Failure='weapon callback admission: dead'},
 @{Name='Disposed'; From='owner.BotState == EBotState.Disposed'; To='false'; Failure='weapon callback admission: disposed'},
 @{Name='Manager'; From='&& manager._disposed'; To='&& false'; Failure='weapon callback admission: manager-disposed'},
 @{Name='Foreign'; From='&& ReferenceEquals(manager._owner, owner)'; To=''; Failure='weapon callback admission: foreign-manager'},
 @{Name='TemporaryDisable'; From='owner.BotState == EBotState.Disposed'; To='owner.BotState == EBotState.Disposed || owner.BotState == EBotState.NonActive'; Failure='weapon callback admission: nonactive'},
 @{Name='Ordinary'; From='|| !ArenaBotLifecycleScope.Owns(owner)'; To=''; Failure='weapon callback admission: ordinary'},
 @{Name='Changing'; From='__instance.IsChanging = false;'; To=''; Failure='weapon callback flags: dead'},
 @{Name='Ready'; From='__instance.IsChanging = false;'; To='__instance.IsChanging = false; __instance.IsWeaponReady = true;'; Failure='weapon callback flags: dead'}
)
foreach($arenaFault in $arenaFaults) {
 if([regex]::Matches($arenaSource,[regex]::Escape($arenaFault.From)).Count -ne 1) { throw 'Fault anchor changed' }
 $arenaFile=Join-Path $arenaScratch ($arenaFault.Name+'.cs')
 [IO.File]::WriteAllText($arenaFile,$arenaSource.Replace($arenaFault.From,$arenaFault.To))
 $arenaLog=& dotnet run --project $arenaProject -c ('WeaponCallback'+$arenaFault.Name) "-p:WeaponSource=$arenaFile" 2>&1 | Out-String
 [IO.File]::WriteAllText((Join-Path $arenaScratch ($arenaFault.Name+'.log')),$arenaLog)
 if($LASTEXITCODE -eq 0 -or !$arenaLog.Contains($arenaFault.Failure) -or $arenaLog -match 'error CS[0-9]') { throw ('Fault not rejected as intended: '+$arenaFault.Name+"`n"+$arenaLog) }
 Write-Output ('Rejected weapon callback fault: '+$arenaFault.Name)
}
& dotnet run --project $arenaProject -c Release
if($LASTEXITCODE -ne 0) { throw 'Weapon callback final baseline failed' }
Write-Output "ARENA_WEAPON_CALLBACK_REGRESSION_PASSED ($($arenaFaults.Count) faults; $arenaScratch)"
