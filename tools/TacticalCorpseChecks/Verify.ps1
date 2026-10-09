param([string]$OutputRoot = '')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$OutputRoot) { $OutputRoot = Join-Path $repo ('artifacts/tactical-corpse-checks/' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$project = Join-Path $PSScriptRoot 'TacticalCorpseChecks.csproj'
$retention = Get-Content (Join-Path $repo 'ArenaMode.Client/Modes/TacticalMode.Corpses.cs') -Raw
$cleanup = Get-Content (Join-Path $repo 'ArenaMode.Client/Bots/CorpseCleanupService.cs') -Raw
$faults = @(
    @('always-release', 'RetentionSource', 'return true;', 'return false;', 'FROZEN no physical release'),
    @('solo-leak', 'RetentionSource', '!_teamArena ||', 'false ||', 'SCOPE solo Tactical'),
    @('foreign-match', 'RetentionSource', '!ReferenceEquals(ArenaMatchService.Current, _match)', 'false', 'SCOPE different match'),
    @('foreign-mode', 'RetentionSource', '!ReferenceEquals(_match?.Mode, this)', 'false', 'SCOPE different mode'),
    @('foreign-world', 'RetentionSource', '!ReferenceEquals(Singleton<GameWorld>.Instance, world)', 'false', 'SCOPE wrong singleton world'),
    @('foreign-game', 'RetentionSource', '!ReferenceEquals(Singleton<AbstractGame>.Instance, _teamGame)', 'false', 'SCOPE wrong singleton game'),
    @('wrong-started-raid', 'RetentionSource', '!ArenaSession.IsExactActiveRaid(_teamGame, world)', 'false', 'SCOPE wrong started world'),
    @('partial-roster', 'RetentionSource', '_boundTeamEnemies.Length != TeamEnemyCount', '_boundTeamEnemies.Length > TeamEnemyCount', 'RETIRED all held bodies release'),
    @('oversized-roster', 'RetentionSource', '_boundTeamEnemies.Length != TeamEnemyCount', '_boundTeamEnemies.Length < TeamEnemyCount', 'SCOPE oversized roster'),
    @('profile-only', 'RetentionSource', 'ReferenceEquals(owner.GetPlayer, player)', 'owner.GetPlayer.ProfileId == player.ProfileId', 'EXACT-BODY reused profile'),
    @('destroyed-owner', 'RetentionSource', 'owner != null &&', '!ReferenceEquals(owner, null) &&', 'SCOPE destroyed owner'),
    @('destroyed-player', 'RetentionSource', 'player == null', 'ReferenceEquals(player, null)', 'SCOPE destroyed body'),
    @('unwired-release-gate', 'CleanupSource', '&& tactical.RetainsFrozenOpponentCorpse(player, world)', '&& false', 'FROZEN no physical release'),
    @('inverted-release-gate', 'CleanupSource', '&& tactical.RetainsFrozenOpponentCorpse(player, world)', '&& !tactical.RetainsFrozenOpponentCorpse(player, world)', 'FROZEN no physical release'),
    @('sticky-permanent-protection', 'CleanupSource', '&& tactical.RetainsFrozenOpponentCorpse(player, world)', '&& (state.Protected = tactical.RetainsFrozenOpponentCorpse(player, world))', 'RETIRED all held bodies release'),
    @('release-live-body', 'CleanupSource', 'player.HealthController?.IsAlive != false', 'false', 'LIVE-LOSS never infer dead')
)
$baseline = Join-Path $OutputRoot 'baseline.log'
& dotnet run --project $project -c Release *> $baseline
if ($LASTEXITCODE -ne 0) { throw "Production failed: $baseline" }
foreach ($fault in $faults) {
    $name,$property,$before,$after,$expected = $fault
    $source = if ($property -eq 'RetentionSource') { $retention } else { $cleanup }
    if (!$source.Contains($before)) { throw "Missing anchor: $name" }
    $file = Join-Path $OutputRoot ($name + '.cs')
    [IO.File]::WriteAllText($file, $source.Replace($before,$after))
    $log = Join-Path $OutputRoot ($name + '.log')
    $ErrorActionPreference = 'Continue'
    & dotnet run --project $project -c Release "-p:${property}=$file" *> $log
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($code -eq 0) { throw "Fault survived: $name" }
    if (Select-String -LiteralPath $log -Pattern ': error (CS|MSB)' -Quiet) { throw "Fault did not compile: $name" }
    if (!(Select-String -LiteralPath $log -SimpleMatch $expected -Quiet)) { throw "Wrong failure: $name expected $expected, inspect $log" }
    Write-Host "rejected: $name ($expected)"
}
$final = Join-Path $OutputRoot 'final-baseline.log'
& dotnet run --project $project -c Release *> $final
if ($LASTEXITCODE -ne 0) { throw "Production failed after mutations: $final" }
Get-Content -LiteralPath $final
Write-Host "TACTICAL_CORPSE_FAULTS_PASSED: $($faults.Count) compiled faults; $OutputRoot"
