param([switch]$BaselineOnly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = Join-Path $repo 'ArenaMode.Client/Compat'
$burstPath = Join-Path $repo 'ArenaMode.Client/Bots/BurstSpawnService.cs'
$run = Join-Path $repo ('artifacts/morebots-checks/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$files = @('ArenaMoreBotsCompatibility.cs','ArenaMoreBotsRaidPolicy.cs')
$original = @{}
foreach ($f in $files) { $original[$f] = [IO.File]::ReadAllText((Join-Path $source $f)) }
$burst = [IO.File]::ReadAllText($burstPath)

function Invoke-Case([string]$name, [string]$file = '', [string]$find = '', [string]$replace = '', [string]$failure = '') {
    $case = Join-Path $run $name
    [IO.Directory]::CreateDirectory($case) | Out-Null
    foreach ($f in $files) {
        $content = $original[$f]
        if ($f -eq $file) {
            if ([regex]::Matches($content, [regex]::Escape($find)).Count -ne 1) { throw "Ambiguous fault anchor: $name" }
            $content = $content.Replace($find, $replace)
        }
        [IO.File]::WriteAllText((Join-Path $case $f), $content)
    }
    $ownedSpawns = [regex]::Matches($burst, 'new BotSpawnParams\s*(?:\{[^}]*\}|\(\))')
    if ($ownedSpawns.Count -ne 2) { throw 'Review changed Arena-owned spawn paths before updating checks.' }
    $constructors = ($ownedSpawns | ForEach-Object { $_.Value }) -join ",`n"
    if ($file -eq 'spawns') { $constructors = $constructors.Replace($find, $replace) }
    $spawnSource = Join-Path $case 'OwnedSpawns.cs'
    [IO.File]::WriteAllText($spawnSource, "internal static class OwnedSpawns { internal static BotSpawnParams[] BuildAll() => new BotSpawnParams[] { $constructors }; }")
    $log = Join-Path $case 'checks.log'
    & dotnet run --project (Join-Path $PSScriptRoot 'MoreBotsCompatibilityChecks.csproj') -c ('MoreBots_' + [IO.Path]::GetFileName($run) + '_' + $name) "-p:MoreBotsSourceRoot=$case" "-p:MoreBotsSpawnSource=$spawnSource" *> $log
    $exit = $LASTEXITCODE
    $output = [IO.File]::ReadAllText($log)
    if ($failure) {
        if ($exit -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains('InvalidOperationException: ' + $failure)) {
            throw "Fault did not compile and fail expected behavior: $name`n$output"
        }
        Write-Output "Rejected compiled fault: $name"
    }
    else {
        if ($exit -ne 0 -or !$output.Contains('MOREBOTS_RUNTIME_CHECKS_PASSED')) { throw $output }
        Write-Output $output.Trim()
    }
}

Invoke-Case 'baseline'
if (!$BaselineOnly) {
    Invoke-Case 'revenge-leak' 'ArenaMoreBotsCompatibility.cs' 'return !_raids.OwnsRevenge' 'return true || !_raids.OwnsRevenge' 'Arena kill never reaches persistent revenge queue'
    Invoke-Case 'end-leak' 'ArenaMoreBotsCompatibility.cs' '=> !_ready || settings == null || !_raids.OwnsEnd' '=> true || !_ready || settings == null || !_raids.OwnsEnd' 'end remains protected after live-world reset'
    Invoke-Case 'end-live-only' 'ArenaMoreBotsCompatibility.cs' '=> !_ready || settings == null || !_raids.OwnsEnd' '=> !Singleton<GameWorld>.Instantiated || !_ready || settings == null || !_raids.OwnsEnd' 'end remains protected after live-world reset'
    Invoke-Case 'identity-reuse' 'ArenaMoreBotsRaidPolicy.cs' 'string.Equals(identity, _identity, StringComparison.Ordinal)' 'true' 'changed request identity not suppressed'
    $receipt = "if (!HasOurPrefix(update, nameof(BeforeRevenge)) || !HasOurPrefix(end, nameof(BeforeRaidEnd))`n                || !HasOurPrefix(hunt, nameof(BeforeHuntCreated)) || !HasOurPrefix(enemy, nameof(BeforeEnemyRevenge)))"
    Invoke-Case 'missing-patch' 'ArenaMoreBotsCompatibility.cs' $receipt 'if (false)' 'failed unpatch retains Arena refusal'
    Invoke-Case 'hunt-safety-leak' 'ArenaMoreBotsCompatibility.cs' 'return player == null || !player.IsAI' 'return true || player == null || !player.IsAI' 'confirmed Arena null marker bypasses foreign hunt handler'
    Invoke-Case 'hunt-body-identity' 'ArenaMoreBotsCompatibility.cs' '!ReferenceEquals(world.GetEverExistedPlayerByID(player.ProfileId), player)' 'false' 'unregistered bot not claimed'
    Invoke-Case 'hunt-explicit-marker' 'ArenaMoreBotsCompatibility.cs' '!string.IsNullOrEmpty(bot.SpawnProfileData.SpawnParams.Id_spawn)' 'false' 'explicit Arena hunt remains foreign-owned'
    Invoke-Case 'enemy-revenge-leak' 'ArenaMoreBotsCompatibility.cs' '=> !TryGetProtectedWorld(out _, out _);' '=> true;' 'Arena team relationship cannot be overridden by revenge'
    Invoke-Case 'wrong-world' 'ArenaMoreBotsCompatibility.cs' '!ReferenceEquals(game.GameWorld, world)' 'false' 'wrong world cannot borrow Arena ownership'
    Invoke-Case 'hunt-prefix-missing' 'ArenaMoreBotsCompatibility.cs' 'Patches.Patch(hunt, prefix: Prefix(nameof(BeforeHuntCreated)));' '' 'reviewed API patches installed'
    Invoke-Case 'enemy-prefix-missing' 'ArenaMoreBotsCompatibility.cs' 'Patches.Patch(enemy, prefix: Prefix(nameof(BeforeEnemyRevenge)));' '' 'reviewed API patches installed'
    Invoke-Case 'cleanup-exception-escapes' 'ArenaMoreBotsCompatibility.cs' 'catch (Exception cleanupError)' 'catch (Exception cleanupError) when (false)' 'simulated internal unpatch failure'
    Invoke-Case 'cleanup-warning-spam' 'ArenaMoreBotsCompatibility.cs' 'if (!_cleanupWarned)' 'if (true)' 'cleanup and refusal warnings remain bounded'
    Invoke-Case 'failed-install-stays-ready' 'ArenaMoreBotsCompatibility.cs' "_ready = false;`n            _failure =" "_ready = true;`n            _failure =" 'failed unpatch retains Arena refusal'
    Invoke-Case 'null-spawn-marker' 'spawns' 'Id_spawn = "arenamode-owned"' 'Id_spawn = null' 'Arena-owned spawn marker is never null'
    Invoke-Case 'hunt-spawn-marker' 'spawns' 'Id_spawn = "arenamode-owned"' 'Id_spawn = "hunt"' 'Arena-owned spawn never opts into faction hunts'
}
foreach ($f in $files) { if ([IO.File]::ReadAllText((Join-Path $source $f)) -ne $original[$f]) { throw "Production source changed: $f" } }
if ([IO.File]::ReadAllText($burstPath) -ne $burst) { throw 'Spawn source changed during verification.' }
Write-Output "MOREBOTS_COMPATIBILITY_CHECKS_PASSED; evidence: $run"
