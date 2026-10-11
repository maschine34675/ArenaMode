using ArenaMode.Combat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using Redeploy.Core.Engine;
using static EFT.InventoryLogic.Weapon;

int assertions = 0;
void Check(bool value, string label)
{ assertions++; if (!value) throw new Exception("ASSERT: " + label); }
Player.FirearmController Setup()
{
    ArenaDefaultFireMode.Shutdown();
    var player = new Player();
    var hands = new Player.FirearmController { _player = player };
    player.HandsController = hands;
    var game = new LocalGame { Status = GameStatus.Started, LocalPlayer = player };
    var world = new GameWorld { MainPlayer = player };
    Singleton<AbstractGame>.Instance = game;
    Singleton<GameWorld>.Instance = world;
    ArenaSession.Game = game; ArenaSession.World = world;
    ArenaSession.IsActive = true; ArenaSession.ArmEpoch++;
    ArenaMode.Modes.ArenaMatchService.TeamStartAbortPending = false;
    ArenaMode.Areas.ArenaAreaRuntime.IsPending = false;
    ArenaMode.Respawn.ArenaRespawnDriver.StandDown = false;
    ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress = false;
    ArenaMode.Spectator.ArenaSpectatorService.IsActive = false;
    ArenaDefaultFireMode.Enable();
    return hands;
}
void Excluded(string label, Action<Player.FirearmController> change)
{
    var h = Setup(); change(h); ArenaDefaultFireMode.Tick();
    Check(h.Item.FireMode.Writes == 0 && h.FirearmsAnimator?.Writes is null or 0, label);
}

var h = Setup();
ArenaDefaultFireMode.Tick();
Check(h.Item.FireMode.FireMode == EFireMode.fullauto && h.Item.FireMode.Writes == 1, "initial current hands become fullauto");
Check(h.FirearmsAnimator.Mode == EFireMode.fullauto && h.FirearmsAnimator.SkipAnimation, "native animator synchronized without selector animation");
h.Item.FireMode.FireMode = EFireMode.single;
for (int n = 0; n < 120; n++) ArenaDefaultFireMode.Tick();
Check(h.Item.FireMode.FireMode == EFireMode.single && h.Item.FireMode.Writes == 1, "manual semi while held is preserved");
new Player.FirearmController.SpawnOperation { Controller = h }.WeaponAppeared();
Check(h.Item.FireMode.FireMode == EFireMode.fullauto && h.Item.FireMode.Writes == 2, "redraw defaults to fullauto again");

h = Setup(); h.Item.FireMode.FireMode = EFireMode.fullauto;
ArenaDefaultFireMode.Tick(); h.Item.FireMode.FireMode = EFireMode.single; ArenaDefaultFireMode.Tick();
Check(h.Item.FireMode.Writes == 0 && h.Item.FireMode.FireMode == EFireMode.single, "already auto is marked observed without writes");

foreach (var modes in new[] { new[] { EFireMode.single }, new[] { EFireMode.single, EFireMode.burst }, new[] { EFireMode.single, EFireMode.doublet }, new[] { EFireMode.single, EFireMode.doubleaction }, Array.Empty<EFireMode>() })
{
    h = Setup(); h.Item.FireMode.AvailableEFireModes = modes;
    ArenaDefaultFireMode.Tick(); new Player.FirearmController.SpawnOperation { Controller = h }.WeaponAppeared();
    Check(h.Item.FireMode.Writes == 0, "unsupported fire modes are not invented");
}
Excluded("ordinary raid unchanged", _ => ArenaSession.IsActive = false);
Excluded("missing world excluded", _ => Singleton<GameWorld>.Instance = null);
Excluded("missing game excluded", _ => Singleton<AbstractGame>.Instance = null);
Excluded("stale world excluded", _ => ArenaSession.World = new GameWorld());
Excluded("stale game excluded", _ => ArenaSession.Game = new LocalGame());
Excluded("stopped game excluded", _ => Singleton<AbstractGame>.Instance.Status = GameStatus.Stopped);
Excluded("nonlocal game excluded", _ => Singleton<AbstractGame>.Instance = new AbstractGame { Status = GameStatus.Started });
Excluded("AI excluded", x => x._player.IsAI = true);
Excluded("foreign player excluded", x => x._player.IsYourPlayer = false);
Excluded("dead player excluded", x => x._player.HealthController.IsAlive = false);
Excluded("old player excluded", _ => ((LocalGame)Singleton<AbstractGame>.Instance).LocalPlayer = new Player());
Excluded("foreign hands owner excluded", x => x._player = new Player());
Excluded("missing current hands excluded", x => x._player.HandsController = null);
Excluded("unfinished draw excluded", x => x.CurrentOperation = new object());
Excluded("missing animator deferred", x => x.FirearmsAnimator = null);
Excluded("stationary weapon excluded", x => x.Item.IsStationaryWeapon = true);
Excluded("malfunction preserved", x => x.Item.MalfState.State = EMalfunctionState.Jam);
Excluded("unknown modes ignored", x => x.Item.FireMode.AvailableEFireModes = null);
Excluded("aborting team excluded", _ => ArenaMode.Modes.ArenaMatchService.TeamStartAbortPending = true);
Excluded("pending area excluded", _ => ArenaMode.Areas.ArenaAreaRuntime.IsPending = true);
Excluded("respawn in progress excluded", _ => ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress = true);
Excluded("standdown excluded", _ => ArenaMode.Respawn.ArenaRespawnDriver.StandDown = true);
Excluded("spectator excluded", _ => ArenaMode.Spectator.ArenaSpectatorService.IsActive = true);

h = Setup(); ArenaSession.IsActive = false;
new Player.FirearmController.SpawnOperation { Controller = h }.WeaponAppeared();
Check(h.Item.FireMode.Writes == 0, "initial pre-arena draw cannot mutate gear");
ArenaSession.IsActive = true; ArenaDefaultFireMode.Tick();
Check(h.Item.FireMode.FireMode == EFireMode.fullauto, "initial draw caught after confirmed activation");

h = Setup(); ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress = true;
new Player.FirearmController.SpawnOperation { Controller = h }.WeaponAppeared();
Check(h.Item.FireMode.Writes == 0, "draw during replacement deferred");
ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress = false;
ArenaDefaultFireMode.Tick(); Check(h.Item.FireMode.Writes == 1, "published respawn catches up");
var old = h;
h = new Player.FirearmController { _player = old._player };
h._player.HandsController = h;
new Player.FirearmController.SpawnOperation { Controller = old }.WeaponAppeared();
Check(h.Item.FireMode.Writes == 0 && old.Item.FireMode.Writes == 1, "late old-controller draw excluded");
ArenaDefaultFireMode.Tick(); Check(h.Item.FireMode.Writes == 1, "weapon kit or pickup switch defaults to fullauto");

var bot = new Player { IsAI = true, IsYourPlayer = false };
var botHands = new Player.FirearmController { _player = bot };
bot.HandsController = botHands;
h.Item.FireMode.FireMode = EFireMode.single;
new Player.FirearmController.SpawnOperation { Controller = botHands }.WeaponAppeared();
Check(botHands.Item.FireMode.Writes == 0 && h.Item.FireMode.FireMode == EFireMode.single,
    "foreign bot draw cannot reset local manual selection");

// Native hands may be pooled; a completed draw must still reapply even when
// the published instance is the same one previously observed by Tick.
h._player.HandsController = null; ArenaDefaultFireMode.Tick();
h._player.HandsController = h;
new Player.FirearmController.SpawnOperation { Controller = h }.WeaponAppeared();
Check(h.Item.FireMode.FireMode == EFireMode.fullauto, "same pooled controller redraw defaults again");

var replacement = new Player();
var replacementHands = new Player.FirearmController { _player = replacement };
replacement.HandsController = replacementHands;
Singleton<GameWorld>.Instance.MainPlayer = replacement;
((LocalGame)Singleton<AbstractGame>.Instance).LocalPlayer = replacement;
new Player.FirearmController.SpawnOperation { Controller = h }.WeaponAppeared();
Check(replacementHands.Item.FireMode.Writes == 0, "old body callback cannot mutate replacement body");
ArenaDefaultFireMode.Tick();
Check(replacementHands.Item.FireMode.FireMode == EFireMode.fullauto,
    "new physical body defaults despite unchanged profile identity");

h = Setup(); h.Item.FireMode.Changed = () => h._player.HandsController = null;
ArenaDefaultFireMode.Tick(); Check(h.FirearmsAnimator.Writes == 0, "native callback cannot animate retired hands");

h = Setup(); ArenaDefaultFireMode.Tick(); h.Item.FireMode.FireMode = EFireMode.single;
RaidLifecycle.End();
ArenaDefaultFireMode.Tick(); Check(h.Item.FireMode.Writes == 2, "raid end releases observed controller");
ArenaDefaultFireMode.Shutdown(); h.Item.FireMode.FireMode = EFireMode.single;
ArenaDefaultFireMode.Tick(); new Player.FirearmController.SpawnOperation { Controller = h }.WeaponAppeared();
Check(h.Item.FireMode.FireMode == EFireMode.single && HarmonyLib.Harmony.Postfix == null, "shutdown disables hook and tick");
Console.WriteLine($"ARENA_DEFAULT_FIRE_MODE_CHECKS_PASSED: {assertions} assertions");
