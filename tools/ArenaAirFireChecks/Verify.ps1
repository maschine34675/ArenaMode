$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arenaEvidence = Join-Path $arenaRepo ('artifacts/air-fire-checks/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($arenaEvidence) | Out-Null
$arenaFire = Join-Path $arenaRepo 'ArenaMode.Client/Movement/ArenaAirFire.cs'
$arenaScope = Join-Path $arenaRepo 'ArenaMode.Client/Movement/ArenaMovementScope.cs'
$arenaHashes = @{}
foreach ($path in @($arenaFire,$arenaScope)) { $arenaHashes[$path] = (Get-FileHash -LiteralPath $path).Hash }
$arenaHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $arenaEvidence 'production-sources.json')
function Invoke-Case([string]$name,[string[]]$extra=@()) {
    $result = & dotnet run --project (Join-Path $PSScriptRoot 'ArenaAirFireChecks.csproj') -c $name @extra 2>&1 | Out-String
    $code = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $arenaEvidence ($name+'.log')),$result)
    return @{ Output=$result; Code=$code }
}
$baseline = Invoke-Case 'Release'
if ($baseline.Code -ne 0 -or !$baseline.Output.Contains('ARENA_AIR_FIRE_PRODUCTION_PASSED')) { throw $baseline.Output }
Write-Output $baseline.Output.Trim()
$faults = @(
    @{Name='LostFeatureGate';Path=$arenaFire;Property='AirFireSource';From='|| Settings.AirHipFireEnabled?.Value != true';To='';Failure='air rejects feature off'},
    @{Name='LostScope';Path=$arenaFire;Property='AirFireSource';From='!ArenaMovementScope.IsLocal(player, requireInput: true)';To='false';Failure='fire rejects master off'},
    @{Name='AirAds';Path=$arenaFire;Property='AirFireSource';From='|| firearm.IsAiming';To='';Failure='air rejects aiming'},
    @{Name='OtherJumpType';Path=$arenaFire;Property='AirFireSource';From='&& jump.GetType() == typeof(JumpPlayerState)';To='';Failure='air rejects derived state'},
    @{Name='PendingMantle';Path=$arenaFire;Property='AirFireSource';From='&& !ArenaAirVault.HasPendingRequest(player)';To='';Failure='air rejects pending mantle'},
    @{Name='NativeTrueChanged';Path=$arenaFire;Property='AirFireSource';From='if (original || !ArenaSession.IsActive';To='if (!ArenaSession.IsActive';Failure='native true remains unchanged: master off'},
    @{Name='AmbiguousRewrite';Path=$arenaFire;Property='AirFireSource';From='if (matches.Length != 1)';To='if (matches.Length == 0)';Failure='ambiguous or missing native call fails open 2'},
    @{Name='OwnerHandover';Path=$arenaScope;Property='ScopeSource';From='&& ReferenceEquals(game.LocalPlayer, player)';To='';Failure='scope rejects owner handover'},
    @{Name='ForeignBody';Path=$arenaScope;Property='ScopeSource';From='ReferenceEquals(world.MainPlayer, player) &&';To='';Failure='scope rejects foreign current body'},
    @{Name='StoppedGame';Path=$arenaScope;Property='ScopeSource';From='&& game.Status == GameStatus.Started';To='';Failure='scope rejects stopping'}
)
foreach ($fault in $faults) {
    $source = [IO.File]::ReadAllText($fault.Path)
    if (!$source.Contains($fault.From)) { throw "Missing source fault anchor: $($fault.Name)" }
    $copy = Join-Path $arenaEvidence ($fault.Name+'.cs')
    [IO.File]::WriteAllText($copy,$source.Replace($fault.From,$fault.To))
    $result = Invoke-Case $fault.Name @("-p:$($fault.Property)=$copy")
    if ($result.Code -eq 0 -or $result.Output -match 'error CS\d+' -or !$result.Output.Contains('ASSERT: '+$fault.Failure)) { throw "Wrong source-fault outcome: $($fault.Name): $($result.Output)" }
    Write-Output "Rejected source fault: $($fault.Name)"
}
$final = Invoke-Case 'Release'
if ($final.Code -ne 0 -or !$final.Output.Contains('ARENA_AIR_FIRE_PRODUCTION_PASSED')) { throw $final.Output }
foreach ($path in $arenaHashes.Keys) { if ((Get-FileHash -LiteralPath $path).Hash -ne $arenaHashes[$path]) { throw "Production changed during checks: $path" } }
& (Join-Path $PSScriptRoot 'Verify-Installed.ps1')
Write-Output "Evidence: $arenaEvidence"
Write-Output 'ARENA_AIR_FIRE_CHECKS_PASSED'
