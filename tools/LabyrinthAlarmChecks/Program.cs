using ArenaMode.Audio;
using ArenaMode.Config;
using ArenaMode.Session;
using Audio.AudioCulling;
using Comfort.Common;
using CommonAssets.Scripts.Game;
using EFT;
using SPT.Reflection.Patching;
using UnityEngine;

int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    checks++;
}
SyncLoopSoundPlayer Fixture()
{
    LabyrinthAlarmPatches.Shutdown();
    LabyrinthAlarmPatches.Enable();
    ArenaSession.IsActive = true;
    Singleton<GameWorld>.Instance = ArenaSession.World = new();
    Singleton<AbstractGame>.Instance = ArenaSession.Game = new();
    Settings.MuteLabyrinthAlarm.Value = true;
    LocationScene.Reads = 0;
    LocationScene.Entities.Clear();
    var player = new SyncLoopSoundPlayer
    {
        gameObject = new() { name = "alarm_sound_root", scene = new(LabyrinthAlarmPatches.ScenePath) },
        _clip = new() { name = "alarm_oneshot" },
    };
    player._sources = Enumerable.Range(0, 7).Select(i => new AudioSource
    {
        gameObject = new() { name = $"alarm_sound_{i}", scene = player.gameObject.scene },
        clip = player._clip,
    }).ToArray();
    foreach (var source in player._sources)
        source.transform.Parent = new() { Parent = player.transform }; // Nested speakers are legitimate.
    foreach (string id in new[] { "3508252692", "4274982985", "336915922", "3861355289" })
        LocationScene.Entities.Add(new SyncableLoopSoundPlayer
        {
            gameObject = new() { name = "INTERACTIVE_Labyrinth_Boss_Alarm", scene = player.gameObject.scene },
            _playTriggerId = "alarm_enter_" + id,
            _stopTriggerId = "",
            _loopSoundPlayer = player,
        });
    return player;
}
bool NativeAllowed(SyncLoopSoundPlayer player, AudioSource source)
    => (bool)ModulePatch.InstalledPrefix.Invoke(null, new object[] { player, source });

var p = Fixture();
Check(ModulePatch.Target.Name == "InitializeSource"
    && ModulePatch.Target.DeclaringType == typeof(SyncLoopSoundPlayer)
    && ModulePatch.Target.GetParameters().Single().ParameterType == typeof(AudioSource), "exact audio-only hook");
var originalSources = p._sources;
var originalClip = p._clip;
LabyrinthAlarmPatches.Tick();
Check(LocationScene.Reads == 1, "single registry scan");
foreach (var s in p._sources)
{
    Check(s.Stops == 1 && !s.Scheduled, "shared player deduped; pre-Arena scheduled source cancelled");
    for (int round = 0; round < 4; round++)
        Check(!NativeAllowed(p, s), "repeated audio blocked through rounds/respawns");
}
LabyrinthAlarmPatches.Tick();
Check(LocationScene.Reads == 1, "no per-frame scene scan");
Check(ReferenceEquals(originalSources, p._sources) && ReferenceEquals(originalClip, p._clip), "source collection and asset unchanged");
Check(LocationScene.Entities.Count == 4
    && LocationScene.Entities.Cast<SyncableLoopSoundPlayer>().All(h => h._loopSoundPlayer == p
        && h._playTriggerId.StartsWith("alarm_enter_") && h._stopTriggerId == ""), "trigger wiring unchanged");

foreach (var mutation in new (string Name, Action<SyncLoopSoundPlayer> Apply)[]
{
    ("ordinary raid", _ => ArenaSession.IsActive = false),
    ("setting off", _ => Settings.MuteLabyrinthAlarm.Value = false),
    ("other map", _ => Singleton<GameWorld>.Instance.LocationId = "laboratory"),
    ("missing map", _ => Singleton<GameWorld>.Instance.LocationId = null),
    ("stale world", _ => Singleton<GameWorld>.Instance = new()),
    ("stale game", _ => Singleton<AbstractGame>.Instance = new()),
    ("missing world", _ => Singleton<GameWorld>.Instance = null),
    ("missing game", _ => Singleton<AbstractGame>.Instance = null),
    ("wrong scene", x => x.gameObject.scene = new("SomeOtherScene")),
    ("unloaded scene", x => x.gameObject.scene = new(LabyrinthAlarmPatches.ScenePath, isLoaded: false)),
    ("invalid scene", x => x.gameObject.scene = new(LabyrinthAlarmPatches.ScenePath, handle: 0)),
    ("renamed player", x => x.gameObject.name = "unrelated_sound_root"),
    ("different clip", x => x._clip.name = "announcer"),
    ("missing clip", x => x._clip = null),
    ("autoplay ambient", x => x._playOnAwake = true),
    ("different playback contract", x => x._useSoundDelayCalculation = false),
})
{
    p = Fixture();
    mutation.Apply(p);
    Check(NativeAllowed(p, p._sources[0]), mutation.Name + " keeps vanilla output");
    LabyrinthAlarmPatches.Tick();
    Check(p._sources.All(s => s.Stops == 0 && s.Scheduled), mutation.Name + " never stops any source");
}

foreach (var mutation in new (string Name, Action<SyncLoopSoundPlayer, AudioSource> Apply)[]
{
    ("foreign parent", (x,s) => s.transform.Parent = new()),
    ("foreign scene instance", (x,s) => s.gameObject.scene = new(LabyrinthAlarmPatches.ScenePath, handle: 2)),
    ("source reused for another clip", (x,s) => s.clip = new() { name = "alarm_oneshot" }),
    ("not a registered source", (x,s) => x._sources = x._sources.Skip(1).ToArray()),
    ("missing sources", (x,s) => x._sources = null),
})
{
    p = Fixture();
    var s = p._sources[0];
    mutation.Apply(p, s);
    Check(NativeAllowed(p, s) && s.Stops == 0, mutation.Name + " remains untouched");
    LabyrinthAlarmPatches.Tick();
    Check(s.Stops == 0, mutation.Name + " also excluded from initial cleanup");
}

p = Fixture();
Check(NativeAllowed(null, p._sources[0]) && NativeAllowed(p, null), "missing native objects fail open");
p._sources[0].ThrowOnStop = true;
Check(NativeAllowed(p, p._sources[0]), "destroyed source fails open");

p = Fixture();
LabyrinthAlarmPatches.Tick();
Settings.MuteLabyrinthAlarm.Value = false;
LabyrinthAlarmPatches.Tick();
Check(NativeAllowed(p, p._sources[0]), "turning setting off permits next native cycle");
p._sources[0].Scheduled = true;
Settings.MuteLabyrinthAlarm.Value = true;
LabyrinthAlarmPatches.Tick();
Check(p._sources[0].Stops == 2 && !p._sources[0].Scheduled && LocationScene.Reads == 2, "hot re-enable cancels current siren immediately");

// A missed GameEnd must never cause the old Arena latch to stop new ordinary audio.
Singleton<GameWorld>.Instance = new();
LabyrinthAlarmPatches.Tick();
Check(NativeAllowed(p, p._sources[0]) && LocationScene.Reads == 2, "stale Arena latch cannot cross exact raid ownership");
ArenaSession.World = Singleton<GameWorld>.Instance;
LabyrinthAlarmPatches.Tick();
Check(LocationScene.Reads == 3, "new confirmed Arena world permits fresh scan");

p = Fixture();
foreach (var h in LocationScene.Entities.Cast<SyncableLoopSoundPlayer>()) h._playTriggerId = "unrelated_event";
LabyrinthAlarmPatches.Tick();
Check(p._sources.All(s => s.Stops == 0), "initial cleanup rejects foreign trigger wiring");
Check(!NativeAllowed(p, p._sources[0]), "scene-verified lower output guard works even with registry drift");
LabyrinthAlarmPatches.Shutdown();
Check(!ModulePatch.Active && NativeAllowed(p, p._sources[0]), "shutdown disables suppression and removes its hook");

Console.WriteLine($"ARENA_LABYRINTH_ALARM_CHECKS_PASSED ({checks} assertions)");
