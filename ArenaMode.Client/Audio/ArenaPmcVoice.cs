using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Diagnostics;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using Diz.DependencyManager;
using Diz.Resources;
using EFT;
using HarmonyLib;
using Redeploy.Core.Engine;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace ArenaMode.Audio;

/// <summary>
/// Automatic, low-priority PMC speech owned by one confirmed Arena match.
/// Neither profile faction nor customization changes. A TDM speaker alone gets
/// one retained faction voice, reapplied to each published replacement body.
/// </summary>
internal static class ArenaPmcVoice
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".PmcVoice");
    private static readonly FieldInfo SpeechSourceField = AccessTools.Field(typeof(Player), "_speechSource");
    private static bool _ready, _warned, _loadStarted;
    private static int _generation, _epoch, _usecCursor = -1, _bearCursor = -1;
    private static ArenaMatch _match;
    private static GameWorld _world;
    private static LocalGame _game;
    private static string _voice;
    private static ETagStatus _side;
    private static Voice _asset;
    private static DependencyGraph<IEasyBundle>.Token _token;
    private static TaskCompletionSource<bool> _cancelLoad;
    private static SpeakerLease _lease;
    private static Player _observedBody, _pendingBody;
    private static Player.FirearmController _hands;
    private static bool _wasReloading;
    private static EPhraseTrigger _pending = EPhraseTrigger.None;
    private static float _due, _expires, _lastSpoke = float.NegativeInfinity, _nextProbe;
    private static BaseSpeaker _playingSpeaker;
    private static Player _playingBody;
    private static BetterSource _playingNativeSource;
    private static AudioSource _playingSource;
    private static TaggedClip _playingClip;
    private static float _playingRelease, _playingQueuedDelay;
    private static EPhraseTrigger _playingQueued;

    private sealed class SpeakerLease
    {
        internal BaseSpeaker Speaker;
        internal string OriginalVoice;
        internal ETagStatus OriginalSide;
        internal KeyValuePair<EPhraseTrigger, TagBank>[] OriginalBanks;
        internal Dictionary<EPhraseTrigger, TagBank> AppliedBanks;
    }

    internal static void Enable()
    {
        if (_ready) return;
        try
        {
            var dry = AccessTools.DeclaredMethod(typeof(Player.FirearmController), nameof(Player.FirearmController.DryShot),
                new[] { typeof(int), typeof(bool) });
            var grenade = AccessTools.DeclaredMethod(typeof(Player.BaseGrenadeHandsController),
                nameof(Player.BaseGrenadeHandsController.CreateAndRegisterGrenade),
                new[] { typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(float) });
            if (dry == null || grenade == null || grenade.ReturnType != typeof(Grenade) || SpeechSourceField == null)
                throw new MissingMethodException("PMC voice action hooks");
            Patches.Patch(dry, postfix: new HarmonyMethod(typeof(ArenaPmcVoice), nameof(AfterDryShot)));
            Patches.Patch(grenade, postfix: new HarmonyMethod(typeof(ArenaPmcVoice), nameof(AfterGrenade)));
            RaidLifecycle.OnRaidEnded += Reset;
            _ready = true;
        }
        catch (Exception ex)
        {
            Patches.UnpatchSelf();
            Warn("hooks unavailable", ex);
        }
    }

    internal static void Shutdown()
    {
        if (_ready) RaidLifecycle.OnRaidEnded -= Reset;
        _ready = false;
        Patches.UnpatchSelf();
        Reset();
    }

    private static bool CurrentRaid(out LocalGame game, out GameWorld world, out ArenaMatch match)
    {
        game = null; world = null; match = null;
        if (!_ready || !ArenaSession.IsActive || !Singleton<GameWorld>.Instantiated
            || !Singleton<AbstractGame>.Instantiated) return false;
        world = Singleton<GameWorld>.Instance;
        game = Singleton<AbstractGame>.Instance as LocalGame;
        match = ArenaMatchService.Current;
        // Result presentation can outlive Current. Keep the bank retained until
        // this exact native raid ends: its last pain/death clip may still play.
        if (match == null && ReferenceEquals(_game, game) && ReferenceEquals(_world, world)) match = _match;
        return game != null && game.Status == GameStatus.Started && world != null && match != null
            && !ArenaMatchService.TeamStartAbortPending
            && ArenaSession.IsExactActiveRaid(game, world);
    }

    private static bool Current(out Player player)
    {
        player = null;
        if (Settings.PmcVoiceEnabled?.Value != true || !CurrentRaid(out var game, out var world, out var match)
            || !ReferenceEquals(_match, match) || !ReferenceEquals(_game, game) || !ReferenceEquals(_world, world)
            || !ReferenceEquals(ArenaMatchService.Current, match) || match.Mode.IsComplete
            || _epoch != ArenaSession.ArmEpoch || ArenaRespawnDriver.StandDown
            || ArenaRespawnDriver.IsRespawnInProgress || Areas.ArenaAreaRuntime.IsPending
            || ArenaSpectatorService.IsActive || !KillcamCompat.CameraReleased) return false;
        var body = world.MainPlayer;
        if (body == null || body.IsAI || !body.IsYourPlayer || body.HealthController?.IsAlive != true
            || !ReferenceEquals(game.LocalPlayer, body) || !ReferenceEquals(world.GetAlivePlayerByProfileID(body.ProfileId), body)
            || body.ProfileId != game.Profile?.Id || body.Speaker == null) return false;
        player = body;
        return true;
    }

    internal static void Tick()
    {
        try
        {
            if (!CurrentRaid(out var game, out var world, out var match)) { Reset(); return; }
            if (!ReferenceEquals(_match, match) || !ReferenceEquals(_game, game)
                || !ReferenceEquals(_world, world) || _epoch != ArenaSession.ArmEpoch)
            {
                Reset();
                _match = match; _game = game; _world = world; _epoch = ArenaSession.ArmEpoch;
                _match.OnKill += OnKill;
            }
            if (Settings.PmcVoiceEnabled?.Value != true)
            {
                CancelOwnedSpeech(); RestoreSpeaker(); ClearBody(); return;
            }
            if (!Current(out var body)) { ClearBody(); RestoreSpeaker(); return; }
            if (!ReferenceEquals(_observedBody, body))
            {
                RestoreSpeaker(); ClearBody(); _observedBody = body;
            }
            else if (_lease != null && !ReferenceEquals(_lease.Speaker, body.Speaker)) RestoreSpeaker();
            PrepareVoice();
            ApplyVoice(body);
            if (AnnouncerService.IsPmcVoiceBlocked) { CancelOwnedSpeech(); ClearCue(); }

            var hands = body.HandsController as Player.FirearmController;
            bool reloading = hands != null && ReferenceEquals(hands._player, body) && hands.IsInReloadOperation();
            // Native queries cover magazine, internal, revolver and UIFixes'
            // individual insert/remove operations without patching generic calls.
            if (reloading && (!ReferenceEquals(hands, _hands) || !_wasReloading))
                Queue(body, EPhraseTrigger.OnWeaponReload);
            _hands = hands; _wasReloading = reloading;
            Pump(body);
        }
        catch (Exception ex) { Warn("tick skipped", ex); ClearCue(); }
    }

    private static void PrepareVoice()
    {
        if (_loadStarted || _match.Mode.Id != ArenaModeCatalog.TeamDeathmatchId) return;
        var selected = ArenaSession.TeamDeathmatchLocalSide(_game, _world);
        if (selected != EPlayerSide.Usec && selected != EPlayerSide.Bear) return;
        bool usec = selected == EPlayerSide.Usec;
        _voice = usec
            ? ArenaPmcVoicePolicy.Rotate(true, ref _usecCursor, UnityEngine.Random.Range(0, ArenaPmcVoicePolicy.UsecVoices.Length))
            : ArenaPmcVoicePolicy.Rotate(false, ref _bearCursor, UnityEngine.Random.Range(0, ArenaPmcVoicePolicy.BearVoices.Length));
        _side = usec ? ETagStatus.Usec : ETagStatus.Bear;
        _loadStarted = true;
        LoadVoice(_generation, _voice, _game, _world, _match);
    }

    private static async void LoadVoice(int generation, string voice, LocalGame game, GameWorld world, ArenaMatch match)
    {
        DependencyGraph<IEasyBundle>.Token token = null;
        var cancelled = new TaskCompletionSource<bool>();
        _cancelLoad = cancelled;
        try
        {
            var assets = Singleton<IEasyAssets>.Instance;
            token = assets.Retain(new[] { InGameBundles.TakePhrasePath(voice) });
            var loading = EasyAssetsExtensions.LoadBundles(token);
            var completed = await Task.WhenAny(loading, cancelled.Task, Task.Delay(15000));
            if (completed != loading)
            {
                if (completed != cancelled.Task && generation == _generation)
                    Warn("faction voice timed out; keeping native voice", new TimeoutException(voice));
                // Observe any eventual native fault without retaining the raid
                // or player. Our token is released by this method's finally.
                _ = loading.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                return;
            }
            await loading;
            if (generation != _generation || !ReferenceEquals(_game, game) || !ReferenceEquals(_world, world)
                || !ReferenceEquals(_match, match) || !ArenaSession.IsExactActiveRaid(game, world)) return;
            if (!assets.TryGetAsset<Voice>(out var asset, InGameBundles.TakePhrasePath(voice)) || asset?.Banks == null)
                throw new InvalidOperationException("voice bank unavailable: " + voice);
            _token = token; token = null;
            _asset = asset;
            Probe("loaded " + voice);
        }
        catch (Exception ex)
        {
            if (generation == _generation) Warn("faction voice unavailable; keeping native voice", ex);
        }
        finally
        {
            if (ReferenceEquals(_cancelLoad, cancelled)) _cancelLoad = null;
            ReleaseToken(token);
        }
    }

    private static void ApplyVoice(Player body)
    {
        if (_asset == null || _lease != null || body.Speaker.Busy) return;
        var speaker = body.Speaker;
        var original = new KeyValuePair<EPhraseTrigger, TagBank>[speaker.PhrasesBanks.Count];
        ((ICollection<KeyValuePair<EPhraseTrigger, TagBank>>)speaker.PhrasesBanks).CopyTo(original, 0);
        var applied = new Dictionary<EPhraseTrigger, TagBank>();
        foreach (var bank in _asset.Banks)
            if (bank != null && !applied.ContainsKey(bank.Trigger)) applied.Add(bank.Trigger, bank);
        if (applied.Count == 0) return;
        _lease = new SpeakerLease { Speaker = speaker, OriginalVoice = speaker.PlayerVoice,
            OriginalSide = speaker.SideTag, OriginalBanks = original, AppliedBanks = applied };
        speaker.PhrasesBanks.Clear();
        foreach (var pair in applied) speaker.PhrasesBanks.Add(pair.Key, pair.Value);
        // ReplaceVoice alone does not set these two native fields.
        speaker.PlayerVoice = _voice;
        speaker.SideTag = _side;
        Probe("assigned " + _voice + " to body " + ArenaHitHistory.BodyId(body));
    }

    private static void RestoreSpeaker()
    {
        var lease = _lease; _lease = null;
        if (lease == null) return;
        var speaker = lease.Speaker;
        // Another mod may have taken over this exact speaker in the meantime.
        // Restore only the values that still form our complete assignment.
        if (speaker.PlayerVoice != _voice || speaker.SideTag != _side
            || speaker.PhrasesBanks.Count != lease.AppliedBanks.Count) return;
        foreach (var pair in lease.AppliedBanks)
            if (!speaker.PhrasesBanks.TryGetValue(pair.Key, out var value) || !ReferenceEquals(value, pair.Value)) return;
        speaker.PhrasesBanks.Clear();
        foreach (var pair in lease.OriginalBanks) speaker.PhrasesBanks.Add(pair.Key, pair.Value);
        speaker.PlayerVoice = lease.OriginalVoice;
        speaker.SideTag = lease.OriginalSide;
    }

    private static void AfterDryShot(Player.FirearmController __instance)
    {
        try
        {
            if (Current(out var body) && ReferenceEquals(body.HandsController, __instance)
                && ReferenceEquals(__instance._player, body)) Queue(body, EPhraseTrigger.OnOutOfAmmo);
        }
        catch (Exception ex) { Warn("empty weapon cue skipped", ex); }
    }

    private static void AfterGrenade(Player.BaseGrenadeHandsController __instance, Grenade __result)
    {
        try
        {
            if (__result != null && Current(out var body) && ReferenceEquals(body.HandsController, __instance)
                && ReferenceEquals(__instance._player, body)) Queue(body, EPhraseTrigger.OnGrenade);
        }
        catch (Exception ex) { Warn("grenade cue skipped", ex); }
    }

    private static void OnKill(ArenaKill kill)
    {
        try
        {
            if (kill == null || !kill.KilledByLocal || kill.LocalIsVictim || !kill.AwardEligible
                || !kill.ScoreEligible || kill.VictimIsFriendly == true || !Current(out var body)
                || kill.KillerBodyId != ArenaHitHistory.BodyId(body)
                || UnityEngine.Random.Range(0, 100) >= ArenaPmcVoicePolicy.KillChancePercent) return;
            Queue(body, EPhraseTrigger.EnemyDown, ArenaPmcVoicePolicy.KillDelaySeconds);
        }
        catch (Exception ex) { Warn("kill cue skipped", ex); }
    }

    private static void Queue(Player body, EPhraseTrigger phrase, float delay = 0f)
    {
        if (body.Speaker.QueuedEvent != EPhraseTrigger.PhraseNone
            || !ArenaPmcVoicePolicy.CanSpeak(Time.time, _lastSpoke, AnnouncerService.IsPmcVoiceBlocked, body.Speaker.Busy))
        { Probe("skipped " + phrase + " (cooldown, speaker or announcer)"); return; }
        // One slot: a grenade warning may replace low-priority confirmation.
        if (_pending != EPhraseTrigger.None && phrase != EPhraseTrigger.OnGrenade) return;
        _pending = phrase; _pendingBody = body; _due = Time.time + delay;
        _expires = Time.time + ArenaPmcVoicePolicy.CueLifetimeSeconds;
    }

    private static void Pump(Player body)
    {
        if (_pending == EPhraseTrigger.None) return;
        if (!ReferenceEquals(_pendingBody, body) || Time.time > _expires) { ClearCue(); return; }
        if (!ArenaPmcVoicePolicy.ValidCue(Time.time, _due, _expires, _pendingBody, body)) return;
        var phrase = _pending; ClearCue();
        var speaker = body.Speaker;
        if (speaker.QueuedEvent != EPhraseTrigger.PhraseNone
            || !ArenaPmcVoicePolicy.CanSpeak(Time.time, _lastSpoke, AnnouncerService.IsPmcVoiceBlocked, speaker.Busy)
            || !speaker.PhrasesBanks.TryGetValue(phrase, out var bank) || bank == null) return;
        // Native speech output, with its natural priority and health tags.
        // Do not force priority 200 over pain/death or create delayed native
        // queues that survive a body replacement. Like Smooth Talker this uses
        // Speaker.Play, not an extra SAIN Player.Say hearing injection.
        TaggedClip emitted = null;
        void Capture(EPhraseTrigger trigger, TaggedClip clip, TagBank emittedBank, BaseSpeaker emitter)
        {
            if (trigger == phrase && ReferenceEquals(emitter, speaker) && ReferenceEquals(emittedBank, bank)) emitted = clip;
        }
        TagBank played;
        speaker.OnPhraseTold += Capture;
        try { played = speaker.Play(phrase, body.HealthStatus | ETagStatus.Combat, true); }
        finally { speaker.OnPhraseTold -= Capture; }
        if (played == null) return;
        // Native speech callbacks belong to other mods too. They may have
        // retired the body or ended this match; never allocate/read its source
        // after that handover.
        if (!Current(out var current) || !ReferenceEquals(current, body)
            || !ReferenceEquals(body.Speaker, speaker) || emitted == null || !ReferenceEquals(speaker.Clip, emitted)) return;
        _lastSpoke = Time.time;
        _playingSpeaker = speaker; _playingClip = speaker.Clip; _playingRelease = speaker.ReleaseTime;
        _playingQueued = speaker.QueuedEvent; _playingQueuedDelay = speaker.QueuedDelay;
        _playingBody = body;
        // The public SpeechSource getter allocates a source after release.
        _playingNativeSource = SpeechSourceField.GetValue(body) as BetterSource;
        _playingSource = _playingNativeSource?.source1;
        Probe("played " + phrase + " voice=" + speaker.PlayerVoice, true);
    }

    private static void CancelOwnedSpeech()
    {
        var speaker = _playingSpeaker;
        if (_playingBody != null && ReferenceEquals(_playingBody.Speaker, speaker)
            && ReferenceEquals(SpeechSourceField.GetValue(_playingBody), _playingNativeSource)
            && ReferenceEquals(_playingNativeSource?.source1, _playingSource)
            && speaker != null && ReferenceEquals(speaker.Clip, _playingClip) && speaker.ReleaseTime == _playingRelease
            && _playingSource != null && ReferenceEquals(_playingSource.clip, _playingClip?.Clip))
        {
            _playingSource.Stop();
            speaker.ReleaseTime = Time.time;
            if (speaker.QueuedEvent == _playingQueued && speaker.QueuedDelay == _playingQueuedDelay)
                speaker.QueuedEvent = EPhraseTrigger.PhraseNone;
        }
        _playingBody = null; _playingNativeSource = null;
        _playingSpeaker = null; _playingSource = null; _playingClip = null;
    }

    private static void ClearCue() { _pending = EPhraseTrigger.None; _pendingBody = null; }
    private static void ClearBody()
    {
        CancelOwnedSpeech();
        ClearCue(); _observedBody = null; _hands = null; _wasReloading = false;
    }

    private static void Reset()
    {
        unchecked { _generation++; }
        if (_match != null) _match.OnKill -= OnKill;
        CancelOwnedSpeech(); RestoreSpeaker(); ClearBody();
        _match = null; _game = null; _world = null;
        var cancelled = _cancelLoad; _cancelLoad = null;
        cancelled?.TrySetResult(true);
        var token = _token; _token = null; _asset = null;
        _voice = null; _loadStarted = false; _warned = false;
        _lastSpoke = float.NegativeInfinity; _nextProbe = 0f;
        ReleaseToken(token);
    }

    private static void ReleaseToken(DependencyGraph<IEasyBundle>.Token token)
    {
        try { token?.Release(); }
        catch (Exception ex) { Warn("voice asset release failed", ex); }
    }

    private static void Probe(string message, bool played = false)
    {
        if (!ArenaLog.DeveloperEnabled || (!played && Time.time < _nextProbe)) return;
        _nextProbe = Time.time + 1f;
        ArenaLog.Developer("Arena PMC voice: " + message + ".");
    }

    private static void Warn(string context, Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena PMC voice " + context + ": " + ex.GetBaseException().Message);
    }
}
