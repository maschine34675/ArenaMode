using ArenaMode.Modes;
using ArenaMode.Session;

int checks = 0;
void Need(bool pass, string reason) { if (!pass) throw new Exception(reason); checks++; }
string Text(string mode, bool team = false, int rounds = 0, int kills = 0, int seconds = 0,
    bool ffa = true, int teamKills = 50, int teamSeconds = 600, int wins = 3, bool ff = true, int points = 0, bool objective = false)
    => ArenaCompactModeRules.Describe(mode, team, rounds, kills, seconds, ffa,
        teamKills, teamSeconds, wins, ff, points, tacticalObjective: objective);

// The menu's no-limit and first-limit statements are checked against the real
// gameplay state machines, not against a second hard-coded policy implementation.
var continuous = new DeathmatchRules(new DeathmatchOptions(0, 0, 0, true, 3, true), "you", "You");
continuous.StartRound(0);
continuous.RecordKill(1, "you", "You", "enemy");
continuous.Tick(100000);
Need(continuous.Phase == DeathmatchPhase.Live && continuous.RoundOutcome == DeathmatchOutcome.None
    && Text("deathmatch").Contains("No round winner"), "continuous DM has no winner and is labelled honestly");
foreach (int rounds in new[] { 0, 1 })
{
    var limited = new DeathmatchRules(new DeathmatchOptions(rounds, 1, 30, true, 3, true), "you", "You");
    limited.StartRound(0);
    limited.RecordKill(1, "you", "You", "enemy");
    Need(limited.Phase == (rounds == 0 ? DeathmatchPhase.Intermission : DeathmatchPhase.Finished)
        && Text("deathmatch", rounds: rounds, kills: 1, seconds: 30)
            .Contains("Most kills wins the round."),
        "finite and unlimited round series follow the actual DM lifecycle");
}
Need(Text("deathmatch", ffa: false).Contains("Enemies only target you.")
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
    Need(Text("teamdeathmatch", teamKills: kills, teamSeconds: seconds).Contains("No match end without limits.") == endless,
        "TDM end text follows active kill and time limits independently");
}
Need(Text("teamdeathmatch", ff: true) == Text("teamdeathmatch", ff: false),
    "friendly-fire state belongs to its control, not the mode description");
Need(Text("gungame").Contains("Kills advance your weapon") && Text("gungame").Contains("ladder repeats"),
    "Gun Game explains weapon progression and its repeating ladder");
Need(Text("tactical").Contains("death / timeout loses") && Text("tactical").Contains("Eliminate the enemy squad"),
    "solo Tactical explains elimination and loss without promising a finite match");
Need(Text("tactical", team: true, wins: 7).Contains("First to 7 round wins")
    && Text("tactical", team: true).Contains("spectate until next round"),
    "PFT Tactical explains its distinct match and death behavior");
Need(Text("tactical", points: 1, objective: true).Contains("Eliminate the squad or hold the point")
    && Text("tactical", team: true, wins: 5, points: 2, objective: true).Contains("First to 5 round wins; hold the point")
    && !Text("tactical", objective: true).Contains("hold the point") && !Text("tactical", points: 3).Contains("hold the point"),
    "Tactical copy promises the point only when it is on and the map has one");
Need(Text("zombie").Contains("Survive escalating waves") && Text("zombie").Contains("Gear refills"),
    "solo Zombies explains waves and resupply; the respawn row owns its life rule");
Need(Text("zombie", team: true).Contains("Team wipe ends the match")
    && Text("zombie", team: true).Contains("each cleared wave"), "PFT Zombies never promises first-death termination");
foreach (string map in new[] { "factory4_day", "factory4_night", "arena_equator", "arena_bay5", "arena_airpit", "arena_bowl", "arena_iceberg", "arena_autoservice" })
{
    int actualPoints = DominationMapLayout.Resolve(map).Points.Count;
    Need(Text("domination", points: actualPoints).Contains("Hold " + actualPoints + " point"),
        "Domination copy uses the actual map objective count: " + map);
}
// The menu's objective count per map through the production helper. Expected values
// are literals, not computed by production; maps without a Domination layout and null
// report 0 instead of reaching Resolve's exception.
foreach (var (map, expected) in new (string, int)[] {
    ("factory4_day", 3), ("factory4_night", 3), ("arena_equator", 1), ("arena_bay5", 1),
    ("arena_airpit", 2), ("arena_bowl", 2), ("arena_iceberg", 3), ("arena_autoservice", 1),
    ("bigmap", 0), ("laboratory", 0), ("labyrinth", 0), ("unknown", 0), (null, 0) })
{
    int count = DominationMapLayout.ControlPointCount(map);
    Need(count == expected, $"menu objective count for {map ?? "null"}: expected {expected}, got {count}");
}
// Domination's own count by (map, area): The Lab has three points, The Labyrinth exactly one, each of the three
// Customs areas three, and the other maps answer as before. Tactical keeps ControlPointCount above, so its copy
// never promises a point on The Lab, The Labyrinth or Customs. Literals again, not computed by production.
foreach (var (map, area, expected) in new (string, string, int)[] {
    ("laboratory", null, 3), ("labyrinth", null, 1), ("LABORATORY", "full-map", 3), ("labyrinth", "", 1),
    ("factory4_day", null, 3), ("arena_equator", null, 1), ("arena_airpit", null, 2), ("arena_autoservice", null, 1),
    ("bigmap", null, 0), ("bigmap", "full-map", 0), ("bigmap", "customs-yard-polygon-v1", 3),
    ("bigmap", "customs-factory-center-polygon-v2", 3), ("BIGMAP", "customs-factory-side-polygon-v2", 3),
    ("bigmap", "customs-factory-center-polygon-v1", 0), ("bigmap", "CUSTOMS-YARD-POLYGON-V1", 0), ("bigmap", "unknown-area", 0),
    ("laboratory", "customs-yard-polygon-v1", 0), ("factory4_day", "customs-yard-polygon-v1", 0),
    ("unknown", null, 0), (null, null, 0), (null, "customs-yard-polygon-v1", 0) })
{
    int count = DominationMapLayout.DominationPointCount(map, area);
    Need(count == expected, $"Domination objective count for {map ?? "null"}/{area ?? "whole map"}: expected {expected}, got {count}");
}
Need(Text("domination", points: DominationMapLayout.DominationPointCount("labyrinth", null)).Contains("Hold 1 point to"),
    "The Labyrinth's Domination copy says one point");
Need(Text("domination", points: DominationMapLayout.DominationPointCount("laboratory", null)).Contains("Hold 3 points to"),
    "The Lab's Domination copy says three points");
Need(Text("domination", points: DominationMapLayout.DominationPointCount("bigmap", "customs-yard-polygon-v1")).Contains("Hold 3 points to"),
    "a Customs area's Domination copy says three points");
foreach (string map in new[] { "laboratory", "labyrinth", "bigmap" })
    Need(!Text("tactical", points: DominationMapLayout.ControlPointCount(map), objective: true).Contains("hold the point"),
        "Tactical's copy promises no point on The Lab, The Labyrinth or Customs: " + map);
Need(!Text("domination").Contains("Hold 3") && Text("domination").Contains("Hold points to earn team score"),
    "unselected Domination map does not invent an objective count");
foreach(var (points,seconds) in new[]{(0,0),(100,0),(0,300),(500,1200)})
{
    string description=ArenaCompactModeRules.Describe("domination",false,0,0,0,true,50,600,3,true,3,points,seconds);
    Need(description == Text("domination", points: 3),"Domination purpose does not duplicate the selected limit controls");
}
Need(ArenaCompactModeRules.CompanionHint("tactical", false, 0) == "GAME MODE"
    && ArenaCompactModeRules.CompanionHint("zombie", true, 1) == "GAME MODE / PITFIRETEAM",
    "companion heading appears only for an actual squad");

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
