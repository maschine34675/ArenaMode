using ArenaMode.Combat;
using ArenaMode.Hud;

var assertions = 0;
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
    var formatted = KillfeedText.Format(kill);
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
    var unknown = KillfeedText.Format(kill);
    Check(unknown.Contains("? m") && !unknown.Contains("42 m"), scenario + " labels unconfirmed distance as unknown");
    kill.DistanceKnown = true;
    kill.Distance = 0f;
    Check(KillfeedText.Format(kill).Contains("0 m"), scenario + " retains a genuine zero distance");
    kill.Distance = 42.6f;
    Check(KillfeedText.Format(kill).Contains("43 m"), scenario + " rounds measured meters");
    foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
    {
        kill.Distance = invalid;
        Check(KillfeedText.Format(kill).Contains("? m"), scenario + " rejects invalid distance " + invalid);
    }
}

var escaped = KillfeedText.Format(new ArenaKill
{
    KillerName = "<size=99>Attacker", VictimName = "Victim</color>",
    KillerRoleLabel = "<b>Role", WeaponName = "<sprite=2>",
});
Check(!escaped.Contains("<size=99>") && escaped.Contains("‹size=99›Attacker"), "attacker cannot inject TMP markup");
Check(!escaped.Contains("Victim</color></color>") && escaped.Contains("Victim‹/color›"), "victim stays literal");
Check(!escaped.Contains("<sprite=2>") && !escaped.Contains("<b>Role"), "weapon and roles stay literal");
Check(!KillfeedText.Format(new ArenaKill()).Contains(" ·  · "), "missing optional metadata leaves no empty separators");

Console.WriteLine($"KILLFEED_TEXT_CHECKS_PASSED ({assertions} assertions; formatter only, no TMP rendering)");
