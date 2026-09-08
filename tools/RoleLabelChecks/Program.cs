using ArenaMode.Combat;
using ArenaMode.Modes;
using EFT;

var assertions = 0;
void Equal<T>(T actual, T expected, string name)
{
    assertions++;
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
        throw new InvalidOperationException($"ASSERT: {name}; expected {expected}, got {actual}");
}

TestPlayer Player(bool ai, EPlayerSide side, WildSpawnType? role = null) => new()
{
    IsAI = ai,
    Profile = new Profile
    {
        Info = new ProfileInfo
        {
            Side = side,
            Nickname = "Test actor",
            Settings = role.HasValue ? new ProfileSettings { Role = role.Value } : new ProfileSettings(),
        },
    },
};

void Check(IPlayer player, ArenaRole bucket, string label, string name)
{
    Equal(RoleClassifier.Label(player), label, name + " label");
    Equal(RoleClassifier.Bucket(player), bucket, name + " bucket");
}

Equal(new ProfileSettings().Role, WildSpawnType.assault, "native human profile bot-role default is assault");
var usec = Player(false, EPlayerSide.Usec);
usec.IsYourPlayer = true;
Check(usec, ArenaRole.Pmc, "USEC", "local USEC with native default role");
var bear = Player(false, EPlayerSide.Bear);
bear.IsYourPlayer = true;
Check(bear, ArenaRole.Pmc, "BEAR", "local BEAR with native default role");
Check(Player(false, EPlayerSide.Usec), ArenaRole.Pmc, "USEC", "non-local human USEC");
Check(Player(false, EPlayerSide.Bear), ArenaRole.Pmc, "BEAR", "non-local human BEAR");
Check(Player(false, EPlayerSide.Usec, WildSpawnType.pmcBEAR), ArenaRole.Pmc, "USEC", "human faction overrides stale bot role");
Check(Player(false, EPlayerSide.Savage), ArenaRole.Scav, "Scav", "human Scav stays Scav");
Check(Player(false, (EPlayerSide)0), ArenaRole.Scav, "Scav", "unknown faction does not imply human PMC");

// AI faction alone cannot distinguish rogues/raiders/bosses from PMC bots.
// Assert the existing label and point bucket across every ordinary faction.
var aiExpectations = new (WildSpawnType Role, ArenaRole Bucket, string Label)[]
{
    (WildSpawnType.assault, ArenaRole.Scav, "Scav"),
    (WildSpawnType.assaultGroup, ArenaRole.Scav, "Scav"),
    (WildSpawnType.marksman, ArenaRole.Scav, "Sniper Scav"),
    (WildSpawnType.cursedAssault, ArenaRole.Scav, "Scav"),
    (WildSpawnType.crazyAssaultEvent, ArenaRole.Scav, "Scav"),
    (WildSpawnType.pmcUSEC, ArenaRole.Pmc, "USEC"),
    (WildSpawnType.pmcBEAR, ArenaRole.Pmc, "BEAR"),
    (WildSpawnType.pmcBot, ArenaRole.Raider, "Raider"),
    (WildSpawnType.exUsec, ArenaRole.Raider, "Rogue"),
    (WildSpawnType.arenaFighter, ArenaRole.Raider, "Arena Fighter"),
    (WildSpawnType.arenaFighterEvent, ArenaRole.Raider, "Arena Fighter"),
    (WildSpawnType.bossKilla, ArenaRole.Boss, "Boss"),
    (WildSpawnType.bossKnight, ArenaRole.Boss, "Boss"),
    (WildSpawnType.followerBigPipe, ArenaRole.Boss, "Boss"),
    (WildSpawnType.followerBirdEye, ArenaRole.Boss, "Boss"),
    (WildSpawnType.followerBully, ArenaRole.Follower, "Guard"),
    (WildSpawnType.sectantWarrior, ArenaRole.Follower, "Cultist"),
    (WildSpawnType.sectantPriest, ArenaRole.Follower, "Cultist Priest"),
    (WildSpawnType.sectactPriestEvent, ArenaRole.Follower, "Cultist Priest"),
    (WildSpawnType.shooterBTR, ArenaRole.Other, "BTR Gunner"),
    (WildSpawnType.gifter, ArenaRole.Other, "gifter"),
    ((WildSpawnType)999, ArenaRole.Other, "999"),
};
foreach (var side in new[] { EPlayerSide.Usec, EPlayerSide.Bear, EPlayerSide.Savage })
foreach (var expected in aiExpectations)
    Check(Player(true, side, expected.Role), expected.Bucket, expected.Label,
        $"AI {side}/{expected.Role} keeps native role");

Check(null, ArenaRole.Other, "?", "missing player");
Check(new TestPlayer(), ArenaRole.Other, "?", "missing profile");
Check(new TestPlayer { Profile = new Profile() }, ArenaRole.Other, "?", "missing profile info");
foreach (var side in new[] { EPlayerSide.Usec, EPlayerSide.Bear })
{
    var human = Player(false, side);
    human.Profile.Info.Settings = null;
    Check(human, ArenaRole.Pmc, side == EPlayerSide.Usec ? "USEC" : "BEAR", "human PMC without bot settings " + side);
}
var incompleteBot = Player(true, EPlayerSide.Usec);
incompleteBot.Profile.Info.Settings = null;
Check(incompleteBot, ArenaRole.Other, "?", "AI missing role does not gain a PMC role");
var incompleteScav = Player(false, EPlayerSide.Savage);
incompleteScav.Profile.Info.Settings = null;
Check(incompleteScav, ArenaRole.Other, "?", "human Scav missing role remains unknown");

// Match-local scoreboard integration: same production label flows through the
// constructor, snapshot sync and body replacement used by ArenaMatch(Service).
var scoreboard = new ArenaScoreboard("local-before", RoleClassifier.DisplayName(usec), RoleClassifier.Label(usec));
Equal(scoreboard.Local.RoleLabel, "USEC", "scoreboard initial local label");
Equal(scoreboard.Local.Side, ArenaScoreboardSide.Friendly, "role label does not change team assignment");
var ally = Player(true, EPlayerSide.Usec, WildSpawnType.pmcUSEC);
var opponent = Player(true, EPlayerSide.Bear, WildSpawnType.pmcBEAR);
ArenaScoreboardActor Actor(string id, IPlayer player) => new(id, RoleClassifier.DisplayName(player), RoleClassifier.Label(player), true);
Equal(scoreboard.TrySynchronizeRoster(1, Actor("local-before", usec),
    new[] { Actor("ally", ally) }, new[] { Actor("enemy", opponent) }, out var problem), true, "scoreboard accepts classified roster " + problem);
Equal(scoreboard.Local.RoleLabel, "USEC", "scoreboard sync retains PMC label");
scoreboard.ReportLocalDeath();
scoreboard.ReportLocalRespawn("local-after", RoleClassifier.DisplayName(usec), RoleClassifier.Label(usec));
Equal(scoreboard.Local.RoleLabel, "USEC", "scoreboard respawn retains PMC label");
Equal(scoreboard.Local.Deaths, 1, "label correction preserves local death count");
Equal(ScoreRules.PointsFor(RoleClassifier.Bucket(usec)), 200, "human PMC resolves PMC score bucket");
Equal(ScoreRules.PointsFor(RoleClassifier.Bucket(Player(true, EPlayerSide.Usec, WildSpawnType.exUsec))), 150, "rogue score remains unchanged");
Equal(ScoreRules.PointsFor(RoleClassifier.Bucket(Player(true, EPlayerSide.Bear, WildSpawnType.bossKnight))), 300, "boss score remains unchanged");
Equal(usec.Profile.Info.Side, EPlayerSide.Usec, "classifier never changes faction");
Equal(usec.Profile.Info.Settings.Role, WildSpawnType.assault, "classifier never rewrites profile bot settings");
Equal(RoleClassifier.DisplayName(usec), "Test actor", "display name remains unchanged");
Equal(RoleClassifier.DisplayName(null), "?", "missing display name remains safe");
Console.WriteLine($"ARENA_ROLE_LABEL_CONTRACTS_PASSED: {assertions} assertions");
