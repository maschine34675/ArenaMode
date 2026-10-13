param([string]$OutputRoot = '')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$OutputRoot) { $OutputRoot = Join-Path $repo ('artifacts/friendly-fire-checks/' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$project = Join-Path $PSScriptRoot 'FriendlyFireRuntimeChecks.csproj'
$sources = @{
    PolicySource = Get-Content (Join-Path $repo 'ArenaMode.Client/Combat/ArenaFriendlyFire.cs') -Raw
    RosterSource = Get-Content (Join-Path $repo 'ArenaMode.Client/Combat/ArenaFriendlyFireRoster.cs') -Raw
    ExplosionSource = Get-Content (Join-Path $repo 'ArenaMode.Client/Combat/ArenaFriendlyExplosionEffects.cs') -Raw
    TacticalSource = Get-Content (Join-Path $repo 'ArenaMode.Client/Modes/TacticalMode.FriendlyFire.cs') -Raw
    ZombieSource = Get-Content (Join-Path $repo 'ArenaMode.Client/Modes/ZombieTeamRuntime.FriendlyFire.cs') -Raw
}
$faults = @(
    @('four-only-squad', 'RosterSource', 'expectedCompanionCount is < 1 or > 4', 'expectedCompanionCount != 4', 'tactical mode admits 1 companions'),
    @('squad-count-drift', 'RosterSource', '(_expectedCompanionCount != 0 && expectedCompanionCount != _expectedCompanionCount)', 'false', 'variable ledger frozen count cannot drift'),
    @('missing-squad-admitted', 'RosterSource', @('followers.Length != expectedCompanionCount', 'followerIds.Length != expectedCompanionCount'), @('false', 'false'), 'admission rejects partial followers'),
    @('invalid-frozen-count', 'RosterSource', 'expectedCompanionCount is < 1 or > 4', 'false', 'physical ledger rejects invalid frozen count 0'),
    @('ignore-frozen-switch', 'PolicySource', 'ArenaSession.FriendlyFireEnabledSelection', 'false', 'tactical ON preserves native'),
    @('ordinary-raid-and-session-scope-lost', 'PolicySource', @('!ArenaSession.IsActive', '!ArenaSession.IsExactActiveRaid(game, world)'), @('false', 'false'), 'tdm ordinary inactive'),
    @('stopping-raid-leak', 'PolicySource', 'game.Status != GameStatus.Started', 'false', 'tdm stopping'),
    @('wrong-active-raid', 'PolicySource', '!ArenaSession.IsExactActiveRaid(game, world)', 'false', 'tdm other game'),
    @('dead-victim-immunity', 'PolicySource', 'victim.HealthController?.IsAlive != true', 'false', 'tdm dead victim'),
    @('unregistered-victim-immunity', 'PolicySource', '!ReferenceEquals(world.GetAlivePlayerByProfileID(victim.ProfileId), victim)', 'false', 'tdm unregistered victim'),
    @('overwritten-victim-immunity', 'PolicySource', '!ReferenceEquals(world.GetEverExistedPlayerByID(victim.ProfileId), victim)', 'false', 'tdm overwritten victim'),
    @('friendly-enemies-immune', 'RosterSource', 'origin.Team != target.Team', 'false', 'tactical enemy'),
    @('old-round-victim-immunity', 'RosterSource', 'target.Generation != _generation', 'false', 'tactical old still registered victim generation'),
    @('own-follower-grenade-suppressed', 'RosterSource', '&& origin.Participant != target.Participant', '', 'tactical own follower replacement keeps self damage'),
    @('mutated-source-authority', 'RosterSource', 'source != null && source.ProfileId != origin.ProfileId', 'false', 'tactical mutated live source'),
    @('destroyed-thrower-lost', 'RosterSource', 'ReferenceEquals(source, null)', 'source == null', 'tactical cleaned source frozen bridge'),
    @('discard-historical-source', 'RosterSource', 'foreach (var item in pending)', '_bodies = new ConditionalWeakTable<Player, Entry>(); foreach (var item in pending)', 'tactical past round grenade to another participant'),
    @('duplicate-follower-accounts', 'RosterSource', '!accounts.Add(owner.Profile.AccountId)', 'false', 'admission rejects duplicate accounts'),
    @('preactive-admission', 'RosterSource', 'owner.BotState != EBotState.Active', 'false', 'admission rejects nonactive follower'),
    @('owner-body-mismatch', 'RosterSource', '!ReferenceEquals(owner.GetPlayer?.AIData?.BotOwner, owner)', 'false', 'admission rejects owner body mismatch'),
    @('unregistered-admission', 'RosterSource', '!ReferenceEquals(world.GetAlivePlayerByProfileID(id), body)', 'false', 'admission rejects unregistered admission'),
    @('wrong-main-player-admission', 'RosterSource', '!ReferenceEquals(world.MainPlayer, leader)', 'false', 'admission rejects wrong leader'),
    @('foreign-match-ledger', 'RosterSource', '!ReferenceEquals(_match, match)', 'false', 'direct ledger foreign match'),
    @('foreign-world-ledger', 'RosterSource', '!ReferenceEquals(_world, world)', 'false', 'direct ledger foreign world'),
    @('owner-bridge-lost', 'RosterSource', '(attacker as BotOwner)?.GetPlayer', '(Player)null', 'exact native owner bridge'),
    @('solo-tactical-leak', 'TacticalSource', '_teamArena &&', 'true &&', 'solo Tactical excluded'),
    @('zombie-live-scope-disabled', 'ZombieSource', 'IsLive &&', 'true &&', 'zombie another match object'),
    @('nested-unknown-inherits-source', 'ExplosionSource', 'return new Scope(source);', 'return source == null ? null : new Scope(source);', 'nested unknown shadows previous source'),
    @('lose-outer-explosion', 'ExplosionSource', '_current = _previous;', '_current = null;', 'outer context restored'),
    @('missing-exception-finalizer', 'ExplosionSource', '__state?.Dispose();', '', 'exception finalizer clears scope'),
    @('reentrant-flash-inherits-grenade', 'ExplosionSource', '!ReferenceEquals(caches, ExplosionSharedMethods._playerBuffer)', 'false', 'reentrant non-explosion flash source not inherited'),
    @('foreign-blast-inherits-grenade', 'ExplosionSource', '!ReferenceEquals(original, ExplosionSharedMethods._playerBuffer)', 'false', 'foreign blast arguments not attributed to grenade'),
    @('mutate-native-flash-cache', 'ExplosionSource', 'var copy = new Dictionary<IObserverToPlayerBridge, PlayerEmitterCache>(caches);', 'var copy = caches;', 'flash shared native buffer unmodified'),
    @('ignore-suppressed-blast', 'ExplosionSource', 'if (__runOriginal) playerAndDistances', 'if (true) playerAndDistances', 'suppressed blast prefix leaves arguments'),
    @('ignore-suppressed-flash', 'ExplosionSource', 'if (__runOriginal) FilterFlash', 'if (true) FilterFlash', 'suppressed flash prefix leaves arguments'),
    @('effect-decision-logs-damage', 'PolicySource', 'blocked && reportDamage &&', 'blocked && true &&', 'effect predicate is not damage-hook evidence'),
    @('bot-hit-consumes-local-diagnostic', 'PolicySource', 'ReferenceEquals(attacker, world.MainPlayer)', 'true', 'bot damage does not consume local diagnostic'),
    @('diagnostic-spam', 'PolicySource', 'if (!report.Emitted)', 'if (true)', 'one diagnostic per match')
)
$baseline = Join-Path $OutputRoot 'baseline.log'
& dotnet run --project $project -c Release *> $baseline
if ($LASTEXITCODE -ne 0) { throw "Production failed: $baseline" }
foreach ($fault in $faults) {
    $name,$property,$before,$after,$expected = $fault
    $source = $sources[$property]
    $anchors = @($before); $replacements = @($after)
    for ($index=0; $index -lt $anchors.Count; $index++) {
        if (!$source.Contains($anchors[$index])) { throw "Missing anchor: $name" }
        $source = $source.Replace($anchors[$index], $replacements[$index])
    }
    $file = Join-Path $OutputRoot ($name + '.cs')
    [IO.File]::WriteAllText($file, $source)
    $log = Join-Path $OutputRoot ($name + '.log')
    $ErrorActionPreference = 'Continue'
    & dotnet run --project $project -c Release "-p:${property}=$file" *> $log
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($code -eq 0) { throw "Fault survived: $name" }
    if (Select-String -LiteralPath $log -Pattern ': error (CS|MSB)' -Quiet) { throw "Fault did not compile: $name" }
    if (!(Select-String -LiteralPath $log -SimpleMatch "FAILED: $expected" -Quiet)) { throw "Wrong failure: $name expected $expected, inspect $log" }
    Write-Host "rejected: $name ($expected)"
}
$final = Join-Path $OutputRoot 'final-baseline.log'
& dotnet run --project $project -c Release *> $final
if ($LASTEXITCODE -ne 0) { throw "Production failed after mutations: $final" }
Get-Content -LiteralPath $final
Write-Host "FRIENDLY_FIRE_MUTATIONS_PASSED: $($faults.Count) compiled faults; $OutputRoot"
