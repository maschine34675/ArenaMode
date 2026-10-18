// Native output/PCM device are stand-ins. Collection, completion and all lifetime
// admission are production code. Real PCM/output have their own HitmarkerAudioChecks.
namespace UnityEngine { public static class AudioListener { public static bool pause; } }
namespace ArenaMode.Audio
{
    internal sealed class ArenaMasterAudioOutput
    {
        internal static int Requests;
        internal static bool Playing;
        internal bool TryPlay(object clip, float volume, out string problem)
        { problem=null; Requests++; Playing=true; return true; }
        internal void Tick(bool enabled,float volume) { if(!enabled) Reset(); }
        internal void Reset() => Playing=false;
    }
    internal sealed class HitmarkerPcmClip
    {
        internal static int Loads;
        internal static bool Fail;
        internal static string LastPath;
        internal static HitmarkerPcmClip Load(string path)
        { LastPath=path; Loads++; if(Fail) throw new IOException("missing cue"); return new(); }
        internal object GetRuntime()=>this;
        internal void ResetRuntime() { }
    }
}
internal static partial class Program
{
    private static void AudioChecks()
    {
        ArenaMode.Audio.HitmarkerPcmClip.Fail=false;
        UnityEngine.AudioListener.pause=false;
        ArenaMode.Pickups.ArenaPickupSettings.SoundVolume.Value=.7f;
        Ready(); int before=ArenaMode.Audio.ArenaMasterAudioOutput.Requests;
        Move(_local,0); Check(ArenaMode.Audio.ArenaMasterAudioOutput.Requests==before+1,"successful local collection plays once");
        Check(ArenaMode.Audio.HitmarkerPcmClip.LastPath==Path.Combine(Scratch,"ArenaMode","sounds","collect.wav"),
            "pickup cue uses the shared effects folder");
        Step(); Check(ArenaMode.Audio.ArenaMasterAudioOutput.Requests==before+1,"standing in cooldown cannot replay sound");
        Ready(); var bot=Bot("audio-bot",-3); Step(); before=ArenaMode.Audio.ArenaMasterAudioOutput.Requests;
        Move(bot,0); Check(ArenaMode.Audio.ArenaMasterAudioOutput.Requests==before,"bot collection is silent");
        Ready(ArenaMode.Pickups.ArenaPickupKind.Weapon);
        var weapon=ArenaMode.Pickups.ArenaWeaponPickup.Created.Single();
        before=ArenaMode.Audio.ArenaMasterAudioOutput.Requests;
        Move(_local,0); Check(ArenaMode.Audio.ArenaMasterAudioOutput.Requests==before,"pending transfer is silent");
        weapon.Settle(true); Check(ArenaMode.Audio.ArenaMasterAudioOutput.Requests==before+1,"confirmed transfer plays once");
        weapon.Callback(true); weapon.Callback(false);
        Check(ArenaMode.Audio.ArenaMasterAudioOutput.Requests==before+1,"duplicate callbacks never repeat audio");
        Ready(ArenaMode.Pickups.ArenaPickupKind.Weapon); weapon=ArenaMode.Pickups.ArenaWeaponPickup.Created.Single();
        before=ArenaMode.Audio.ArenaMasterAudioOutput.Requests; Move(_local,0); weapon.Settle(false,true);
        Check(ArenaMode.Audio.ArenaMasterAudioOutput.Requests==before,"failed transfer is silent");
        Ready(ArenaMode.Pickups.ArenaPickupKind.Weapon); weapon=ArenaMode.Pickups.ArenaWeaponPickup.Created.Single();
        Move(_local,0); Step(10); weapon.Callback(true);
        Check(ArenaMode.Audio.ArenaMasterAudioOutput.Requests==before,"late retired transfer is silent");
        Ready(); before=ArenaMode.Audio.ArenaMasterAudioOutput.Requests;
        ArenaMode.Pickups.ArenaPickupSettings.SoundVolume.Value=0; Move(_local,0);
        Check(ArenaMode.Audio.ArenaMasterAudioOutput.Requests==before,"mute still permits pickup without sound");
        ArenaMode.Pickups.ArenaPickupSettings.SoundVolume.Value=.7f;
        Ready(); UnityEngine.AudioListener.pause=true; Move(_local,0);
        Check(ArenaMode.Audio.ArenaMasterAudioOutput.Requests==before,"paused listener never queues pickup");
        UnityEngine.AudioListener.pause=false; Step();
        Check(ArenaMode.Audio.ArenaMasterAudioOutput.Requests==before,"unpause never replays an old pickup");
        Ready(); Move(_local,0); ArenaMode.Session.ArenaSession.IsActive=false; Step();
        Check(!ArenaMode.Audio.ArenaMasterAudioOutput.Playing,"raid exit stops owned pickup output");
        Ready(); before=ArenaMode.Audio.ArenaMasterAudioOutput.Requests; ArenaMode.Session.ArenaSession.Exact=false;
        ArenaMode.Pickups.ArenaPickupAudio.Collected(_local);
        Check(ArenaMode.Audio.ArenaMasterAudioOutput.Requests==before,"lost exact raid cannot emit late audio");
        Ready(); ArenaMode.Audio.HitmarkerPcmClip.Fail=true;
        int loads=ArenaMode.Audio.HitmarkerPcmClip.Loads;
        Move(_local,0); Step(11); Move(_local,-3); Move(_local,0);
        Check(ArenaMode.Audio.HitmarkerPcmClip.Loads==loads+1,"missing cue is attempted once per round");
        ArenaMode.Audio.HitmarkerPcmClip.Fail=false;
        Console.WriteLine("PICKUP_AUDIO_RUNTIME_PASSED");
    }
}
