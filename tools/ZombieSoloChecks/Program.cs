using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using Redeploy.Core.Engine;
using UnityEngine;

int checks = 0;
void Check(bool condition, string reason) { checks++; if (!condition) throw new InvalidOperationException(reason); }
(ZombieMode mode, ArenaMatch match, LocalGame game, GameWorld world) Start(bool ownGear = false, float replay = 0f)
{
    Time.time = 0; Time.timeScale = 1;
    var player = new LocalPlayer { IsYourPlayer = true, Profile = new() { Id = "local", Nickname = "You" } };
    var game = new LocalGame { LocalPlayer = player, Owner = new() { Player = player } };
    var world = new GameWorld { MainPlayer = player };
    Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = world;
    ArenaSession.Active = true; ArenaSession.ZombieOwnGearActive = ownGear; ArenaSession.ReplaySeconds = replay;
    PitFireTeamCompat.TeamArenaActive = false;
    ArenaRespawnDriver.IsRespawnInProgress = false; ArenaRespawnDriver.StandDown = false;
    RespawnEngineState.IsPipelineRunning = false;
    KillcamReplayCompat.State = OptionalCameraOwnerState.Absent; KillcamReplayCompat.Remaining = 0;
    ArenaMode.Kits.ZombieLoadoutRefill.Calls = 0;
    Settings.ZombieWaveStartSize.Value = 2; Settings.ZombieIntermissionSeconds.Value = 2;
    var mode = new ZombieMode(); var match = new ArenaMatch(mode, "local", "You", "PMC");
    ArenaMatchService.Current = match; mode.OnMatchStart(match);
    LiveKitRespawn.Reset();
    // Terminal-death scenarios run after the shared opening countdown.
    mode.Tick(0); Time.time=2.1f;
    ArenaMatchService.GenericAborts = 0;
    return (mode, match, game, world);
}
void Tick(ZombieMode mode, float seconds = .3f) { Time.time += seconds; mode.Tick(seconds); }
void Die(LocalGame game) { game.LocalPlayer.HealthController.IsAlive = false; ArenaRespawnDriver.IsRespawnInProgress = true; }
ArenaKill Victim() => new() { LocalIsVictim = true, VictimProfileId = "local", VictimBodyId = 1, KillerProfileId = "zombie", VictimRole = ArenaRole.Pmc };
ArenaKill ZombieKill(string id = "infected") => new() { KilledByLocal = true, KillerProfileId = "local", VictimProfileId = id, VictimRoleLabel = "ZOMBIE", VictimRole = ArenaRole.Scav, AwardEligible = true };

foreach (bool own in new[] { false, true })
foreach (bool killFirst in new[] { false, true })
{
    var (mode, match, game, world) = Start(own);
    Check(mode.HoldRespawn && !mode.IsComplete, "solo has one life before callbacks, with either equipment choice");
    Tick(mode); match.ReportKill(ZombieKill());
    Check(match.Kills == 1 && mode.ZombiesKilled == 1, "living solo can score");
    int dispatched = ZombieSpawnService.Dispatched, points = match.Score;
    int finalKillfeedEvents = 0;
    match.OnKill += kill => { if (kill.LocalIsVictim) finalKillfeedEvents++; };
    Die(game);
    if (killFirst) { match.ReportKill(Victim()); match.ReportLocalDeath(); }
    else { match.ReportLocalDeath(); match.ReportKill(Victim()); }
    Check(mode.IsComplete && mode.HoldRespawn && match.Deaths == 1, "first solo death freezes exactly one casualty in either native order");
    match.ReportKill(Victim());
    Check(finalKillfeedEvents == 1, "final local killfeed entry appears once in either callback order");
    Check(ArenaMatchResult.HasResult(mode) && ArenaMatchResult.OwnsRaid(mode), "solo owns protected terminal exit");
    Check(ArenaMatchResult.MayAutoOpenPanel(mode), "no-replay solo result may show immediately");
    Check(!mode.AssistScoringOpen && !mode.IsSpawnAdmissionOpen, "terminal closes assists and async infected admission");
    match.ReportKill(ZombieKill("late")); match.ReportLocalDeath();
    Check(match.Kills == 1 && match.Score == points && match.Deaths == 1, "late kill and duplicate death cannot change frozen result");
    game.Owner = null;
    Tick(mode);
    Check(game.Stops == 0 && mode.HoldRespawn && !ArenaRespawnDriver.StandDown, "missing native owner retains terminal death interception");
    Check(ZombieSpawnService.Dispatched == dispatched && ArenaMode.Kits.ZombieLoadoutRefill.Calls == 0, "no further waves or loadout refill after solo death");
    Check(mode.ResultExitStatus.Contains("owner"), "native-owner failure is visible");
    game.Owner = new() { Player = game.LocalPlayer };
    Tick(mode, 1.1f);
    Check(game.Stops == 1 && game.LastStatus == ExitStatus.Killed && ArenaRespawnDriver.StandDown, "verified dead native owner exits once as killed");
    Tick(mode, 5); Check(game.Stops == 1, "no second native Stop");
    mode.OnMatchEnd();
}

{
    var (mode, match, game, _) = Start();
    Die(game); Tick(mode, .1f);
    Check(mode.IsComplete && match.Deaths == 1 && ZombieSpawnService.Dispatched == 0, "native health backstop ends unreported death before first wave");
    Check(game.Stops == 0, "death listeners get one bounded scheduling grace");
    mode.OnMatchEnd();
}
{
    var (mode, match, game, world) = Start();
    Die(game); match.ReportLocalDeath(); RespawnEngineState.IsPipelineRunning = true;
    Tick(mode, 5); Check(game.Stops == 0 && mode.HoldRespawn, "existing native replacement must settle before Stop");
    RespawnEngineState.IsPipelineRunning = false; world.MainPlayer = new LocalPlayer();
    Tick(mode, 1.1f); Check(game.Stops == 0 && !ArenaRespawnDriver.StandDown, "mismatched world body cannot release terminal hold");
    world.MainPlayer = game.LocalPlayer; game.StopThrows = true;
    Tick(mode, 1.1f); Check(game.Stops == 1 && !ArenaRespawnDriver.StandDown && mode.HoldRespawn, "precommit stop failure restores interception");
    game.StopThrows = false; Time.timeScale = 0;
    Tick(mode, 1.1f); Check(game.Stops == 1 && !ArenaRespawnDriver.StandDown, "paused simulation defers fade/native exit");
    Time.timeScale = 1; Tick(mode, 1.1f); Check(game.Stops == 2 && game.Status == GameStatus.Stopping, "ready retry exits after prior failure");
    mode.OnMatchEnd();
}
foreach (bool timeout in new[] { false, true })
{
    var (mode, match, game, _) = Start(replay: 3);
    Die(game); match.ReportLocalDeath(); Tick(mode, .1f);
    Check(ArenaMatchResult.OwnsRaid(mode) && mode.HoldRespawn && !ArenaMatchResult.MayAutoOpenPanel(mode),
        "selected replay holds automatic result panel during native death-listener grace without delaying terminal state");
    KillcamReplayCompat.State = OptionalCameraOwnerState.Busy; KillcamReplayCompat.Remaining = 3;
    Tick(mode, .3f); Check(game.Stops == 0 && mode.HoldRespawn, "pending replay keeps dead body and terminal hold");
    Check(!ArenaMatchResult.MayAutoOpenPanel(mode), "pending playback cannot auto-open a scoreboard over killcam");
    Tick(mode, 2); Check(game.Stops == 0, "selected replay can finish before native exit");
    Check(!ArenaMatchResult.MayAutoOpenPanel(mode), "playing replay remains unobstructed by automatic results");
    if (!timeout) KillcamReplayCompat.State = OptionalCameraOwnerState.Idle;
    game.Owner = null;
    Tick(mode, timeout ? 7 : .3f);
    Check(ArenaMatchResult.MayAutoOpenPanel(mode) == !timeout,
        "only proven replay cleanup admits automatic panel; timeout retains terminal exit without an overlay");
    game.Owner = new() { Player = game.LocalPlayer }; Tick(mode, 1.1f);
    Check(game.Stops == 1 && mode.HoldRespawn, "replay cleanup or bounded timeout exits without respawn");
    mode.OnMatchEnd();
}
{
    var (mode, match, game, _) = Start(replay: 3);
    Die(game); match.ReportLocalDeath(); game.Owner = null;
    Tick(mode, .3f);
    Check(ArenaMatchResult.MayAutoOpenPanel(mode), "selected but absent replay releases automatic panel after grace");
    mode.OnMatchEnd();
    var ordinary = new RoundsMode { HasResult=true, ResultOwnsCurrentRaid=true };
    Check(ArenaMatchResult.MayAutoOpenPanel(ordinary), "other terminal modes keep their existing automatic panel timing");
}
foreach (bool own in new[] { false, true })
{
    var (mode, match, game, _) = Start(own);
    Tick(mode); ZombieSpawnService.Pending=0; ZombieSpawnService.Alive=1;
    Tick(mode); ZombieSpawnService.Pending=0; ZombieSpawnService.Alive=0;
    Tick(mode);
    Check(!mode.IsComplete && ArenaMode.Kits.ZombieLoadoutRefill.Calls == 1, "living solo still refills between waves for either equipment choice");
    Die(game); match.ReportLocalDeath(); Tick(mode);
    Check(ArenaMode.Kits.ZombieLoadoutRefill.Calls == 1 && mode.IsComplete, "intermission death cannot grant another life");
    mode.OnMatchEnd();
}
{
    var (mode, match, game, _) = Start(replay: 3);
    Die(game); match.ReportLocalDeath(); game.Owner = null;
    KillcamReplayCompat.State = OptionalCameraOwnerState.Incompatible;
    var heldReplay = new ArenaDeathReplayHold(game, game.LocalPlayer, 1);
    Check(!heldReplay.Wait().MoveNext() && !heldReplay.CanRespawn, "unsafe replay releases coroutine without permitting replacement");
    Check(ArenaMatchService.GenericAborts == 0 && !ArenaRespawnDriver.StandDown,
        "unsafe terminal replay cannot enter generic abort or release interception");
    Tick(mode, 1);
    Check(game.Stops == 0 && mode.HoldRespawn && !ArenaRespawnDriver.StandDown,
        "unsafe replay retains terminal hold while native owner is missing");
    game.Owner = new() { Player = game.LocalPlayer }; Tick(mode, 1.1f);
    Check(game.Stops == 1, "unsafe replay exits only after exact native owner returns");
    mode.OnMatchEnd();
}
{
    var (mode, match, game, _) = Start();
    Die(game); match.ReportLocalDeath(); ArenaMatchService.Current = new ArenaMatch(new RoundsMode());
    Check(!ArenaMatchResult.OwnsRaid(mode), "stale terminal result cannot claim the next match");
    Tick(mode, 20); Check(game.Stops == 0 && !ArenaRespawnDriver.StandDown, "stale terminal match cannot stop another match");
    mode.OnMatchEnd();
}
Console.WriteLine($"ZOMBIE_SOLO_CHECKS_PASSED {checks} assertions");
