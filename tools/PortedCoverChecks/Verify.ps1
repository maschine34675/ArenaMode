param([string]$OutputRoot = '')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$OutputRoot) { $OutputRoot = Join-Path $repo ('artifacts/ported-cover-checks/' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$project = Join-Path $PSScriptRoot 'PortedCoverChecks.csproj'
$source = Get-Content (Join-Path $repo 'ArenaMode.Client/Compat/PitFireTeamPortedMapCombat.cs') -Raw
$faults = @(
    @('ordinary','!ArenaSession.IsActive ||','false ||','SCOPE-COVER ordinary'),
    @('unauthenticated','!PitFireTeamCompat.TeamArenaActive ||','false ||','SCOPE-COVER solo'),
    @('finalizing','|| PitFireTeamCompat.IsFinalizing','|| false','SCOPE-COVER finalizing'),
    @('unsupported-mode','(ArenaSession.ActiveMode != "tactical" && ArenaSession.ActiveMode != "zombie")','false','SCOPE-COVER mode deathmatch'),
    @('loading','game.Status != GameStatus.Started','false','SCOPE-COVER status Running'),
    @('wrong-raid','!ArenaSession.IsExactActiveRaid(game, world)','false','SCOPE-COVER wrong exact raid'),
    @('ordinary-map','(!string.Equals(world.LocationId, "arena_bay5"','(false && !string.Equals(world.LocationId, "arena_bay5"','SCOPE-COVER map bigmap'),
    @('wrong-world','!ReferenceEquals(game.GameWorld, world)','false','SCOPE-COVER game-world mismatch'),
    @('foreign-controller','ReferenceEquals(bot.BotsController, controller)','true','SCOPE-COVER foreign controller'),
    @('dead-body','player.HealthController?.IsAlive == true','true','SCOPE-COVER dead'),
    @('foreign-player-world','ReferenceEquals(player.GameWorld, world)','true','SCOPE-COVER foreign player world'),
    @('replaced-owner','ReferenceEquals(player.AIData?.BotOwner, bot)','true','SCOPE-COVER replaced owner'),
    @('replaced-body','ReferenceEquals(world.GetAlivePlayerByProfileID(player.ProfileId), player)','true','SCOPE-COVER same id wrong physical body'),
    @('native-covers','controller.CoversData.Points.Count == 0','controller.CoversData.Points.Count >= 0','SCOPE-COVER real covers present'),
    @('unowned-cover','!PitFireTeamCompat.IsTeamMember(search._owner)','false','COMPANION-ONLY-COVER'),
    @('foreign-master','!ReferenceEquals(search._owner.BotsGroup?.CoverPointMaster, __instance)','false','FOREIGN-COVER-MASTER'),
    @('foreign-search','!ReferenceEquals(search._owner.CoverSearchInfo, search)','false','FOREIGN-COVER-SEARCH'),
    @('foreign-finder-covers','!ReferenceEquals(__instance._covers, __instance._owner.BotsController.CoversData)','false','FOREIGN-FINDER-COVERS'),
    @('graph-fallback','onlyShortTrie = true;','onlyShortTrie = false;','DIRECT-NAV-ONLY'),
    @('cover-teleport','mustHaveWay = false;','mustHaveWay = true;','DIRECT-NAV-ONLY'),
    @('fabricated-cover','__result = null;','__result = new CustomNavigationPoint();','EMPTY-COVER'),
    @('run-unsafe-cover','return false;','return true;','EMPTY-COVER'),
    @('earlier-cover-owner','!__runOriginal || data?.Bot','false || data?.Bot','EARLIER-COVER-OWNER'),
    @('earlier-path-owner','!__runOriginal || __instance == null','false || __instance == null','EARLIER-PATH-OWNER'),
    @('prefix-order','priority = Priority.Last','priority = 400','HARMONY-PRIORITY')
)
$baselineLog = Join-Path $OutputRoot 'baseline.log'
& dotnet run --project $project -c Release *> $baselineLog
if ($LASTEXITCODE -ne 0) { throw "Unmodified production failed: $baselineLog" }
foreach ($fault in $faults) {
    $name,$before,$after,$expected = $fault
    if (!$source.Contains($before)) { throw "Fault anchor missing: $name" }
    $file = Join-Path $OutputRoot ($name + '.cs')
    [IO.File]::WriteAllText($file,$source.Replace($before,$after))
    $log = Join-Path $OutputRoot ($name + '.log')
    $ErrorActionPreference = 'Continue'
    & dotnet run --project $project -c Release "-p:CombatSource=$file" *> $log
    $exit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($exit -eq 0) { throw "Fault survived: $name" }
    if (Select-String -LiteralPath $log -Pattern ': error (CS|MSB)' -Quiet) { throw "Fault did not compile: $name" }
    if (!(Select-String -LiteralPath $log -SimpleMatch $expected -Quiet)) { throw "Wrong failure for $name; expected $expected, inspect $log" }
    Write-Host "rejected: $name ($expected)"
}
$finalLog = Join-Path $OutputRoot 'final-baseline.log'
& dotnet run --project $project -c Release *> $finalLog
if ($LASTEXITCODE -ne 0) { throw "Production failed after compiled mutations: $finalLog" }
Get-Content -LiteralPath $finalLog
Write-Host "PORTED_COVER_FAULTS_PASSED: $($faults.Count) compiled faults; $OutputRoot"
