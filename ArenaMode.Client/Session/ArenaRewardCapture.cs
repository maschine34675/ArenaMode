using ArenaMode.Combat;
using ArenaMode.Modes;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Session;

/// <summary>
/// Collects native individual/body identities and fatal events while combat is
/// live. Money, restoration proof and payout authorization remain server-owned.
/// One immutable final upload is retried without changing its revision/content.
/// </summary>
internal static class ArenaRewardCapture
{
    private static ArenaMatch _match;
    private static GameWorld _world;
    private static LocalGame _game;
    private static RewardCaptureBuffer _buffer;
    private static RewardBeginRequest _selection;
    private static float _lastAt, _activeSeconds;
    private static bool _lastActive, _stopFrozen, _warned, _delivered;
    private static string _finalJson;
    private static bool _hooksReady;
    private static readonly Dictionary<long, (int Round, bool Administrative)> PendingDeaths = new();

    internal static void Enable()
    {
        try
        {
            new StopPatch().Enable();
            new GameEndPatch().Enable();
            new DeathPatch().Enable();
            _hooksReady = true;
        }
        catch (Exception e) { Warn("required capture hooks are unavailable; rewards remain disabled", e); }
    }

    internal static void Attach(ArenaMatch match)
    {
        Detach();
        _buffer = null; _selection = null; _finalJson = null;
        _delivered = _stopFrozen = _warned = _lastActive = false;
        _lastAt = Time.time; _activeSeconds = 0f;
        PendingDeaths.Clear();
        // Objective ownership has no server-verifiable reward proof yet. Do not
        // submit Domination under the kill-based TeamScore contract inherited
        // by its shared runtime class; existing reward whitelists stay strict.
        if (!_hooksReady || !ArenaSession.IsActive || match == null
            || match.Mode.Id == ArenaModeCatalog.DominationId
            || string.IsNullOrWhiteSpace(ArenaServerClient.RewardArmToken)) return;
        try
        {
            var state = ArenaRewardModeState.Read(match.Mode);
            var begin = new RewardBeginRequest {
                ArmToken = ArenaServerClient.RewardArmToken,
                NativeEndIdentity = ArenaServerClient.RewardNativeEndIdentity,
                Mode = match.Mode.Id, Rule = state.Rule, TimeLimitMilliseconds = state.Limit, KillTarget = state.Target
            };
            var reply = JsonConvert.DeserializeObject<RewardBeginResponse>(RequestHandler.PostJson(
                RewardWire.BeginRoute, JsonConvert.SerializeObject(begin)));
            if (reply?.Accepted != true || !Guid.TryParseExact(reply.RunId, "N", out _))
            {
                if (reply?.Reason != "rewards-not-enabled-or-enrolled")
                    Warn("reward capture was refused (" + (reply?.Reason ?? "invalid response") + ")", null);
                return;
            }
            ArenaRewardMenuState.CaptureProblem = null;
            _match = match;
            _world = Singleton<GameWorld>.Instance;
            _game = Singleton<AbstractGame>.Instance as LocalGame;
            _selection = begin;
            _buffer = new RewardCaptureBuffer(reply.RunId, begin.ArmToken, begin.NativeEndIdentity);
            match.OnAnnouncerCue += OnCue;
            Observe();
        }
        catch (Exception e) { Warn("reward capture could not start", e); }
    }

    internal static void Observe()
    {
        if (_buffer == null || _stopFrozen || _buffer.Packet.Final || _match == null
            || !ReferenceEquals(ArenaMatchService.Current, _match)) return;
        try
        {
            var state = ArenaRewardModeState.Read(_match.Mode);
            if (state.Failed) _buffer.Invalidate();
            if (state.Rule != _selection.Rule || state.Limit != _selection.TimeLimitMilliseconds || state.Target != _selection.KillTarget)
                _buffer.Invalidate();
            var now = Time.time;
            if (_lastActive) _activeSeconds += Mathf.Max(0f, now - _lastAt);
            _lastAt = now;
            if (state.Active && (_buffer.Current == null || _buffer.Current.Number != state.Number))
            {
                _buffer.BeginRound(state.Number);
                _activeSeconds = state.Elapsed >= 0 ? state.Elapsed : 0f;
            }
            if (_buffer.Current == null) { _lastActive = state.Active; return; }
            if (state.Elapsed >= 0 && (state.Active || _lastActive)) _activeSeconds = state.Elapsed;
            _buffer.Advance(Math.Max(Milliseconds(_activeSeconds), state.MinimumElapsedMilliseconds));
            foreach (var pending in PendingDeaths)
            {
                if (pending.Value.Round != _buffer.Current.Number) { _buffer.Invalidate(); continue; }
                if (_buffer.KnowsActor(pending.Key) && !_buffer.KnowsDeath(pending.Key))
                {
                    if (pending.Value.Administrative) _buffer.Invalidate();
                    else _buffer.Death(pending.Key, null, "Environment", _buffer.Current.ObservedDurationMilliseconds);
                }
            }
            PendingDeaths.Clear();
            _lastActive = state.Active;
            if (state.Active)
            {
                if (state.FixedRoster != null)
                {
                    if (state.FixedRoster.Count(x => x.Side == "Opponent") != 5
                        || state.FixedRoster.Any(x => x.Player == null)) _buffer.Invalidate();
                    foreach (var actor in state.FixedRoster) Register(actor.Player, actor.Side, true);
                }
                else
                {
                    foreach (var actor in _world.RegisteredPlayers)
                        if (actor is Player player && player.HealthController?.IsAlive == true) Register(player);
                    Register(_world.MainPlayer);
                }
            }
            else if (state.Finished) _buffer.CloseRound();
        }
        catch (Exception e) { _buffer.Invalidate(); Warn("combat evidence could not be captured", e); }
    }

    private static void Register(Player player, string explicitSide = null, bool initialRoster = false)
    {
        if (player == null || _buffer?.Current == null) return;
        string side = explicitSide;
        if (side == null)
        {
            if (_selection.Rule == "FreeForAllScore") side = "Individual";
            else if (player.ProfileId == _game.ProfileId) side = "Friendly";
            else if (_match.Mode is TeamDeathmatchMode team)
            { if (!team.TryRewardSide(player, out side)) return; }
            else if (_match.Mode is ZombieMode zombies && zombies.IsRewardFriendly(player)) side = "Friendly";
            else side = "Opponent";
        }
        _buffer.Register(ArenaHitHistory.BodyId(player), player.ProfileId, side,
            initialRoster ? 0 : _buffer.Current.ObservedDurationMilliseconds);
    }

    internal static void Kill(ArenaKill kill)
    {
        if (_buffer == null || _stopFrozen || _buffer.Packet.Final || kill == null) return;
        try
        {
            PendingDeaths.Remove(kill.VictimBodyId);
            var state = ArenaRewardModeState.Read(_match.Mode);
            if (!state.Active)
            {
                if (kill.LocalIsVictim && _buffer.Current != null && !_buffer.Current.Closed
                    && _buffer.KnowsActor(kill.VictimBodyId))
                {
                    // Native owner death may already have switched the mode to
                    // its result. Complete this same fatal event before closing
                    // the open round; no unrelated post-result kill is admitted.
                    if (_lastActive) _activeSeconds += Mathf.Max(0f, Time.time - _lastAt);
                    _lastAt = Time.time;
                    if (state.Elapsed >= 0) _activeSeconds = state.Elapsed;
                    _buffer.Advance(Milliseconds(_activeSeconds));
                    long? lastKiller = kill.KillerBodyId > 0 ? kill.KillerBodyId : (long?)null;
                    _buffer.Death(kill.VictimBodyId, lastKiller, lastKiller.HasValue ? "Combat" : "Environment",
                        _buffer.Current.ObservedDurationMilliseconds);
                    Observe();
                    return;
                }
                // Solo Tactical permits fighting while its other opponents are
                // still assembling. Such a roster cannot establish five ready
                // opponents at the common start required by the reward protocol.
                if (_match.Mode is TacticalMode tactical && tactical.CurrentPhase == TacticalMode.Phase.Live)
                    _buffer.Invalidate();
                return;
            }
            Observe();
            var round = _buffer.Current;
            if (round == null || round.Closed) return;
            var at = round.ObservedDurationMilliseconds;
            bool inclusive = _selection.Rule == "TeamElimination";
            if (_selection.TimeLimitMilliseconds > 0 && (inclusive
                ? at > _selection.TimeLimitMilliseconds : at >= _selection.TimeLimitMilliseconds)) return;
            // A spawn may be killed in the same frame before the normal poll.
            // Resolve the exact wrapper identity, never a reused profile slot.
            foreach (var actor in _world.RegisteredPlayers)
                if (actor is Player player && (ArenaHitHistory.BodyId(player) == kill.VictimBodyId
                    || ArenaHitHistory.BodyId(player) == kill.KillerBodyId)) Register(player);
            long? killer = kill.KillerBodyId > 0 ? kill.KillerBodyId : (long?)null;
            _buffer.Death(kill.VictimBodyId, killer, killer.HasValue ? "Combat" : "Environment", at);
        }
        catch (Exception e) { _buffer.Invalidate(); Warn("a fatal event could not be captured", e); }
    }

    internal static void LocalForfeit(Player player)
    {
        if (_buffer == null || _stopFrozen || _buffer.Packet.Final || player == null) return;
        Observe();
        Register(player, "Friendly");
        _buffer.Death(ArenaHitHistory.BodyId(player), null, "LocalForfeit",
            _buffer.Current?.ObservedDurationMilliseconds ?? 0);
    }

    internal static void NoticeLocalDeath(Player player)
    {
        if (_buffer == null || _stopFrozen || _buffer.Packet.Final || player == null) return;
        // Delay the environment fallback until the next observation: the native
        // aggressor callback can still arrive synchronously later in this frame.
        QueueDeath(player, administrative: false);
    }

    private static void QueueDeath(Player player, bool administrative)
    {
        if (_buffer?.Current == null || _stopFrozen || _buffer.Packet.Final || _buffer.Current.Closed) return;
        long body = ArenaHitHistory.BodyId(player);
        if (_buffer.KnowsDeath(body)) return;
        if (!_buffer.KnowsActor(body)) Register(player);
        if (_buffer.KnowsActor(body)) PendingDeaths[body] = (_buffer.Current.Number, administrative);
    }

    internal static void Invalidate(string reason)
    {
        if (_buffer == null || _buffer.Packet.Final) return;
        _buffer.Invalidate();
        Plugin.ModLogger.LogDebug("Arena reward capture withheld: " + reason + ".");
    }

    private static void OnCue(ArenaAnnouncerCue cue)
    { if (cue == ArenaAnnouncerCue.NoContest) Invalidate("match ended no contest"); }

    private static long Milliseconds(float seconds)
        => float.IsNaN(seconds) || float.IsInfinity(seconds) ? -1 : Math.Max(0L, (long)(seconds * 1000d));

    internal static void FlushFinal()
    {
        if (_buffer == null || _delivered) return;
        try
        {
            if (_finalJson == null)
            {
                if (!_stopFrozen) Observe();
                _stopFrozen = true;
                var final = _buffer.Finish();
                final.ColaState = Compat.ArenaTarkovaColaCompat.FinishCapture();
                _finalJson = JsonConvert.SerializeObject(final);
            }
            var reply = JsonConvert.DeserializeObject<RewardCaptureResponse>(
                RequestHandler.PostJson(RewardWire.CaptureRoute, _finalJson));
            _delivered = reply?.Accepted == true && reply.Revision == _buffer.Packet.Revision;
            if (_delivered && _buffer.Packet.State == "NoContest")
                ArenaRewardMenuState.CaptureProblem = "Last Arena match: no rewards (no contest or incomplete combat evidence).";
            if (!_delivered) Warn("the final result was not accepted (" + (reply?.Reason ?? "no response") + ")", null);
        }
        catch (Exception e) { Warn("the final result could not be sent; profile restoration continues", e); }
    }

    internal static void OnRaidEnded()
    {
        // Backstop only: GameEnd prefix normally publishes before native HTTP.
        FlushFinal();
        Detach();
    }

    private static void Detach()
    {
        if (_match != null) _match.OnAnnouncerCue -= OnCue;
        _match = null; _world = null; _game = null;
    }

    private static void Warn(string reason, Exception error)
    {
        if (_warned) return;
        _warned = true;
        ArenaRewardMenuState.CaptureProblem = "Last Arena match: rewards withheld because result capture was incomplete.";
        Plugin.ModLogger.LogWarning("Arena rewards: " + reason + (error == null ? "." : " (" + error.GetBaseException().Message + ")."));
    }

    private sealed class StopPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(LocalGame), nameof(LocalGame.Stop),
            new[] { typeof(string), typeof(ExitStatus), typeof(string), typeof(float) });

        [PatchPrefix, HarmonyPriority(Priority.Last)]
        private static void Prefix(LocalGame __instance, string profileId, bool __runOriginal)
        {
            // A blocked Killed-stop is a respawn, not a match end. Prefix order
            // preserves Core's decision and never changes native stop behavior.
            if (!__runOriginal || !ReferenceEquals(__instance, _game) || _buffer == null
                || profileId != __instance.ProfileId || __instance.Status != GameStatus.Started) return;
            Observe();
            _stopFrozen = true;
        }

        [PatchFinalizer]
        private static void Finalizer(LocalGame __instance, Exception __exception)
        {
            if (__exception != null && ReferenceEquals(__instance, _game) && _finalJson == null)
            { _stopFrozen = false; _lastAt = Time.time; }
        }
    }

    private sealed class GameEndPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "GameEnd");

        [PatchPrefix, HarmonyPriority(Priority.Last)]
        private static void Prefix(object __instance, bool __runOriginal)
        {
            if (__runOriginal && __instance is LocalGame game && ReferenceEquals(__instance, _game)
                && !Compat.ArenaTarkovaColaCompat.DeferFinalCapture(game)) FlushFinal();
        }
    }

    private sealed class DeathPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(Player), nameof(Player.OnDead), new[] { typeof(EDamageType) });

        [PatchPrefix]
        private static void Prefix(Player __instance, out bool __state)
        {
            __state = false;
            if (_buffer == null || _stopFrozen || _buffer.Packet.Final || _match == null || _game?.Status != GameStatus.Started) return;
            try { __state = ArenaRewardModeState.Read(_match.Mode).Active; }
            catch { _buffer.Invalidate(); }
        }

        [PatchPostfix]
        private static void Postfix(Player __instance, bool __state, bool __runOriginal)
        {
            if (!__state || !__runOriginal || __instance?.HealthController?.IsAlive != false) return;
            try
            {
                bool administrative = ArenaAdministrativeDeathGate.IsActive(__instance.ProfileId)
                    && !__instance.IsYourPlayer; // Local Give up has its own confirmed callback.
                QueueDeath(__instance, administrative);
            }
            catch (Exception e) { _buffer?.Invalidate(); Warn("a native death could not be captured", e); }
        }
    }
}
