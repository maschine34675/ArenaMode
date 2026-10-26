using ArenaMode.Modes;
using ArenaMode.Session;

int checks = 0;
void Need(bool pass, string reason) { if (!pass) throw new Exception(reason); checks++; }
string Text(string mode, bool team = false, int rounds = 0, int kills = 0, int seconds = 0,
    bool ffa = true, int teamKills = 50, int teamSeconds = 600, int wins = 3, bool ff = true, int points = 0)
    => ArenaCompactModeRules.Describe(mode, team, rounds, kills, seconds, ffa,
        teamKills, teamSeconds, wins, ff, points);

// The menu's no-limit and first-limit statements are checked against the real
// gameplay state machines, not against a second hard-coded policy implementation.
var continuous = new DeathmatchRules(new DeathmatchOptions(0, 0, 0, true, 3, true), "you", "You");
continuous.StartRound(0);
continuous.RecordKill(1, "you", "You", "enemy");
continuous.Tick(100000);
Need(continuous.Phase == DeathmatchPhase.Live && continuous.RoundOutcome == DeathmatchOutcome.None
    && Text("deathmatch").Contains("no round winner"), "continuous DM has no winner and is labelled honestly");
foreach (int rounds in new[] { 0, 1 })
{
    var limited = new DeathmatchRules(new DeathmatchOptions(rounds, 1, 30, true, 3, true), "you", "You");
    limited.StartRound(0);
    limited.RecordKill(1, "you", "You", "enemy");
    Need(limited.Phase == (rounds == 0 ? DeathmatchPhase.Intermission : DeathmatchPhase.Finished)
        && Text("deathmatch", rounds: rounds, kills: 1, seconds: 30)
            .Contains(rounds == 0 ? "ENDLESS ROUNDS" : "1 ROUND"),
        "finite and unlimited round series follow the actual DM lifecycle");
}
Need(Text("deathmatch", ffa: false).Contains("enemies ignore each other")
    && ArenaCompactModeRules.DeathmatchObjective(10, 60, false) == "Reach kill target"
    && ArenaCompactModeRules.DeathmatchObjective(0, 60, false) == "Survive timer",
    "all-vs-player copy separates target and survival outcomes");
var survival = new DeathmatchRules(new DeathmatchOptions(1, 0, 5, false, 3, true), "you", "You");
survival.StartRound(0); survival.Tick(5);
Need(survival.Outcome == DeathmatchOutcome.Complete, "timer-only all-vs-player is completion, not a fabricated win");

foreach (var (kills, seconds) in new[] { (0, 0), (1, 0), (0, 5), (1, 5) })
{
    var match = new TeamDeathmatchMatchPolicy(kills, seconds);
    match.Update(0, true, 0, 0); match.Update(3, true, 0, 0);
    match.Update(10, true, 1, 0);
    bool endless = match.Phase == TdmMatchPhase.Live;
    Need(Text("teamdeathmatch", teamKills: kills, teamSeconds: seconds).Contains("ENDLESS") == endless,
        "TDM end text follows active kill and time limits independently");
}
Need(Text("teamdeathmatch", ff: true).EndsWith("ON") && Text("teamdeathmatch", ff: false).EndsWith("OFF"),
    "friendly-fire summary reads the current preference");
Need(Text("gungame").Contains("ENDLESS") && Text("gungame").Contains("assigned weapon"),
    "Gun Game explicitly assigns weapons and repeats endlessly");
Need(Text("tactical").Contains("death / timeout loses") && Text("tactical").Contains("ARENA CASH")
    && Text("tactical").Contains("keep surviving kit"), "solo Tactical explains loss and private buying without promising a finite match");
Need(Text("tactical", team: true, wins: 7).Contains("FIRST TO 7 WINS")
    && Text("tactical", team: true).Contains("spectate") && !Text("tactical", team: true).Contains("ARENA CASH"),
    "PFT Tactical explains its distinct match and death behavior");
Need(Text("zombie").Contains("ONE LIFE") && Text("zombie").Contains("death ends match")
    && !Text("zombie").Contains("fallen allies"), "solo Zombies clearly has one terminal life");
Need(Text("zombie", team: true).Contains("TEAM WIPE ENDS MATCH")
    && Text("zombie", team: true).Contains("fallen allies return")
    && Text("zombie", team: true).Contains("WAVE CLEAR"), "PFT Zombies never promises first-death termination");
foreach (string map in new[] { "factory4_day", "factory4_night", "arena_equator", "arena_bay5", "arena_airpit", "arena_bowl" })
{
    int actualPoints = DominationMapLayout.Resolve(map).Points.Count;
    Need(Text("domination", points: actualPoints).Contains("HOLD " + actualPoints + " POINT"),
        "Domination copy uses the actual map objective count: " + map);
}
Need(!Text("domination").Contains("HOLD 3") && Text("domination").Contains("select a map"),
    "unselected Domination map does not invent an objective count");
Need(ArenaCompactModeRules.CompanionHint("tactical", false, 0).Contains("PITFIRETEAM / MY SQUAD")
    && ArenaCompactModeRules.CompanionHint("zombie", true, 1) == "PITFIRETEAM · 1 FROM MY SQUAD",
    "companion source is explicit without a PFT abbreviation");

// Bound every active variant and supported numerical extreme to the two-line
// slot. A deliberately overlong positive fixture proves the oracle can fail.
bool Fits(string text) => text.Split('\n').Length == 2
    && text.Split('\n').All(line => line.Length is > 0 and <= ArenaCompactModeRules.MaxLineCharacters);
Need(!Fits(new string('x', ArenaCompactModeRules.MaxLineCharacters + 1) + "\nshort"),
    "text-budget oracle rejects an overlong line");
foreach (var mode in ArenaModeCatalog.All)
foreach (bool team in new[] { false, true })
foreach (int rounds in new[] { 0, 1, 100 })
foreach (int kills in new[] { 0, 1000 })
foreach (int seconds in new[] { 0, 7200 })
foreach (bool ffa in new[] { false, true })
    Need(Fits(Text(mode.Id, team, rounds, kills, seconds, ffa, kills, seconds, 9, false, 3)),
        "every rendered variant fits two short status lines: " + mode.Id);
Console.WriteLine($"COMPACT_MODE_UX_CHECKS_PASSED {checks} assertions");

namespace UnityEngine { public readonly record struct Vector3(float x, float y, float z); }
