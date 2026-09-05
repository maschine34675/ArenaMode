using ArenaMode;
using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.GameTriggers;
using SPT.Reflection.Patching;
using UnityEngine;

int count = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); count++; }
GameObject Go(string name) => new() { name = name, scene = new(LabyrinthSpawnTraps.ScenePath) };
void Add(ITriggerEntity item) => LocationScene.Entities.Add(item);
T[] All<T>() => LocationScene.Entities.OfType<T>().ToArray();
bool NativeAllowed<T>(T instance) => (bool)ModulePatch.Hooks[typeof(T)].Invoke(null, new object[] { instance });
void Fixture()
{
    LabyrinthSpawnTraps.Shutdown();
    LabyrinthSpawnTraps.Enable();
    Settings.DisableLabyrinthSpawnTraps.Value = true;
    ArenaSession.IsActive = true;
    Singleton<GameWorld>.Instance = ArenaSession.World = new();
    Singleton<AbstractGame>.Instance = ArenaSession.Game = new();
    LocationScene.Entities.Clear(); LocationScene.Reads = 0;
    Time.unscaledTime = 1;
    Plugin.ModLogger = new();
    var groups = new[]
    {
        ("Fire_zone", "flame", "493925125", "1349880479", "INTERACTIVE_Flametrap_00001"),
        ("Luquid_damage_zone", "Luquid", "3486811782", "2773347541", "INTERACTIVE_Lifting_grate_00001"),
        ("Steam_damage_zone", "steam", "115583983", "3704346085", "INTERACTIVE_Valve_00001"),
    };
    foreach (var (name, prefix, soft, deadly, config) in groups)
        for (int i = 0; i < 2; i++)
        {
            string id = i == 0 ? soft : deadly;
            Add(new HandlerDamage { gameObject = Go(name + "_0" + (i + 1)),
                _triggerIdStartDamage = prefix + "_enter_zone_" + id,
                _triggerIdFinishDamage = prefix + "_exit_zone_" + id,
                ConfigurationId = config + (i == 0 ? "_soft" : "_deadly") });
        }
    foreach (string id in new[] { "1628276106", "2131128863", "1446364582", "2505672369" })
        Add(new HandlerShoot { gameObject = Go("INTERACTIVE_Guntrap_01"), _triggerId = "guntrap_trigger_" + id,
            ConfigurationId = "INTERACTIVE_Guntrap_00001_" + (id == "1446364582" ? "deadly" : "soft") });
    foreach (var gun in All<HandlerShoot>())
        Add(new HandlerEffect { gameObject = Go(gun.gameObject.name), _triggerId = gun._triggerId });
    foreach (var liquid in All<HandlerDamage>().Skip(2).Take(2))
        Add(new HandlerEffect { gameObject = Go(liquid.gameObject.name), _triggerId = liquid._triggerIdStartDamage });
    var switches = new[]
    {
        ("disable_traps_01", "INTERACTIVE_Toggle_Trap_Switch", All<HandlerShoot>().Select(x => x._triggerId).ToArray()),
        ("disable_traps_02", "Interactive_button_12", All<HandlerDamage>().Take(2).Select(x => x._triggerIdStartDamage).ToArray()),
        ("disable_traps_04", "INTERACTIVE_Toggle_Trap_Switch", All<HandlerDamage>().Skip(2).Take(2).Select(x => x._triggerIdStartDamage).ToArray()),
        ("set_valve_01", "Valve_PlaceHolder", All<HandlerDamage>().Skip(4).Select(x => x._triggerIdStartDamage).ToArray()),
    };
    foreach (var (id, name, targets) in switches)
        Add(new HandlerTriggerState { gameObject = Go(name), _triggerId = id, _controlledTriggerId = targets });
    foreach (var (name, id, parameter, value) in new[]
    {
        ("Fire_zone_Animaton", "Fire_zone_switch_01", "isGoing", false),
        ("Valve_Animation_Sync", "set_valve_01", "HasValve", true),
        ("INTERACTIVE_Lifting_grate_01", "LiftGate_843566315", "IsClosed", true),
    })
    {
        var go = Go(name);
        Add(new HandlerAnimator { gameObject = go, _target = new() { gameObject = go },
            _animatorActions = new[] { new HandlerAnimator.AnimatorAction
                { TriggerId = id, BoolAction = new() { Name = parameter, Value = value } } } });
    }
    foreach (var name in new[] { "Fire_zone_01", "Fire_zone_02", "Gas_sound_01", "Gas_sound_01" })
        Add(new HandlerPlaySoundSequenced { gameObject = Go(name), _playTriggerId = "",
            _stopTriggerId = name.StartsWith("Fire") ? "disable_traps_02" : "set_valve_01",
            _playOnSoundSystemInit = true, _playOnce = true });
}
void CheckUntouched(string label)
{
    Check(Singleton<GameWorld>.Instance?.TriggersEmitter.Ignored.Count is null or 0, label + " emitter");
    Check(All<HandlerDamage>().All(x => x.Finishes == 0), label + " existing damage");
    Check(All<HandlerAnimator>().All(x => x._target == null || x._target.Calls == 0), label + " animator");
    Check(All<HandlerPlaySoundSequenced>().All(x => x.Stops == 0 && x.CoroutineStops == 0), label + " audio");
}

Fixture();
Check(ModulePatch.Hooks.Count == 4, "four exact native hooks installed");
foreach (var d in All<HandlerDamage>()) Check(!NativeAllowed(d), "damage prevented before initial tick");
foreach (var s in All<HandlerShoot>()) Check(!NativeAllowed(s), "shot and its effect prevented before initial tick");
foreach (var s in All<HandlerPlaySoundSequenced>()) Check(!NativeAllowed(s), "continuous trap restart prevented");
foreach (var e in All<HandlerEffect>()) Check(!NativeAllowed(e), "trap health effects prevented before initial tick");
LabyrinthSpawnTraps.Tick();
Check(Singleton<GameWorld>.Instance.TriggersEmitter.Ignored.Count == 10, "all ten entry trigger IDs disabled");
Check(All<HandlerDamage>().All(x => !x.DamageRunning && x.Finishes == 1), "existing damage coroutines stopped");
foreach (var a in All<HandlerAnimator>())
    Check(a._target.Calls == 1 && a._target.Values[a._animatorActions[0].BoolAction.Name] == a._animatorActions[0].BoolAction.Value,
        "native switch animation state applied");
foreach (var s in All<HandlerPlaySoundSequenced>())
{
    Check(!s.PendingStart && s.PendingEnd && s.Fading && s.Stops == 1, "only start coroutine cancelled; native fade and end timer retained");
    s.AdvanceNativeStop();
    Check(!s.PendingEnd && !s.LoopSource && !s.OneShotSource && s.LoopReleases == 1 && s.OneShotReleases == 1,
        "native audio completion releases each source exactly once");
}
for (int round = 0; round < 10; round++)
{
    Time.unscaledTime++;
    LabyrinthSpawnTraps.Tick();
    Check(All<HandlerDamage>().All(x => !NativeAllowed(x)), "respawns and new rounds cannot re-arm damage");
}
Check(LocationScene.Reads == 1 && All<HandlerTriggerState>().All(x => x.Calls == 1), "no repeated scene mutation or scan");

foreach (var (label, mutate) in new (string, Action)[]
{
    ("ordinary raid", () => ArenaSession.IsActive = false),
    ("other map", () => Singleton<GameWorld>.Instance.LocationId = "laboratory"),
    ("stale world", () => Singleton<GameWorld>.Instance = new()),
    ("stale game", () => Singleton<AbstractGame>.Instance = new()),
    ("missing world", () => Singleton<GameWorld>.Instance = null),
    ("missing game", () => Singleton<AbstractGame>.Instance = null),
    ("disabled option", () => Settings.DisableLabyrinthSpawnTraps.Value = false),
})
{
    Fixture(); mutate();
    foreach (var d in All<HandlerDamage>()) Check(NativeAllowed(d), label + " damage remains vanilla");
    foreach (var s in All<HandlerShoot>()) Check(NativeAllowed(s), label + " firing remains vanilla");
    foreach (var s in All<HandlerPlaySoundSequenced>()) Check(NativeAllowed(s), label + " sounds remain vanilla");
    foreach (var e in All<HandlerEffect>()) Check(NativeAllowed(e), label + " effects remain vanilla");
    LabyrinthSpawnTraps.Tick(); CheckUntouched(label);
}

foreach (var (label, mutate) in new (string, Action)[]
{
    ("other scene", () => All<MonoBehaviour>().ToList().ForEach(x => x.gameObject.scene = new("elsewhere"))),
    ("unloaded scene", () => All<MonoBehaviour>().ToList().ForEach(x => x.gameObject.scene = new(LabyrinthSpawnTraps.ScenePath, isLoaded: false))),
    ("foreign damage configuration", () => All<HandlerDamage>()[0].ConfigurationId = "INTERACTIVE_Spikes_00001"),
    ("changed finish ID", () => All<HandlerDamage>()[0]._triggerIdFinishDamage = "other_exit"),
    ("foreign control ID", () => All<HandlerTriggerState>()[0]._controlledTriggerId[0] = "quest_progress"),
    ("control enables traps", () => All<HandlerTriggerState>()[0]._targetState = true),
    ("foreign animator target", () => All<HandlerAnimator>()[0]._target = new() { gameObject = Go("foreign") }),
    ("extra animator action", () => All<HandlerAnimator>()[0]._animatorActions[0].IntAction.Name = "foreign"),
    ("missing loop", () => LocationScene.Entities.Remove(All<HandlerPlaySoundSequenced>()[0])),
    ("non-local emitter", () => Singleton<GameWorld>.Instance.TriggersEmitter = new TriggersEmitter()),
})
{
    Fixture(); mutate();
    for (int i = 0; i < 10; i++) { Time.unscaledTime += 1; LabyrinthSpawnTraps.Tick(); }
    CheckUntouched(label);
    Check(LocationScene.Reads <= 5, label + " bounded retry");
    Check(Plugin.ModLogger.Lines.Count(x => x.Contains("incomplete after 5 attempts")) == 1, label + " one diagnostic");
}

Fixture();
var d0 = All<HandlerDamage>()[0]; d0.ConfigurationId = "other";
Check(NativeAllowed(d0), "unrecognized damage is not suppressed");
var s0 = All<HandlerShoot>()[0]; s0._triggerId = "unrelated_shot";
Check(NativeAllowed(s0), "unrecognized gun is not suppressed");
var loop0 = All<HandlerPlaySoundSequenced>()[0]; loop0._playTriggerId = "quest_event";
Check(NativeAllowed(loop0), "one-shot or puzzle sound is not suppressed");
var effect0 = All<HandlerEffect>()[0]; effect0._triggerId = "unrelated_quest";
Check(NativeAllowed(effect0), "unrelated health effects remain vanilla");
Check(LabyrinthSpawnTraps.AllowDamageStart(null) && LabyrinthSpawnTraps.AllowShoot(null)
    && LabyrinthSpawnTraps.AllowLoopStart(null) && LabyrinthSpawnTraps.AllowEffect(null), "missing handler fails open");

Fixture();
var alreadyStopping = All<HandlerPlaySoundSequenced>()[0];
alreadyStopping.StopSound(); // User's switch started a fade/end timer before Arena tick.
LabyrinthSpawnTraps.Tick();
Check(alreadyStopping.PendingEnd && alreadyStopping.Fading && !alreadyStopping.PendingStart,
    "previously triggered native stop keeps its cleanup timers");
alreadyStopping.AdvanceNativeStop();
Check(alreadyStopping.LoopReleases == 1 && alreadyStopping.OneShotReleases == 1, "already stopping source completes without leaks or recursive release");

// Positive control: the stand-in detects the native reentrant-release bug.
var unsafeSound = new HandlerPlaySoundSequenced();
unsafeSound.StopSound();
bool recursive = false;
try { unsafeSound.OnLoopSourceFadeOut(); } catch (NullReferenceException) { recursive = true; }
Check(recursive, "audio stand-in rejects manually forced fade callbacks");

Fixture();
All<HandlerPlaySoundSequenced>()[2].FailNextStop = true;
LabyrinthSpawnTraps.Tick();
Check(All<HandlerPlaySoundSequenced>()[0].PendingEnd, "partial setup retains already scheduled end timer");
Time.unscaledTime++; LabyrinthSpawnTraps.Tick();
foreach (var sound in All<HandlerPlaySoundSequenced>())
{
    sound.AdvanceNativeStop();
    sound.OnLoopSourceFadeOut(); // Later teardown callback must remain a no-op.
    sound.OnEndSoundPlayed();
    Check(!sound.PendingStart && sound.LoopReleases == 1 && sound.OneShotReleases == 1,
        "retry after later-loop failure releases each native source once");
}

Fixture();
var absent = All<HandlerAnimator>()[0]; LocationScene.Entities.Remove(absent);
LabyrinthSpawnTraps.Tick(); CheckUntouched("not ready");
Add(absent); Time.unscaledTime += 1; LabyrinthSpawnTraps.Tick();
Check(Singleton<GameWorld>.Instance.TriggersEmitter.Ignored.Count == 10 && LocationScene.Reads == 2, "delayed registry readiness recovers");

Settings.DisableLabyrinthSpawnTraps.Value = false;
Time.unscaledTime++; LabyrinthSpawnTraps.Tick();
Check(All<HandlerDamage>().All(x => !NativeAllowed(x)), "turning option off does not re-arm a trap around live players");
Check(All<HandlerTriggerState>().All(x => x.Calls == 1), "option toggle cannot retrigger animations");
Singleton<GameWorld>.Instance = ArenaSession.World = new();
Singleton<AbstractGame>.Instance = ArenaSession.Game = new();
LabyrinthSpawnTraps.Tick();
Check(All<HandlerDamage>().All(NativeAllowed), "new exact raid reads disabled option");
Settings.DisableLabyrinthSpawnTraps.Value = true;
LabyrinthSpawnTraps.Tick();
Check(All<HandlerDamage>().All(NativeAllowed), "enabling option also waits for next raid");
Singleton<GameWorld>.Instance = ArenaSession.World = new();
Singleton<AbstractGame>.Instance = ArenaSession.Game = new();
LabyrinthSpawnTraps.Tick();
Check(Singleton<GameWorld>.Instance.TriggersEmitter.Ignored.Count == 10, "following raid reads enabled option");
LabyrinthSpawnTraps.Shutdown();
Check(ModulePatch.Hooks.Count == 0 && LabyrinthSpawnTraps.AllowDamageStart(All<HandlerDamage>()[0]), "shutdown removes guard ownership");
Console.WriteLine($"ARENA_LABYRINTH_TRAP_CHECKS_PASSED ({count} assertions)");
