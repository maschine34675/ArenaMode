using System.Text.RegularExpressions;

internal static class NativeContract
{
    internal static int Verify()
    {
        string directory = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(directory, "ArenaMode.Client")))
            directory = Directory.GetParent(directory)?.FullName ?? throw new Exception("Cannot locate checkout");
        string native = Path.GetFullPath(Path.Combine(directory, "..", "_decompiled"));
        string Read(string path) => File.ReadAllText(Path.Combine(native, path));
        int checks = 0;
        void Require(bool condition, string explanation)
        { checks++; if (!condition) throw new Exception("Native contract drift: " + explanation); }
        string player = Read("EFT/Player.cs");
        Require(Regex.IsMatch(player, @"public virtual void OnPhraseTold\(EPhraseTrigger @event, TaggedClip clip, TagBank bank, BaseSpeaker speaker\)\s*\{\s*PlayPhraseClip\(clip\);\s*\}"),
            "OnPhraseTold must remain audio only");
        Require(Regex.IsMatch(player, @"public void PlayPhraseClip\(TaggedClip clip\)\s*\{\s*PlaySpeechFromTime\(clip, 0f\);\s*\}"),
            "phrase playback is synchronous");
        string say = player[player.IndexOf("public virtual void Say(")..player.IndexOf("public void NeedRepairMalfPhraseSituation")];
        Require(say.IndexOf(".SayPhrase(this, phrase)") >= 0
            && say.IndexOf(".SayPhrase(this, phrase)") < say.IndexOf("Speaker.Play(phrase,"), "global phrase signal precedes speaker/audio");
        Require(player.Contains("protected BetterSource _speechSource;"), "non-creating backing field");
        Require(player.Contains("Speaker.OnPhraseTold += OnPhraseTold;"), "player event subscriber");
        string playback = player[player.IndexOf("public void PlaySpeechFromTime(")..player.IndexOf("public void PlayPhraseClip(")];
        Require(playback.Contains("if (!IsOutOfProtagonistsRange(SpeechSource.MaxDistance))"), "native range guard may decline playback");
        Require(playback.Contains("SpeechSource.Play(clip.Clip, null, 1f, volume, PointOfView.IsFirstPerson(), oneShot: false);"), "speech replaces source clip, not PlayOneShot");
        string speaker = Read("EFT/BaseSpeaker.cs");
        Require(speaker.IndexOf("ReleaseTime = Time.time + taggedClip.Length + 0.1f;")
            < speaker.IndexOf("_onPhraseTold?.Invoke(trigger, taggedClip, bank, this);"), "speaker becomes busy before audio subscriber");
        Require(speaker.Contains("Singleton<GameWorld>.Instance.SpeakerManager.FreeToSpeak(trigger, Id)"), "native limiter still precedes admission");
        Require(speaker.Contains("Queue(bank.ChainEvent.Event, tags, taggedClip.Length, demand: false);"), "native chains remain outside player callback");
        string simple = Read("SimpleSource.cs");
        Require(simple.Contains("if (!base.CanPlay)") && simple.Contains("source1.clip = clip1;") && simple.Contains("source1.Play();"), "native source may decline; real clip assigned synchronously");
        Require(Read("BetterAudio.cs").Contains("SourceGroups[6] = method_6<ReverbSimpleSource>(veryStandartMixerGroup, gameObject_3, preset8);"), "speech uses ReverbSimpleSource");
        string reverb = Read("Audio.ReverbSubsystem/ReverbSimpleSource.cs");
        Require(reverb.Contains("class ReverbSimpleSource : SimpleSource")
            && reverb.Contains("base.Play(clip1, clip2, balance, volume, forceStereo, oneShot);"), "speech reverb retains primary source playback");
        string attack = Read("InfectedAttackLayer.cs"), patrol = Read("InfectedPatrolLayer.cs");
        Require(attack.Contains("_talk = Time.time + MyExtensions.Random(1f, 3f);")
            && attack.Contains("_owner.BotTalk.Say(EPhraseTrigger.OnEnemyConversation, sayImmediately: true);"), "attack repetition trigger/cadence");
        Require(patrol.Contains("_talk = Time.time + MyExtensions.Random(1f, 3f);")
            && patrol.Contains("_owner.BotTalk.Say(EPhraseTrigger.OnMutter, sayImmediately: true);"), "patrol repetition trigger/cadence");
        string brain = Read("StandartBotBrain.cs");
        Require(Regex.IsMatch(brain, @"case WildSpawnType.infectedAssault:\s*case WildSpawnType.infectedPmc:\s*case WildSpawnType.infectedCivil:\s*case WildSpawnType.infectedLaborant:\s*BaseBrain = new InfectedLayersStrategy\(_owner\);"), "four generic native brain mapping");
        Console.WriteLine($"NATIVE_VOICE_CONTRACT_PASSED checks={checks}");
        return checks;
    }
}
