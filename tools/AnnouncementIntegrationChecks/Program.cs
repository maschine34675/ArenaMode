using ArenaMode.Audio;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;

int checks = 0;
void Check(bool yes, string message) { checks++; if (!yes) throw new Exception(message); }
try
{
    var options = new DeathmatchOptions(2, 10, 120, true, 3, false);
    var dm = new RoundsMode { _options = options, _rules = new DeathmatchRules(options, "me", "Local") };
    Check(dm.AnnouncementSnapshot().Preparing, "DM preparation is observable before start");
    dm._rules.StartRound(0); Time.time = 10;
    dm._rules.RecordKill(1, "me", "Local", "a");
    dm._rules.RecordKill(2, "a", "A", "b");
    dm._rules.RecordKill(3, "a", "A", "b");
    var snapshot = dm.AnnouncementSnapshot();
    Check(snapshot.Live && snapshot.KillsRemaining == 8 && snapshot.Lead == -1, "FFA uses actual round leader kills, not local totals");
    Check(snapshot.SecondsRemaining == 110 && !snapshot.FinalRound, "DM time and finite round state match rules");
    dm._rules.Tick(121); dm._rules.StartRound(125); Time.time = 126;
    snapshot = dm.AnnouncementSnapshot();
    Check(snapshot.KillsRemaining == 10 && snapshot.Lead == null && snapshot.FinalRound && snapshot.Epoch == 2, "DM counters reset per round while final round uses finite limit");
    dm._rules.Tick(246); Time.time = 246;
    Check(dm.AnnouncementSnapshot().Terminal && !dm.AnnouncementSnapshot().Draw, "Lost finite FFA match is not a fabricated draw");
    var tieOptions = new DeathmatchOptions(1, 0, 30, true, 3, false);
    var tie = new RoundsMode { _options = tieOptions, _rules = new DeathmatchRules(tieOptions, "me", "Local") };
    tie._rules.StartRound(0); tie._rules.Tick(30);
    Check(tie.AnnouncementSnapshot().Draw, "Actual terminal FFA tie is announced");
    var continuous = new DeathmatchOptions(0, 0, 0, true, 3, false);
    dm = new RoundsMode { _options = continuous, _rules = new DeathmatchRules(continuous, "me", "Local") };
    dm._rules.StartRound(0); dm._rules.RecordKill(1, "me", "Local", "a");
    snapshot = dm.AnnouncementSnapshot();
    Check(snapshot.SecondsRemaining == null && snapshot.KillsRemaining == null && snapshot.Lead == null, "Continuous DM cannot invent a target or leader");
    var soloOptions = new DeathmatchOptions(1, 10, 60, false, 3, false);
    dm = new RoundsMode { _options = soloOptions, _rules = new DeathmatchRules(soloOptions, "me", "Local") };
    dm._rules.StartRound(0); dm._rules.RecordKill(1, "me", "Local", "a");
    Check(dm.AnnouncementSnapshot().KillsRemaining == 9 && dm.AnnouncementSnapshot().Lead == null, "All-vs-player has a local target but no team lead contest");
    dm.Valid = false; Check(!dm.AnnouncementSnapshot().Active, "Foreign DM raid is inactive");

    var match = new ArenaMatch();
    var tdm = new TeamDeathmatchMode { _match = match, _rules = new TeamDeathmatchMatchPolicy(10, 120) };
    match.Mode = tdm; Time.time = 0;
    Check(tdm.AnnouncementSnapshot().Preparing, "TDM prepares during actual assembling");
    tdm._rules.Update(0, true, 0, 0);
    Check(tdm.AnnouncementSnapshot().Countdown == 3, "TDM reads actual three second countdown");
    Time.time = 1.1f; Check(tdm.AnnouncementSnapshot().Countdown == 2, "TDM advances to actual current digit");
    tdm._rules.Update(1.2f, false, 0, 0);
    Check(tdm.AnnouncementSnapshot().Countdown == null && tdm.AnnouncementSnapshot().Preparing, "Readiness loss removes countdown");
    tdm._rules.Update(2, true, 0, 0); tdm._rules.Update(5, true, 0, 0); Time.time = 10;
    match.Scoreboard.Friendly = new() { Kills = 4, Score = 800 };
    match.Scoreboard.Opponent = new() { Kills = 6, Score = 700 };
    snapshot = tdm.AnnouncementSnapshot();
    Check(snapshot.KillsRemaining == 4 && snapshot.Lead == -1, "TDM uses raw team kills rather than points");
    Check(snapshot.SecondsRemaining == 115 && snapshot.Live, "TDM time starts only after readiness countdown");
    tdm._rules.Update(126, false, 6, 6); snapshot = tdm.AnnouncementSnapshot();
    Check(snapshot.Terminal && snapshot.Draw && !snapshot.Live, "TDM actual draw is terminal");

    var tactical = new TacticalMode { _match = match, _phase = TacticalMode.Phase.Live, RoundNumber = 2, _phaseEndsAt = 60 };
    Check(!tactical.AnnouncementSnapshot().Live && tactical.AnnouncementSnapshot().SecondsRemaining == null, "Solo Tactical assembly never spends or announces combat time");
    tactical._soloCombatReady = true; Time.time = 20;
    Check(tactical.AnnouncementSnapshot().SecondsRemaining == 40, "Solo Tactical remaining time uses actual live deadline");
    tactical._phase = TacticalMode.Phase.Buy;
    Check(tactical.AnnouncementSnapshot().Preparing && tactical.AnnouncementSnapshot().Countdown == null, "Tactical buy timer is not a predicted combat countdown");
    tactical._phase = TacticalMode.Phase.Live; tactical._teamArena = true; tactical._teamRoundsToWin = 3;
    tactical.RoundsWon = 2; tactical.RoundsLost = 1;
    Check(tactical.AnnouncementSnapshot().MatchPoint && !tactical.AnnouncementSnapshot().FinalRound, "PFT first-to target exposes match point only");
    tactical.RoundsLost = 2;
    Check(tactical.AnnouncementSnapshot().FinalRound, "Both sides one win away is actual deciding round");
    tactical._teamRoundsToWin = 1;
    tactical.RoundsWon = tactical.RoundsLost = 0;
    Check(tactical.AnnouncementSnapshot().MatchPoint && tactical.AnnouncementSnapshot().FinalRound, "First-to-one PFT is a real deciding round");

    var game = new LocalGame { Status = GameStatus.Started };
    var world = new GameWorld { MainPlayer = new Player() };
    Singleton<AbstractGame>.Instance = ArenaSession.Game = game;
    Singleton<GameWorld>.Instance = ArenaSession.World = world;
    var gun = new GunGameMode { _match = match, _tier = 2, _wins = 1, _ladder = new() { new(), new(), new() } };
    Check(gun.AnnouncementSnapshot().WeaponTier == 2 && gun.AnnouncementSnapshot().LadderCycle == 1, "Equipped Gun Game final tier and cycle are available");
    gun._loadoutDirty = true; Check(gun.AnnouncementSnapshot().WeaponTier == null, "Earned but not equipped final weapon stays silent");
    gun._loadoutDirty = false; world.MainPlayer.HealthController.IsAlive = false;
    Check(gun.AnnouncementSnapshot().WeaponTier == null, "Dead body cannot announce equipped final weapon");
    world.MainPlayer.HealthController.IsAlive = true;
    ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress = true;
    Check(gun.AnnouncementSnapshot().WeaponTier == null, "In-flight respawn cannot announce equipped final weapon");
    ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress = false;

    tdm = new TeamDeathmatchMode { _match = match, _rules = new TeamDeathmatchMatchPolicy(10, 120) };
    match.Mode = tdm; ArenaMatchService.Current = match; Time.time = 0;
    ArenaAnnouncementDirector.Attach(match); ArenaAnnouncementDirector.Tick();
    Check(match.Cues.Contains(ArenaAnnouncerCue.PrepareToFight), "Actual director dispatches preparation from current mode");
    tdm._rules.Update(0, true, 0, 0); ArenaAnnouncementDirector.Tick();
    Check(match.Cues.Contains(ArenaAnnouncerCue.Three), "Actual director dispatches real countdown digit");
    tdm._rules.Update(0.5f, false, 0, 0); Time.time = 0.5f; ArenaAnnouncementDirector.Tick();
    Check(match.Cues[^1] == ArenaAnnouncerCue.CancelStatus, "Actual readiness loss cancels transient cues");
    var foreign = new GameWorld(); Singleton<GameWorld>.Instance = foreign;
    int before = match.Cues.Count; ArenaAnnouncementDirector.Tick();
    Check(match.Cues.Count == before + 1 && match.Cues[^1] == ArenaAnnouncerCue.CancelStatus, "Foreign world cancels and cannot publish progress");
    ArenaAnnouncementDirector.Tick(); Check(match.Cues.Count == before + 1, "Foreign-world cancellation is bounded");
    Singleton<GameWorld>.Instance = world; ArenaSession.Active = false;
    ArenaAnnouncementDirector.Tick(); Check(match.Cues.Count == before + 1, "Normal unarmed raid remains silent");
    ArenaSession.Active = true; ArenaAnnouncementDirector.Detach(); ArenaAnnouncementDirector.Tick();
    Check(match.Cues.Count == before + 1, "Detached director cannot publish old match state");
    Check(ArenaMode.Plugin.ModLogger.Lines.Count == 0, "Adapters and director completed without hidden failures");
    Console.WriteLine($"ANNOUNCEMENT INTEGRATION PASSED ({checks} assertions)");
}
catch (Exception ex) { Console.Error.WriteLine($"ANNOUNCEMENT INTEGRATION FAILED after {checks}: {ex.Message}"); return 1; }
return 0;
