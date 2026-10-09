#nullable disable
using ArenaMode.Compat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Admits the audio-only Player.OnPhraseTold body after native phrase dispatch,
/// speaker priority/busy checks and clip selection. Native OnRelease/chain/event
/// subscribers continue even when this one subscriber omits the clip playback.
/// Slots describe observed AudioSource playback, never BaseSpeaker.ReleaseTime:
/// the latter is already set for suppressed and out-of-hearing-range clips too.
/// </summary>
internal static class ZombieVoiceBudget
{
    // Includes unsuppressed baseline observations. Overflow fails open; the usual
    // enabled budget needs at most six records. No allocation on the phrase path.
    private const int ObservationCapacity = 128;
    private static readonly Voice[] Voices = new Voice[ObservationCapacity];
    private static AccessTools.FieldRef<Player, BetterSource> _speechSource;
    private static GameWorld _world;
    private static LocalGame _game;
    private static bool _enabled, _warned, _budgetEnabled;
    private static float _nextTick, _nextReport;
    private static long _candidates, _admitted, _played, _skipped, _wouldSkip, _exempt, _unplayed, _overflow;
    private static int _peak;

    private struct Voice
    {
        internal Player Body;
        internal BetterSource Source;
        internal AudioClip Clip;
    }

    internal struct Admission
    {
        internal Player Body;
        internal GameWorld World;
        internal LocalGame Game;
        internal TaggedClip Clip;
        internal int ActiveBefore;
        internal bool Track;
    }

    internal static void EnablePatch()
    {
        if (_enabled) return;
        try
        {
            // The property creates a source on access; read the existing field
            // instead so a refused clip does not create/prewarm/occlude one.
            _speechSource = AccessTools.FieldRefAccess<Player, BetterSource>("_speechSource");
            new PhrasePatch().Enable();
            _enabled = true;
            Plugin.ModLogger.LogInfo("Arena zombie voices: audio admission installed (6 repetitive voices, 4 distant; nearby attack calls exempt).");
        }
        catch (Exception ex) { Warn(ex); }
    }

    private sealed class PhrasePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(Player), nameof(Player.OnPhraseTold),
                new[] { typeof(EPhraseTrigger), typeof(TaggedClip), typeof(TagBank), typeof(BaseSpeaker) });

        [PatchPrefix]
        private static bool Prefix(Player __instance, EPhraseTrigger __0, TaggedClip __1,
            BaseSpeaker __3, out Admission __state)
            => BeforePhrase(__instance, __0, __1, __3, out __state);

        [PatchPostfix]
        private static void Postfix(Admission __state) => AfterPhrase(__state);
    }

    private static bool TryWorld(out GameWorld world, out LocalGame game)
    {
        world = null; game = null;
        if (!ZombieNativeEventBridge.IsActiveZombieRaid()
            || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated) return false;
        world = Singleton<GameWorld>.Instance;
        game = Singleton<AbstractGame>.Instance as LocalGame;
        return world != null && game != null && game.Status == GameStatus.Started
            && ArenaSession.IsExactActiveRaid(game, world);
    }

    private static bool GenericInfected(WildSpawnType role)
        => role == WildSpawnType.infectedAssault || role == WildSpawnType.infectedCivil
            || role == WildSpawnType.infectedLaborant || role == WildSpawnType.infectedPmc;

    private static bool IsCurrentBody(Player body, GameWorld world)
    {
        if (body == null || body.HealthController?.IsAlive != true || !body.IsAI || body.IsYourPlayer
            || ReferenceEquals(world.MainPlayer, body) || !ReferenceEquals(body.GameWorld, world)
            || string.IsNullOrEmpty(body.ProfileId)) return false;
        var settings = body.Profile?.Info?.Settings;
        if (settings == null || !GenericInfected(settings.Role)) return false;
        var owner = body.AIData?.BotOwner;
        return owner != null && !owner.IsDead && owner.BotState == EBotState.Active
            && ReferenceEquals(owner.GetPlayer, body)
            && owner.Brain?.BaseBrain?.GetType() == typeof(InfectedLayersStrategy)
            && !owner.IsFollower() && owner.BotFollower?.HaveBoss != true
            && !PitFireTeamCompat.IsTeamPlayer(body)
            && ReferenceEquals(world.GetAlivePlayerByProfileID(body.ProfileId), body);
    }

    private static bool TryListener(GameWorld world, out Player listener)
    {
        listener = world.MainPlayer;
        // A dead local PMC can spectate PFT companions at another position.
        // Keep native audio then rather than using the dead body's distance.
        return listener != null && listener.IsYourPlayer && listener.HealthController?.IsAlive == true
            && ReferenceEquals(listener.GameWorld, world)
            && !string.IsNullOrEmpty(listener.ProfileId)
            && ReferenceEquals(world.GetAlivePlayerByProfileID(listener.ProfileId), listener);
    }

    internal static bool BeforePhrase(Player body, EPhraseTrigger phrase, TaggedClip clip,
        BaseSpeaker speaker, out Admission admission)
    {
        admission = default;
        try
        {
            if (!TryWorld(out var world, out var game)) return true;
            Bind(world, game);
            // All critical and unrecognized phrases bypass admission. Their
            // replacement of a tracked clip is observed through source identity.
            if (!IsCurrentBody(body, world) || !ReferenceEquals(body.Speaker, speaker)) return true;
            if (phrase != EPhraseTrigger.OnMutter && phrase != EPhraseTrigger.OnEnemyConversation)
            { _exempt++; return true; }
            if (clip?.Clip == null || !TryListener(world, out var listener)) return true;
            float distance = (body.Position - listener.Position).sqrMagnitude;
            if (float.IsNaN(distance) || float.IsInfinity(distance)) return true;
            _candidates++;
            if (ZombieVoiceBudgetPolicy.IsThreatExempt(phrase == EPhraseTrigger.OnEnemyConversation, distance))
            { _exempt++; return true; }

            CountVoices(world, listener, body, out int active, out int distant);
            bool room = ZombieVoiceBudgetPolicy.HasRoom(active, distant,
                distance >= ZombieVoiceBudgetPolicy.NearbyDistanceSquared);
            if (!room)
            {
                _wouldSkip++;
                if (_budgetEnabled) { _skipped++; return false; }
            }
            _admitted++;
            // Synchronous original call: only its postfix can publish a slot,
            // after Unity confirms this specific clip really started.
            admission = new Admission { Body = body, World = world, Game = game, Clip = clip, ActiveBefore = active, Track = true };
            return true;
        }
        catch (Exception ex) { admission = default; Warn(ex); return true; }
    }

    internal static void AfterPhrase(Admission admission)
    {
        if (!admission.Track) return;
        try
        {
            if (!TryWorld(out var world, out var game) || !ReferenceEquals(world, admission.World)
                || !ReferenceEquals(game, admission.Game) || !ReferenceEquals(_world, world)
                || !IsCurrentBody(admission.Body, world)) return;
            var source = _speechSource(admission.Body);
            var audio = source == null ? null : source.source1;
            if (audio == null || !audio.isPlaying || !ReferenceEquals(audio.clip, admission.Clip.Clip))
            { _unplayed++; return; }
            int available = -1;
            for (int i = 0; i < Voices.Length; i++)
            {
                if (ReferenceEquals(Voices[i].Body, admission.Body)) { available = i; break; }
                if (available < 0 && ReferenceEquals(Voices[i].Body, null)) available = i;
            }
            if (available < 0) { _overflow++; return; }
            Voices[available] = new Voice { Body = admission.Body, Source = source, Clip = audio.clip };
            _played++;
            if (admission.ActiveBefore + 1 > _peak) _peak = admission.ActiveBefore + 1;
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static void CountVoices(GameWorld world, Player listener, Player replacing,
        out int active, out int distant)
    {
        active = distant = 0;
        for (int i = 0; i < Voices.Length; i++)
        {
            var voice = Voices[i];
            if (ReferenceEquals(voice.Body, null)) continue;
            if (!IsCurrentBody(voice.Body, world) || voice.Source == null
                || !ReferenceEquals(_speechSource(voice.Body), voice.Source)
                || voice.Source.source1 == null || !voice.Source.source1.isPlaying
                || !ReferenceEquals(voice.Source.source1.clip, voice.Clip))
            { Voices[i] = default; continue; }
            // A source replaced synchronously by its own next clip uses one slot.
            // Do not clear its record before playback: native range/CanPlay may
            // decline the new clip while the old source continues playing.
            if (ReferenceEquals(voice.Body, replacing)) continue;
            active++;
            if ((voice.Body.Position - listener.Position).sqrMagnitude >= ZombieVoiceBudgetPolicy.NearbyDistanceSquared)
                distant++;
        }
        if (active > _peak) _peak = active;
    }

    private static void Bind(GameWorld world, LocalGame game)
    {
        bool budget = Config.Settings.ZombieVoiceBudgetEnabled?.Value ?? true;
        if (!ReferenceEquals(_world, world) || !ReferenceEquals(_game, game))
        {
            Reset();
            _world = world; _game = game; _budgetEnabled = budget;
            _nextTick = Time.realtimeSinceStartup;
            _nextReport = Time.realtimeSinceStartup + 30f;
        }
        else if (_budgetEnabled != budget)
        {
            Report(); ClearCounters(); _budgetEnabled = budget;
            _nextReport = Time.realtimeSinceStartup + 30f;
        }
    }

    /// <summary>Call from ArenaController.Update, including its inactive branch.</summary>
    internal static void Tick()
    {
        if (!_enabled) return;
        try
        {
            if (!TryWorld(out var world, out var game))
            {
                if (!ReferenceEquals(_world, null)) Reset();
                return;
            }
            Bind(world, game);
            float now = Time.realtimeSinceStartup;
            if (now < _nextTick) return;
            _nextTick = now + 0.5f;
            if (TryListener(world, out var listener)) CountVoices(world, listener, null, out _, out _);
            else Array.Clear(Voices, 0, Voices.Length);
            if (now >= _nextReport) { Report(); ClearCounters(); _nextReport = now + 30f; }
        }
        catch (Exception ex) { Warn(ex); }
    }

    internal static void Reset()
    {
        if (!ReferenceEquals(_world, null)) Report();
        Array.Clear(Voices, 0, Voices.Length);
        _world = null; _game = null; _nextTick = _nextReport = 0f; _warned = false;
        ClearCounters();
    }

    private static void ClearCounters()
    {
        _candidates = _admitted = _played = _skipped = _wouldSkip = _exempt = _unplayed = _overflow = 0;
        _peak = 0;
    }

    private static void Report()
    {
        if (_candidates == 0 && _exempt == 0 && _overflow == 0) return;
        Plugin.ModLogger.LogInfo($"Arena zombie voices: enabled={_budgetEnabled}; candidates={_candidates}, admitted={_admitted}, played={_played}, skipped={_skipped}, wouldSkip={_wouldSkip}, exempt={_exempt}, nativeUnplayed={_unplayed}, observedPeak={_peak}, overflow={_overflow} (30s window or boundary; baseline wouldSkip uses actual occupancy).");
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning($"Arena zombie voices: admission unavailable, native audio retained ({ex.Message}).");
    }
}
