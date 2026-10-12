using ArenaMode.Modes;
using ArenaMode.Session;
int checks = 0;
void Check(bool b, string why) { if (!b) throw new Exception(why); checks++; }
Check(ArenaModeCatalog.All.Count == 5 && ArenaModeCatalog.All[0].Id == "gungame"
    && ArenaModeCatalog.All.Count(m => m.Id == "deathmatch") == 1, "Gun Game first, one Deathmatch entry replaces both legacy entries");
foreach (var alias in new[] { "endless", "rounds", " ROUNDS ", " ENDLESS " }) {
 string saved = alias;
 var mode = new ArenaModeSelectionState(saved, false, x => { saved = x; return true; });
 mode.CommitInitialSelection();
 Check(mode.SelectedMode == "deathmatch" && saved == "deathmatch", "legacy alias is persisted to canonical mode");
}
int[] values = { 0, 0, 0, 0 }; bool locked = false, accept = true; int writes = 0;
var state = new ArenaDeathmatchRulesState(row => values[row], (row,value) => { writes++; if (!accept) return false; values[row] = value; return true; }, () => locked);
state.Synchronize("deathmatch", false);
Check(state.Visible && state.Editable, "DM rules visible and editable before raid");
Check(!state.Select(0,3) && writes == 0, "finite rounds cannot be stranded with no ending condition");
Check(state.Select(1,20) && state.Select(0,3), "enable kill limit then finite match");
Check(!state.Select(1,0), "cannot remove last limit from finite match");
Check(state.Select(2,120) && state.Select(1,0), "time-only finite rounds supported");
Check(state.Select(3,1) && values[3] == 1, "all vs player persists same rule");
locked = true;
int before = writes;
for (int r = 0; r < 4; r++) foreach (int v in ArenaDeathmatchRulesState.Choices[r]) Check(!state.Select(r,v), "all controls freeze during arm and raid");
Check(writes == before, "locked writes never reach settings");
locked = false; accept = false;
Check(!state.Select(1,50) && values[1] == 0, "rejected write cannot move selection marker");
foreach (var other in new[] { "tactical", "gungame", "teamdeathmatch", "zombie" }) {
 state.Synchronize(other, false); Check(!state.Visible && !state.Select(1,20), "other modes cannot edit Deathmatch controls");
}
state.Synchronize("deathmatch", true);
Check(!state.Visible && !state.Select(3,0), "PFT restricts mode to Tactical");
Console.WriteLine($"DEATHMATCH_MENU_STATE_PASSED: {checks} assertions");
