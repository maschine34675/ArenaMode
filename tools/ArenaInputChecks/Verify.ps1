$ErrorActionPreference = 'Stop'
$arenaProject = Join-Path $PSScriptRoot 'ArenaInputChecks.csproj'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arenaScratch = Join-Path $arenaRepo ('artifacts/input-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($arenaScratch)
& dotnet run --project $arenaProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Input baseline failed' }
$arenaText = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client/Hud/ArenaInputBlocker.cs'))
$arenaMutants = @(
 @{Name='ImmediateRelease'; Anchor='_drain = true;'; Change='_drain = false;'; Failure='close is not immediate input release'},
 @{Name='StaleCollection'; Anchor='_releaseCollected = false; // Older, buffered input is not a release sample.'; Change='_releaseCollected = true;'; Failure='pre-close collected batch cannot release'},
 @{Name='HeldPointer'; Anchor='Input.GetMouseButton(i) || '; Change=''; Failure='held pointer keeps close protected'},
 @{Name='SameFrameClick'; Anchor=' || Input.GetMouseButtonDown(i)'; Change=''; Failure='same-frame down and up is not a quiet sample'},
 @{Name='SmoothDrain'; Anchor='&& ReferenceEquals(__instance, _manager) && ReferenceEquals(__0, _commandList)'; Change='&& ReferenceEquals(__instance, _manager)'; Failure='SuperSmooth empty delivery cannot drain real collection'},
 @{Name='ForeignPreset'; Anchor='|| !ReferenceEquals(__instance, _preset)'; Change=''; Failure='foreign preset cannot supply release evidence'},
 @{Name='ForeignList'; Anchor='|| !ReferenceEquals(__0, _commandList)'; Change=''; Failure='foreign list cannot supply release evidence'},
 @{Name='ForeignAxes'; Anchor='|| !ReferenceEquals(__1, _axisList)'; Change=''; Failure='foreign axes cannot supply release evidence'},
 @{Name='UninitializedKeys'; Anchor='_workingKeys.GetValue(__instance) != null && '; Change=''; Failure='uninitialized combinations cannot certify fresh input'},
 @{Name='WrongGeneration'; Anchor='|| __state != _generation'; Change=''; Failure='reentrant reopen and close cannot consume new generation'},
 @{Name='ReopenOldSample'; Anchor='Owners.Add(owner) && _drain'; Change='Owners.Add(owner) && false'; Failure='reopen invalidates a previously ready batch even without UI close flag'},
 @{Name='UnknownClose'; Anchor='Owners.Remove(owner) && drainOnRelease'; Change='(Owners.Remove(owner) || true) && drainOnRelease'; Failure='unknown repeated and null releases cannot latch input'},
 @{Name='LostCleanup'; Anchor='finally { ResetScope(); }'; Change='finally { }'; Failure="reinstall cannot inherit a failed cleanup's old close"},
 @{Name='LiveDisabledHook'; Anchor='if (!_ready || !SyncScope() || (Owners.Count == 0 && !_drain))'; Change='if (!SyncScope() || (Owners.Count == 0 && !_drain))'; Failure='remaining detour after failed unpatch is harmless'},
 @{Name='NoNativeDrain'; Anchor='|| __0 || !NativeMenuIgnored()'; Change='|| true'; Failure='native resume starts drain before clearing native gate'},
 @{Name='RepeatedNativeRelease'; Anchor='|| !NativeMenuIgnored()'; Change=''; Failure='repeated native false cannot relatch input'},
 @{Name='ForeignNativeBody'; Anchor='ReferenceEquals(player, _world?.MainPlayer)'; Change='true'; Failure="old native player cannot latch replacement body's input"},
 @{Name='BlockNativeNavigation'; Anchor='(Owners.Count == 0 && !_drain)) return;'; Change='(Owners.Count == 0 && !_drain && !NativeMenuIgnored())) return;'; Failure='native menu retains its own command navigation'}
)
foreach ($arenaMutant in $arenaMutants) {
 if ([regex]::Matches($arenaText,[regex]::Escape($arenaMutant.Anchor)).Count -ne 1) { throw ('Fault anchor ambiguous: '+$arenaMutant.Name) }
 $arenaFile = Join-Path $arenaScratch ($arenaMutant.Name+'.cs')
 [IO.File]::WriteAllText($arenaFile,$arenaText.Replace($arenaMutant.Anchor,$arenaMutant.Change))
 $arenaOutput = & dotnet run --project $arenaProject -c ('InputFault'+$arenaMutant.Name) "-p:BlockerSource=$arenaFile" 2>&1 | Out-String
 [IO.File]::WriteAllText((Join-Path $arenaScratch ($arenaMutant.Name+'.log')),$arenaOutput)
 if ($LASTEXITCODE -eq 0 -or !$arenaOutput.Contains($arenaMutant.Failure) -or $arenaOutput -match 'error CS[0-9]') { throw ('Wrong fault result: '+$arenaMutant.Name+"`n"+$arenaOutput) }
 Write-Output ('Rejected input fault: '+$arenaMutant.Name)
}
& dotnet run --project $arenaProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Input final baseline failed' }
Write-Output "ARENA_INPUT_REGRESSION_PASSED ($($arenaMutants.Count) compiled faults; $arenaScratch)"
