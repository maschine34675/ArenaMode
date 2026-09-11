using ArenaMode.Combat;
using ArenaMode.Effects;
using ArenaMode.Hud;

var assertions = 0;
const string friendlyColor = "#209FFF";
const string opponentColor = "#FF1E1E";
const string unknownColor = "#D5DAE0";
string Format(ArenaKill kill) => KillfeedText.Format(kill, friendlyColor, opponentColor);
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("CHECK FAILED: " + message);
    assertions++;
}

foreach (var scenario in new[] { "local-kill", "local-death", "other-kill" })
{
    var kill = new ArenaKill
    {
        KillerName = "Attacker_LongNickname_1234567890",
        VictimName = "Victim_LongNickname_0987654321",
        KillerRoleLabel = "USEC", VictimRoleLabel = "BEAR", VictimRole = ArenaRole.Pmc,
        KilledByLocal = scenario == "local-kill", LocalIsVictim = scenario == "local-death",
        WeaponName = "HK 416A5", Headshot = true, Distance = 42f, DistanceKnown = true,
    };
    var formatted = Format(kill);
    var split = formatted.IndexOf('\n');
    Check(split > 0, scenario + " separates identities from metadata");
    var headline = formatted[..split];
    var details = formatted[(split + 1)..];
    Check(headline.Contains(kill.KillerName), scenario + " retains the entire attacker name in headline");
    Check(headline.Contains(kill.VictimName), scenario + " retains the entire victim name in headline");
    Check(headline.IndexOf(kill.KillerName, StringComparison.Ordinal) < headline.IndexOf(kill.VictimName, StringComparison.Ordinal),
        scenario + " keeps attacker before victim");
    Check(!headline.Contains("HK 416A5"), scenario + " leaves weapon metadata below identities");
    Check(details.Contains("USEC") && details.Contains("BEAR"), scenario + " retains both roles");
    Check(details.Contains("HK 416A5") && details.Contains("HS"), scenario + " retains weapon and headshot");
    Check(details.Contains("42 m"), scenario + " includes measured distance");
    Check(!formatted.Contains("…") && !formatted.Contains("..."), scenario + " does not shorten names");
}

foreach (var scenario in new[] { "local-kill", "local-death", "other-kill" })
{
    var kill = new ArenaKill
    {
        KilledByLocal = scenario == "local-kill", LocalIsVictim = scenario == "local-death",
        Distance = 42f, DistanceKnown = false,
    };
    var unknown = Format(kill);
    Check(unknown.Contains("? m") && !unknown.Contains("42 m"), scenario + " labels unconfirmed distance as unknown");
    kill.DistanceKnown = true;
    kill.Distance = 0f;
    Check(Format(kill).Contains("0 m"), scenario + " retains a genuine zero distance");
    kill.Distance = 42.6f;
    Check(Format(kill).Contains("43 m"), scenario + " rounds measured meters");
    foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
    {
        kill.Distance = invalid;
        Check(Format(kill).Contains("? m"), scenario + " rejects invalid distance " + invalid);
    }
}

var escaped = Format(new ArenaKill
{
    KillerName = "<size=99>Attacker", VictimName = "Victim</color>",
    KillerRoleLabel = "<b>Role", WeaponName = "<sprite=2>",
});
Check(!escaped.Contains("<size=99>") && escaped.Contains("‹size=99›Attacker"), "attacker cannot inject TMP markup");
Check(!escaped.Contains("Victim</color></color>") && escaped.Contains("Victim‹/color›"), "victim stays literal");
Check(!escaped.Contains("<sprite=2>") && !escaped.Contains("<b>Role"), "weapon and roles stay literal");
Check(!Format(new ArenaKill()).Contains(" ·  · "), "missing optional metadata leaves no empty separators");

void CheckTeams(string scenario, string killer, string victim, string expectedKiller, string expectedVictim,
    bool localKill = false, bool localDeath = false, ArenaRole role = ArenaRole.Pmc)
{
    var kill = new ArenaKill
    {
        KillerProfileId = killer, VictimProfileId = victim,
        KillerName = "Attacker", VictimName = "Victim",
        KillerRoleLabel = "USEC", VictimRoleLabel = "BEAR", VictimRole = role,
        KilledByLocal = localKill, LocalIsVictim = localDeath,
        // Scoring metadata deliberately conflicts with some cases: presentation
        // must use the roster without rewriting these score/award inputs.
        KillerIsFriendly = true, VictimIsFriendly = false,
        ScoreEligible = false, AwardEligible = true,
    };
    var headline = Format(kill).Split('\n')[0];
    Check(headline == $"<color={expectedKiller}>Attacker</color> ➔ <color={expectedVictim}>Victim</color>",
        scenario + " colors both identities by team");
    Check(kill.KillerIsFriendly == true && kill.VictimIsFriendly == false && !kill.ScoreEligible && kill.AwardEligible,
        scenario + " does not change scoring metadata");
}

CheckTeams("unbound", "local", "enemy", unknownColor, unknownColor, localKill: true);
Check(ArenaTracerTeams.BeginMatch("local", false), "solo roster binds");
CheckTeams("solo-local-kill", "local", "enemy", friendlyColor, opponentColor, localKill: true);
CheckTeams("solo-local-death", "enemy", "local", opponentColor, friendlyColor, localDeath: true);
CheckTeams("solo-other-kill", "enemy1", "enemy2", opponentColor, opponentColor);
CheckTeams("solo-missing-id", null, " ", unknownColor, unknownColor);

Check(ArenaTracerTeams.BeginFactionMatch("local"), "TDM roster binds");
Check(ArenaTracerTeams.RegisterFactionActor("friend", true), "TDM friend admitted");
Check(ArenaTracerTeams.RegisterFactionActor("enemy", false), "TDM enemy admitted");
CheckTeams("tdm-friend-kill", "friend", "enemy", friendlyColor, opponentColor);
CheckTeams("tdm-enemy-kill", "enemy", "friend", opponentColor, friendlyColor);
CheckTeams("tdm-local-death", "enemy", "local", opponentColor, friendlyColor, localDeath: true);
CheckTeams("tdm-friendly-fire", "friend", "local", friendlyColor, friendlyColor, localDeath: true);
CheckTeams("tdm-enemy-pair", "enemy", "enemy", opponentColor, opponentColor);
CheckTeams("tdm-unknown", "unregistered1", "unregistered2", unknownColor, unknownColor);
CheckTeams("tdm-role-not-team", "enemy", "friend", opponentColor, friendlyColor, role: ArenaRole.Boss);
CheckTeams("tdm-flags-not-team", "enemy", "friend", opponentColor, friendlyColor, localKill: true, localDeath: true);

var colorChangeKill = new ArenaKill { KillerProfileId = "friend", VictimProfileId = "enemy", KillerName = "A", VictimName = "V" };
var previousEntry = Format(colorChangeKill);
var customizedEntry = KillfeedText.Format(colorChangeKill, "#00FF00", "#FF00FF");
Check(customizedEntry.StartsWith("<color=#00FF00>A</color> ➔ <color=#FF00FF>V</color>"), "custom colors are respected on new entries");
Check(previousEntry.StartsWith($"<color={friendlyColor}>A</color> ➔ <color={opponentColor}>V</color>"), "formatted entry retains its original colors");

Check(ArenaTracerTeams.BeginMatch("local", true), "PFT roster starts unbound");
CheckTeams("pft-awaiting-roster", "local", "enemy", unknownColor, unknownColor, localKill: true);
var friends = new[] { "friend", "friend2", "friend3", "friend4" };
var enemies = new[] { "enemy", "enemy2", "enemy3", "enemy4", "enemy5" };
Check(ArenaTracerTeams.TryBindTeamRound(1, "local", friends, enemies, out _), "PFT complete roster binds");
CheckTeams("pft-friend-kill", "friend", "enemy", friendlyColor, opponentColor);
CheckTeams("pft-local-death", "enemy", "local", opponentColor, friendlyColor, localDeath: true);
CheckTeams("pft-unknown", "outsider", "friend", unknownColor, friendlyColor);
Check(ArenaTracerTeams.TryBindTeamRound(2, "local",
    new[] { "new-f1", "new-f2", "new-f3", "new-f4" },
    new[] { "new-e1", "new-e2", "new-e3", "new-e4", "new-e5" }, out _), "PFT second roster binds");
CheckTeams("pft-historical-kill", "enemy", "friend", opponentColor, friendlyColor);
CheckTeams("pft-new-round", "new-f1", "new-e1", friendlyColor, opponentColor);
Check(!ArenaTracerTeams.TryBindTeamRound(3, "local", new[] { "too-short" }, enemies, out _), "PFT invalid rebind is refused");
CheckTeams("pft-invalid-rebind", "local", "enemy", unknownColor, unknownColor, localKill: true);
ArenaTracerTeams.Reset();
CheckTeams("after-reset", "local", "friend", unknownColor, unknownColor);

Console.WriteLine($"KILLFEED_TEXT_CHECKS_PASSED ({assertions} assertions; formatter and team registry, no TMP rendering)");
