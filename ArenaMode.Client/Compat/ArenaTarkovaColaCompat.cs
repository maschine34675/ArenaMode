using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using ArenaMode.Session;
using ArenaMode.Shared;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Counters;
using EFT.HealthSystem;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Redeploy.Core.Api;
using SPT.Common.Http;
using UnityEngine;

namespace ArenaMode.Compat;

/// <summary>
/// Keeps Cola's existing Arena gameplay, but binds its mutable client state to a
/// real Arena arm and physical body. Server code independently owns preservation
/// and validation; this class never claims that a client snapshot grants rewards.
/// </summary>
internal static class ArenaTarkovaColaCompat
{
    private const string Owner = "com.maschine.ArenaMode.tarkovacola";
    private static Contract _contract;
    private static Harmony _harmony;
    private static string _failure;
    private static bool _warnedForward, _allowEnd, _allowAward, _rebindInitialization;
    private static TarkovaColaClientLease _lease;
    private static JObject _baseline;
    private static bool _baselineDirty;
    private static float _baselineSaveAt;
    private static object[] _refunds, _native, _claims;
    private static string _offering;
    private static Profile _lastProfile;
    private static string _captured;
    // Tag the exact request closure and produced JSON, never a reusable container.
    // Weak keys avoid retaining old worlds. Only Cola's three request boundaries
    // and reset share this lock; no Task/global callback interception is used.
    private static readonly object RerollSync = new();
    private static readonly ConditionalWeakTable<object, RerollOwner> RerollClosures = new();
    private static readonly ConditionalWeakTable<JObject, RerollResponse> RerollResponses = new();
    [ThreadStatic] private static RerollOwner _rerollRequestOwner;

    internal static void Initialize() => GetRejectionReason();

    internal static string GetRejectionReason()
    {
        if (!Chainloader.PluginInfos.TryGetValue(TarkovaColaPolicy.PluginGuid, out var info)) return null;
        if (_failure != null) return _failure;
        if (_contract != null) return null;
        try
        {
            if (info?.Instance == null) return "Tarkova Cola has not finished loading.";
            if (!TarkovaColaPolicy.SupportedVersion(info.Metadata.Version))
                return "Arena requires Tarkova Cola 2.2.1 or newer.";
            var contract = new Contract(info.Instance.GetType().Assembly);
            var harmony = new Harmony(Owner);
            try
            {
                void Patch(MethodBase target, string prefix = null, string postfix = null, int priority = Priority.Normal, string finalizer = null)
                {
                    HarmonyMethod Method(string name) => name == null ? null
                        : new HarmonyMethod(typeof(ArenaTarkovaColaCompat).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)) { priority = priority };
                    harmony.Patch(target, Method(prefix), Method(postfix), finalizer: Method(finalizer));
                }
                Patch(contract.Flush, nameof(PersistPrefix));
                Patch(contract.FlushSync, nameof(PersistPrefix));
                Patch(contract.EnsureLoaded, nameof(EnsureLoadedPrefix));
                Patch(contract.ServerSafe.GetMethod, nameof(ServerSafePrefix));
                Patch(contract.SendRefund, nameof(PersistPrefix));
                Patch(contract.SendClaim, nameof(PersistPrefix));
                Patch(contract.UnlockNative, nameof(NativeUnlockPrefix));
                Patch(contract.TrackerTick, nameof(TrackerPrefix), nameof(TrackerPostfix));
                Patch(contract.AchReady, nameof(PlayerReadyPrefix));
                Patch(contract.ClassicReady, nameof(PlayerReadyPrefix));
                Patch(contract.EternalReady, nameof(PlayerReadyPrefix));
                if (contract.MachinesReady != null) Patch(contract.MachinesReady, nameof(PlayerReadyPrefix));
                foreach (var method in contract.ProtectedMutations) Patch(method, nameof(PersistPrefix));
                if (contract.RerollRequest != null)
                {
                    Patch(contract.RerollRequest, nameof(RerollRequestPrefix), finalizer: nameof(RerollRequestFinalizer));
                    Patch(contract.RerollClosureCtor, postfix: nameof(RerollClosurePostfix));
                    Patch(contract.RerollReset, nameof(RerollResetPrefix), finalizer: nameof(RerollLockFinalizer));
                    Patch(contract.RerollApply, nameof(RerollApplyPrefix), finalizer: nameof(RerollLockFinalizer));
                    Patch(contract.RerollCallback, nameof(RerollCallbackPrefix), nameof(RerollCallbackPostfix), finalizer: nameof(RerollCallbackFinalizer));
                }
                Patch(contract.ArenaTick, nameof(ArenaTickPrefix));
                Patch(contract.ArenaStart, postfix: nameof(ArenaStartPostfix));
                Patch(contract.ModeId, nameof(ModePrefix));
                Patch(contract.ArenaNow.GetMethod, nameof(ArenaNowPrefix));
                Patch(contract.RaidEnd, nameof(RaidEndPrefix));
                Patch(contract.Award, nameof(AwardPrefix));
                Patch(contract.DrinkPlay, nameof(DrinkPlayPrefix));
                Patch(contract.IsMe, nameof(IsMePrefix));
                Patch(contract.DrinkPostfix, nameof(DrinkPostfixPrefix));
                Patch(typeof(GameWorld).GetMethod(nameof(GameWorld.OnGameStarted)), nameof(GameStartedPrefix), priority: Priority.First);
                foreach (var type in typeof(BaseStatisticsManager).Assembly.GetTypes().Where(t => typeof(BaseStatisticsManager).IsAssignableFrom(t)))
                {
                    var end = type.GetMethod(nameof(BaseStatisticsManager.EndStatisticsSession), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                        null, new[] { typeof(ExitStatus), typeof(float) }, null);
                    if (end != null && !end.IsAbstract) Patch(end, postfix: nameof(StatisticsEndedPostfix), priority: Priority.Last);
                }
                Patch(typeof(Player).GetMethod(nameof(Player.OnGameSessionEnd), new[] { typeof(ExitStatus), typeof(float), typeof(string), typeof(string) }),
                    postfix: nameof(PlayerSessionEndedPostfix), priority: Priority.Last);
                Patch(typeof(BaseStatisticsManager).GetMethod(nameof(BaseStatisticsManager.BeginStatisticsSession), Type.EmptyTypes),
                    prefix: nameof(StatisticsBeginningPrefix), priority: Priority.First);
                _contract = contract; _harmony = harmony;
                RespawnApi.Replaced += OnReplaced;
                RespawnApi.ReplacementFailed += OnReplacementFailed;
                RespawnApi.DeathClaimed += OnDeathClaimed;
            }
            catch { harmony.UnpatchSelf(); throw; }
            if (!_warnedForward && info.Metadata.Version > new System.Version(TarkovaColaPolicy.ReviewedVersion))
            {
                _warnedForward = true;
                Plugin.ModLogger.LogWarning("Tarkova Cola is newer than the reviewed " + TarkovaColaPolicy.ReviewedVersion + " build; required Arena contracts were found.");
            }
            return null;
        }
        catch (Exception exception)
        {
            _failure = "Tarkova Cola's Arena compatibility contract could not be verified (" + exception.GetBaseException().GetType().Name + ").";
            return _failure;
        }
    }

    /// <summary>
    /// Called BEFORE the Arena arm request. The server compares the returned exact
    /// state with its file before taking ownership, catching older async writes.
    /// This deliberately sends synchronously even when Cola already cleared dirty.
    /// </summary>
    internal static string PrepareForArm()
    {
        var problem = GetRejectionReason();
        if (problem != null) throw new InvalidOperationException(problem);
        if (_contract == null) return null;
        if (_lease != null) throw new InvalidOperationException("A previous Cola Arena lease is still open.");
        var state = (JObject)_contract.State.GetValue(null);
        var json = Serialize(state);
        var body = new JObject { ["json"] = json }.ToString(Formatting.None);
        var reply = JObject.Parse(RequestHandler.PostJson("/tarkovacola/state/save", body));
        if ((string)reply["json"] != "ok") throw new InvalidOperationException("Tarkova Cola did not confirm the pre-Arena save.");
        _contract.Dirty.SetValue(null, false);
        return json;
    }

    /// <summary>After successful server arm and MarkArmed, before GameWorld.OnGameStarted.</summary>
    internal static void PrepareRaid(string profileId, string mode, int armEpoch)
    {
        if (_contract == null) return;
        if (_lease != null) throw new InvalidOperationException("A previous Cola Arena lease is still open.");
        if (!ArenaSession.ArmedForNextRaid || ArenaSession.ArmEpoch != armEpoch
            || !string.Equals(mode, ArenaSession.ArmedMode, StringComparison.Ordinal))
            throw new InvalidOperationException("Cola protection requires the exact accepted Arena arm.");
        _baseline = ((JObject)_contract.State.GetValue(null)).DeepClone() as JObject;
        _baselineDirty = (bool)_contract.Dirty.GetValue(null);
        _baselineSaveAt = (float)_contract.SaveAt.GetValue(null);
        _refunds = Snapshot(_contract.PendingRefunds);
        _native = Snapshot(_contract.PendingNative);
        _claims = Snapshot(_contract.PendingClaims);
        CaptureOffering();
        _lease = new TarkovaColaClientLease(profileId, ArenaSession.ArmedMap, mode, armEpoch);
        _lastProfile = null; _captured = null; _rebindInitialization = false;
        try
        {
            // The protected server shadow includes already signed progression
            // from earlier matches in this process. It never replaces the normal
            // file. Later foreign force-loads cannot overwrite this live match.
            var response = JObject.Parse(RequestHandler.PostJson("/tarkovacola/state/get", "{}"));
            var shadow = JObject.Parse((string)response["json"] ?? throw new InvalidOperationException("Cola shadow was missing."));
            Serialize(shadow);
            _contract.StateField.SetValue(null, shadow);
            _contract.Dirty.SetValue(null, false);
        }
        catch (Exception exception) { Fail(exception); throw; }
    }

    internal static void BeginConfirmedRaid(GameWorld world)
    {
        if (_contract == null) return;
        if (_lease == null) throw new InvalidOperationException("Cola protection was not armed.");
        var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        if (game == null || !ArenaSession.IsExactActiveRaid(game, world)
            || !_lease.Confirm(world, game.ProfileId, ArenaSession.ActiveMode, ArenaSession.ArmEpoch))
            throw new InvalidOperationException("Cola protection did not match the confirmed Arena raid.");
        ObserveBody(world);
        Invoke(_contract.ArenaTick);
    }

    internal static void Tick()
    {
        if (_lease == null || _lease.Finished || _lease.Failed) return;
        try
        {
            var world = CurrentWorld();
            if (_lease.CanObserve(world)) ObserveBody(world);
        }
        catch (Exception exception) { Fail(exception); }
    }

    // GameEnd's prefix precedes native XP calculation. Keep combat frozen there,
    // but wait for the real player's statistics before serializing Cola rewards.
    // Never call native EndStatisticsSession ourselves: it mutates the PMC.
    internal static bool DeferFinalCapture(LocalGame game)
    {
        if (_lease == null) return false;
        try
        {
            var world = CurrentWorld();
            if (!ArenaSession.IsExactActiveRaid(game, world)) throw new InvalidOperationException("Cola final game changed.");
            ObserveExperience(_lease.Body as Player); // before native extraction XP is added
            if (!_lease.BeginFinal(world)) throw new InvalidOperationException("Cola final statistics did not match its Arena raid.");
        }
        catch (Exception exception) { Fail(exception); }
        return true;
    }

    private static void StatisticsBeginningPrefix(BaseStatisticsManager __instance)
    {
        if (!Blocks || !RespawnApi.IsReplacing || !_lease.CanObserve(CurrentWorld())) return;
        var next = __instance.Player;
        if (next == null || !next.IsYourPlayer || ReferenceEquals(next, _lease.Body)) return;
        try
        {
            if (next.ProfileId != _lease.ProfileId || !ReferenceEquals(next.Profile, _lastProfile)
                || !_lease.BeforeReplacementStatistics(CurrentWorld(), _lease.Body, next,
                    next.Profile.EftStats.SessionCounters.GetAllLong(CounterTag.Exp)))
                throw new InvalidOperationException("Cola replacement statistics changed their owner.");
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void StatisticsEndedPostfix(BaseStatisticsManager __instance, bool __runOriginal)
    {
        if (__runOriginal && Blocks) _lease.ObserveStatisticsEnd(CurrentWorld(), __instance.Player);
    }

    private static void PlayerSessionEndedPostfix(Player __instance, bool __runOriginal)
    {
        if (!__runOriginal || !Blocks || !_lease.FinalRequested || !_lease.CanApply(CurrentWorld(), __instance)) return;
        _lastProfile = __instance.Profile;
        ArenaRewardCapture.FlushFinal(); // before native GameEnd cleanup and backend serialization
    }

    /// <summary>Before reporting the final result/native end. Never sends or grants anything.</summary>
    internal static string FinishCapture()
    {
        if (_lease == null) return null;
        if (_captured != null) return TarkovaColaPolicy.RewardsAllowed(_lease.Mode) ? _captured : null;
        if (_lease.Failed) throw new InvalidOperationException(_failure ?? "Cola capture failed.");
        if (!_lease.Confirmed) return null;
        try
        {
            if (!_lease.FinalRequested || !_lease.StatisticsCompleted)
                throw new InvalidOperationException("Cola research requires the actual final native statistics.");
            _allowAward = true;
            // Arena ends through Left (native multiplier zero). Research uses
            // earned session counters across exact physical lives, without an
            // extraction bonus/penalty. Cola still owns its 25% Arena scaling
            // and configured research bonus; no PMC statistics are rewritten.
            Invoke(_contract.Award, "Arena earned session XP", null, _lease.EarnedExperience);
            _allowEnd = true;
            Invoke(_contract.ArenaEnd);
            var json = Serialize((JObject)_contract.StateField.GetValue(null));
            if (!_lease.Finish()) throw new InvalidOperationException("Cola result was already closed.");
            _captured = json;
            return TarkovaColaPolicy.RewardsAllowed(_lease.Mode) ? json : null;
        }
        catch (Exception exception) { Fail(exception); throw; }
        finally { _allowAward = false; _allowEnd = false; }
    }

    /// <summary>Real end/cancel only. Drop Arena-created queues, keep preexisting ordinary work.</summary>
    internal static void EndRaid()
    {
        if (_lease == null) return;
        bool restored = false;
        try
        {
            // Rewards can be disabled, so FinishCapture is not guaranteed to
            // have run. Close the native fallback before releasing protection:
            // Tracker's next null-body tick must not award the old Arena XP to
            // the restored ordinary state. An arm cancelled before world start
            // never owned a native statistics session and leaves it untouched.
            if (_lease.World != null) _contract.Awarded.SetValue(null, true);
            _contract.StateField.SetValue(null, _baseline.DeepClone());
            _contract.Dirty.SetValue(null, _baselineDirty);
            _contract.SaveAt.SetValue(null, _baselineSaveAt);
            Restore(_contract.PendingRefunds, _refunds);
            Restore(_contract.PendingNative, _native);
            Restore(_contract.PendingClaims, _claims);
            RestoreOffering();
            ClearAnimator();
            Invoke(_contract.ClearActive);
            foreach (var reset in _contract.NewPerkCleanup) Invoke(reset);
            Invoke(_contract.EternalStart);
            Invoke(_contract.Refresh);
            restored = true;
        }
        // A failed optional cleanup must not prevent Arena's own full-profile,
        // world, input and camera cleanup. Retain this lease's write barrier.
        catch (Exception exception) { Fail(exception); }
        finally
        {
            if (restored)
            {
                _lease.Release(); _lease = null; _baseline = null;
                _refunds = _native = _claims = null; _offering = null; _lastProfile = null; _captured = null;
                _rebindInitialization = false;
            }
            _allowEnd = _allowAward = false;
        }
    }

    internal static void Shutdown()
    {
        EndRaid();
        if (_lease != null) return; // Preserve failed-cleanup write barriers until process exit.
        RespawnApi.Replaced -= OnReplaced;
        RespawnApi.ReplacementFailed -= OnReplacementFailed;
        RespawnApi.DeathClaimed -= OnDeathClaimed;
        _harmony?.UnpatchSelf(); _harmony = null; _contract = null;
    }

    private static bool Blocks => _lease?.BlocksWrites == true;
    private static GameWorld CurrentWorld() => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
    private static bool PersistPrefix() => !Blocks;
    private static void CaptureOffering() => _offering = _contract.PendingOffering?.GetValue(null) as string;
    private static void RestoreOffering() => _contract.PendingOffering?.SetValue(null, _offering);

    private static bool EnsureLoadedPrefix() => !Blocks;
    private static bool NativeUnlockPrefix(ref bool __result) { if (!Blocks) return true; __result = false; return false; }
    private static bool ServerSafePrefix(ref bool __result) { if (!Blocks) return true; __result = false; return false; }
    private static bool ArenaNowPrefix(ref bool __result) { if (!Blocks) return true; __result = true; return false; }
    private static bool ModePrefix(ref string __result) { if (!Blocks) return true; __result = _lease.Mode; return false; }
    private static bool RaidEndPrefix() => !Blocks || _allowEnd;
    private static bool AwardPrefix() => !Blocks || _allowAward;
    private static bool ArenaTickPrefix() => !Blocks || (!_lease.Failed && !_lease.Finished);
    private static bool PlayerReadyPrefix() => !Blocks || !_rebindInitialization;
    private sealed class RerollOwner
    {
        internal TarkovaColaClientLease Lease;
        internal GameWorld World;
        internal Player Body;
    }
    private sealed class RerollRequestState { internal RerollOwner Previous; }
    private sealed class RerollResponse
    {
        internal RerollOwner Owner;
        internal object Container;
    }
    private sealed class RerollCallbackState
    {
        internal RerollOwner Owner;
        internal object Container;
        internal JObject Previous;
    }
    private static bool RerollRequestPrefix(out RerollRequestState __state)
    {
        __state = null;
        if (!Blocks) return true;
        System.Threading.Monitor.Enter(RerollSync);
        __state = new RerollRequestState { Previous = _rerollRequestOwner };
        var world = CurrentWorld(); var body = world?.MainPlayer;
        if (!TarkovaColaPolicy.AllowsDelayedAction(_lease, _lease, world, body, body,
            RespawnApi.IsReplacing, body?.HealthController?.IsAlive == true)) return false;
        _rerollRequestOwner = new RerollOwner { Lease = _lease, World = world, Body = body };
        return true;
    }
    private static void RerollRequestFinalizer(RerollRequestState __state)
    {
        if (__state == null) return;
        _rerollRequestOwner = __state.Previous;
        System.Threading.Monitor.Exit(RerollSync);
    }
    private static void RerollClosurePostfix(object __instance)
    {
        if (_rerollRequestOwner != null) RerollClosures.Add(__instance, _rerollRequestOwner);
    }
    private static void RerollResetPrefix(out bool __state)
    {
        __state = Blocks;
        if (__state) System.Threading.Monitor.Enter(RerollSync);
    }
    private static void RerollLockFinalizer(bool __state)
    {
        if (__state) System.Threading.Monitor.Exit(RerollSync);
    }
    private static bool RerollCallbackPrefix(object __instance, out RerollCallbackState __state)
    {
        __state = null;
        if (!RerollClosures.TryGetValue(__instance, out var owner)) return !Blocks;
        System.Threading.Monitor.Enter(RerollSync);
        __state = new RerollCallbackState { Owner = owner };
        // This is a Task continuation. Read only managed lease identities here;
        // Unity/world/health and replacement checks belong to main-thread Apply.
        // A failed replacement with the old body retained must still run Cola's
        // finally (_busy=false), rather than leave its requests permanently busy.
        if (!ReferenceEquals(_lease, owner.Lease) || owner.Lease.FinalRequested
            || !owner.Lease.CanApply(owner.World, owner.Body)) return false;
        __state.Container = _contract.RerollContainer.GetValue(__instance);
        __state.Previous = (JObject)_contract.RerollPending.GetValue(null);
        return true;
    }
    private static void RerollCallbackPostfix(bool __runOriginal, RerollCallbackState __state)
    {
        if (!__runOriginal || __state == null) return;
        var pending = (JObject)_contract.RerollPending.GetValue(null);
        if (pending != null && !ReferenceEquals(pending, __state.Previous)
            && ReferenceEquals(_contract.RerollPendingFor.GetValue(null), __state.Container))
            RerollResponses.GetValue(pending, _ => new RerollResponse { Owner = __state.Owner, Container = __state.Container });
    }
    private static void RerollCallbackFinalizer(RerollCallbackState __state)
    {
        if (__state != null) System.Threading.Monitor.Exit(RerollSync);
    }
    private static bool RerollApplyPrefix(JObject __0, object __1, out bool __state)
    {
        __state = false;
        if (!Blocks && (__0 == null || !RerollResponses.TryGetValue(__0, out _))) return true;
        System.Threading.Monitor.Enter(RerollSync); __state = true;
        if (__0 == null || !RerollResponses.TryGetValue(__0, out var response)) return !Blocks;
        // Upstream Tick reads pending JSON/container separately. A concurrent
        // callback must never redirect an earlier response into another crate.
        if (!ReferenceEquals(__1, response.Container)) return false;
        var owner = response.Owner;
        var world = CurrentWorld();
        return ReferenceEquals(world, owner.World) && TarkovaColaPolicy.AllowsDelayedAction(_lease, owner.Lease,
            world, world?.MainPlayer, owner.Body, RespawnApi.IsReplacing, owner.Body?.HealthController?.IsAlive == true);
    }
    private static void TrackerPostfix()
    {
        if (Blocks && _rebindInitialization && (bool)_contract.TrackerInit.GetValue(null))
            _rebindInitialization = false;
    }

    private static void GameStartedPrefix(GameWorld __instance)
    {
        if (!Blocks) return;
        try
        {
            if (__instance == null || __instance is HideoutGameWorld || __instance is NarrateGameWorld) return;
            var body = __instance.MainPlayer;
            if (!ArenaSession.ArmedForNextRaid || !_lease.BindWorld(__instance, body, body?.ProfileId,
                    __instance.LocationId, ArenaSession.ArmEpoch))
                throw new InvalidOperationException("Cola arm did not match the starting world.");
            _lastProfile = body.Profile;
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void ArenaStartPostfix()
    {
        if (!Blocks || _lease.Failed || _lease.World == null) return;
        try
        {
            if (!(bool)_contract.Lookup.Invoke(null, null)) throw new InvalidOperationException("Cola could not bind Arena's public state.");
            Invoke(_contract.Enter);
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static bool TrackerPrefix()
    {
        if (!Blocks) return true;
        try
        {
            var world = CurrentWorld();
            if (!_lease.CanObserve(world) || world == null || world.MainPlayer == null || RespawnApi.IsReplacing) return false;
            ObserveBody(world);
            ObserveExperience(world.MainPlayer);
            Invoke(_contract.ArenaTick);
            return true;
        }
        catch (Exception exception) { Fail(exception); return false; }
    }

    private static void ObserveBody(GameWorld world)
    {
        var body = world.MainPlayer;
        if (body == null) return;
        if (!ReferenceEquals(_lease.Body, body))
            throw new InvalidOperationException("Cola observed a body without a confirmed replacement.");
        _lastProfile = body.Profile;
    }

    private static void OnReplaced(Player oldBody, Player newBody, string reason)
    {
        if (!Blocks || !_lease.CanObserve(CurrentWorld())) return;
        try
        {
            var world = CurrentWorld();
            if (!ReferenceEquals(world.MainPlayer, newBody) || !_lease.Rebind(world, oldBody, newBody))
                throw new InvalidOperationException("Cola replacement did not match its current body.");
            ClearAnimator();
            // Tracker must rebind hands/inventory polling, but its ready hooks
            // count whole raids and restore entry perks. Do not replay those
            // for a replacement body within the same protected match.
            _rebindInitialization = true;
            _contract.TrackerInit.SetValue(null, false);
            _contract.TrackerDeath.SetValue(null, false);
            _contract.TrackerHands.SetValue(null, null);
            _contract.TrackerWeapon.SetValue(null, null);
            _contract.Breath.SetValue(null, null);
            _contract.HasPosition.SetValue(null, false);
            _contract.StaminHasPosition.SetValue(null, false);
            _contract.PreviousStamina.SetValue(null, -1f);
            _contract.HealingPolled.SetValue(null, false);
            _contract.NextPhysicalPoll.SetValue(null, Time.time + 1f);
            _lastProfile = newBody.Profile;
            Invoke(_contract.RefreshPhysical, newBody);
            (newBody.HandsController as Player.FirearmController)?.SyncWithCharacterSkills();
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void ObserveExperience(Player body)
    {
        if (body?.Profile?.EftStats?.SessionCounters == null) return;
        _lease.ObserveExperience(CurrentWorld(), body, body.Profile.EftStats.SessionCounters.GetAllLong(CounterTag.Exp));
    }

    private static void OnDeathClaimed(Player body, string reason)
    {
        if (!Blocks || !_lease.CanApply(CurrentWorld(), body)) return;
        try { ObserveExperience(body); }
        catch (Exception exception) { Fail(exception); }
    }

    private static void OnReplacementFailed(Player oldBody, string stage)
    {
        if (!Blocks || !_lease.CanObserve(CurrentWorld())) return;
        // A failed operation may leave the old body alive or publish a replacement
        // without a completion event. Never adopt an unconfirmed physical identity.
        ClearAnimator();
        if (_lease.ReplacementStatisticsPending || !ReferenceEquals(CurrentWorld()?.MainPlayer, _lease.Body))
            Fail(new InvalidOperationException("Cola body identity changed during a failed replacement."));
    }

    private static bool IsMePrefix(Player __0, ref bool __result)
    {
        if (!Blocks) return true;
        __result = _lease.CanApply(CurrentWorld(), __0);
        return false;
    }

    private static bool DrinkPostfixPrefix(ActiveHealthController __0)
        => !Blocks || _lease.CanApply(CurrentWorld(), __0?.Player);

    private static void DrinkPlayPrefix(ref Action __1)
    {
        if (!Blocks || __1 == null) return;
        var lease = _lease; var world = CurrentWorld(); var body = world?.MainPlayer; var apply = __1;
        __1 = () =>
        {
            if (ReferenceEquals(_lease, lease) && lease.CanApply(CurrentWorld(), body)
                && ReferenceEquals(CurrentWorld(), world) && body?.HealthController?.IsAlive == true) apply();
        };
    }

    private static void ClearAnimator()
    {
        // Do not call the foreign reset: it dereferences a possibly disposed
        // IAnimator before releasing it, which can leave the cache stuck forever.
        _contract.Sped.SetValue(null, null);
    }
    private static object[] Snapshot(FieldInfo field) => ((IEnumerable)field.GetValue(null)).Cast<object>().ToArray();
    private static void Restore(FieldInfo field, object[] values)
    {
        var collection = field.GetValue(null);
        collection.GetType().GetMethod("Clear", Type.EmptyTypes).Invoke(collection, null);
        var add = collection.GetType().GetMethod("Add");
        foreach (var value in values) add.Invoke(collection, new[] { value });
    }
    private static string Serialize(JObject state)
    {
        if (state == null) throw new InvalidOperationException("Cola returned no progression state.");
        var json = state.ToString(Formatting.None);
        if (Encoding.UTF8.GetByteCount(json) > TarkovaColaPolicy.MaxStateBytes)
            throw new InvalidOperationException("Cola progression state exceeds the Arena size limit.");
        return json;
    }
    private static void Invoke(MethodInfo method, params object[] values) => method.Invoke(null, values);
    private static void Fail(Exception exception)
    {
        _lease?.Fail();
        if (_failure != null) return;
        _failure = "Tarkova Cola's Arena lifecycle could not be preserved (" + exception.GetBaseException().GetType().Name + ").";
        Plugin.ModLogger.LogError(_failure);
    }

    private sealed class Contract
    {
        internal readonly PropertyInfo State, ServerSafe, ArenaNow;
        internal readonly FieldInfo StateField, Dirty, SaveAt, PendingRefunds, PendingNative, PendingClaims, PendingOffering;
        internal readonly FieldInfo TrackerInit, TrackerDeath, TrackerHands, TrackerWeapon, Breath, Sped, Awarded;
        internal readonly FieldInfo HasPosition, StaminHasPosition, PreviousStamina, HealingPolled, NextPhysicalPoll;
        internal readonly MethodInfo Flush, FlushSync, EnsureLoaded, SendRefund, SendClaim, UnlockNative;
        internal readonly MethodInfo TrackerTick, ArenaStart, ArenaTick, ArenaEnd, Enter, Lookup, ModeId, RaidEnd, Award;
        internal readonly MethodInfo DrinkPlay, IsMe, DrinkPostfix, ClearActive, EternalStart, Refresh, RefreshPhysical;
        internal readonly MethodInfo AchReady, ClassicReady, EternalReady;
        internal readonly MethodInfo MachinesReady, RerollRequest, RerollApply, RerollCallback, RerollReset;
        internal readonly ConstructorInfo RerollClosureCtor;
        internal readonly FieldInfo RerollContainer, RerollPending, RerollPendingFor;
        internal readonly List<MethodInfo> ProtectedMutations = new(), NewPerkCleanup = new();

        internal Contract(Assembly assembly)
        {
            if (assembly.GetName().Name != "TarkovaCola.Client") throw new InvalidOperationException("Unexpected Cola assembly.");
            string catalog = RequireProgressCatalog(assembly);
            bool currentCatalog = catalog != TarkovaColaResearchCatalog.ResearchFingerprint;
            Type T(string name) => assembly.GetType("TarkovaCola.Client." + name, true);
            MethodInfo M(string type, string name, Type returns, params Type[] args)
            {
                var method = T(type).GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
                if (method == null || method.ReturnType != returns || method.IsGenericMethod) throw new MissingMethodException(type, name);
                return method;
            }
            FieldInfo F(string type, string name, Type expected)
            {
                var field = T(type).GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
                if (field == null || field.FieldType != expected) throw new MissingFieldException(type, name);
                return field;
            }
            PropertyInfo P(string type, string name, Type expected)
            {
                var property = T(type).GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
                if (property == null || property.PropertyType != expected || property.GetMethod == null || property.GetIndexParameters().Length != 0)
                    throw new MissingMemberException(type, name);
                return property;
            }
            State = P("PerkService", "State", typeof(JObject)); StateField = F("PerkService", "_state", typeof(JObject));
            Dirty = F("PerkService", "_dirty", typeof(bool)); SaveAt = F("PerkService", "_saveAt", typeof(float));
            PendingRefunds = F("PerkService", "_pendingRefunds", typeof(List<KeyValuePair<string, int>>));
            PendingNative = F("Achievements", "_pendingNative", typeof(List<string>));
            PendingClaims = F("Achievements", "_pendingClaims", typeof(HashSet<string>));
            Flush = M("PerkService", "Flush", typeof(void)); FlushSync = M("PerkService", "FlushSync", typeof(void));
            EnsureLoaded = M("PerkService", "EnsureLoaded", typeof(void), typeof(bool));
            ServerSafe = P("Plugin", "ServerSafe", typeof(bool));
            SendRefund = M("PerkService", "SendRefund", typeof(void), typeof(string), typeof(int));
            SendClaim = M("Achievements", "SendClaim", typeof(void), typeof(string[]));
            UnlockNative = M("Achievements", "UnlockNative", typeof(bool), typeof(string));
            TrackerTick = M("Tracker", "Tick", typeof(void));
            TrackerInit = F("Tracker", "_init", typeof(bool)); TrackerDeath = F("Tracker", "_deathDone", typeof(bool));
            TrackerHands = F("Tracker", "_hands", typeof(object)); TrackerWeapon = F("Tracker", "_prevWeapon", typeof(EFT.InventoryLogic.Weapon));
            Breath = F("Perk", "MyBreath", typeof(object)); Sped = F("PerkFx", "_sped", typeof(IAnimator));
            HasPosition = F("PerkFx", "_hasPos", typeof(bool));
            StaminHasPosition = F("StaminUp", "_hasPos", typeof(bool));
            PreviousStamina = F("PerkFx", "_prevStamina", typeof(float));
            HealingPolled = F("Juggernog", "_polled", typeof(bool));
            NextPhysicalPoll = F("PerkFx", "_next1Hz", typeof(float));
            AchReady = M("AchTrack", "OnPlayerReady", typeof(void));
            ClassicReady = M("ClassicMode", "OnPlayerReady", typeof(void), typeof(Player));
            EternalReady = M("Eternal", "OnPlayerReady", typeof(void), typeof(Player));
            ArenaStart = M("ArenaCompat", "OnRaidStart", typeof(void)); ArenaTick = M("ArenaCompat", "Tick", typeof(void));
            ArenaEnd = M("ArenaCompat", "OnRaidEnd", typeof(void)); Enter = M("ArenaCompat", "Enter", typeof(void));
            Lookup = M("ArenaCompat", "Lookup", typeof(bool));
            ModeId = M("ArenaCompat", "ModeId", typeof(string)); ArenaNow = P("ArenaCompat", "ArenaNow", typeof(bool));
            RaidEnd = M("PerkFx", "OnRaidEnd", typeof(void), typeof(ExitStatus));
            Award = M("XpPatch", "Award", typeof(void), typeof(string), typeof(Profile), typeof(int));
            Awarded = F("XpPatch", "Awarded", typeof(bool));
            DrinkPlay = M("DrinkAnim", "Play", typeof(void), T("PerkDef"), typeof(Action));
            IsMe = M("Util", "IsMe", typeof(bool), typeof(Player));
            DrinkPostfix = M("DrinkPatch", "Postfix", typeof(void), typeof(ActiveHealthController), typeof(EFT.InventoryLogic.Item));
            ClearActive = M("Perks", "ClearActive", typeof(void)); EternalStart = M("Eternal", "OnRaidStart", typeof(void));
            Refresh = M("Perk", "Refresh", typeof(void)); RefreshPhysical = M("PerkFx", "RefreshPhysical", typeof(void), typeof(Player));
            if (catalog == TarkovaColaResearchCatalog.LatestResearchFingerprint)
            {
                PendingOffering = F("PerkService", "_pendingOffering", typeof(string));
                ProtectedMutations.Add(M("PerkService", "RequestOffering", typeof(void), typeof(string)));
                // Never drain even a legitimate pre-Arena queue while server-side
                // protection is active. Restore its exact value at lease release.
                ProtectedMutations.Add(M("PerkService", "FlushRefunds", typeof(void)));
                foreach (string name in new[] { "DoubleTap", "ElectricCherry", "QuickRevive", "DeathPerception", "Tombstone", "MuleKick" })
                    NewPerkCleanup.Add(M(name, "Reset", typeof(void)));
            }
            if (assembly.GetType("TarkovaCola.Client.PitPerks", false) != null)
            {
                // 3.0.1's follower editor writes pit_perks.json directly, outside
                // PerkService. Block both UI mutation and delayed bottle actions,
                // leaving ordinary raids and already pending ordinary work alone.
                foreach (string name in new[] { "Tick", "Save", "ApplyBottles" })
                    ProtectedMutations.Add(M("PitPerks", name, typeof(void)));
                ProtectedMutations.Add(M("PitPerks", "OnDeath", typeof(void), typeof(Player)));
                foreach (string name in new[] { "Equip", "Unequip" })
                {
                    var method = T("PitPerks").GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                        null, new[] { typeof(string) }, null);
                    if (method == null || method.ReturnType != typeof(void) || method.IsGenericMethod)
                        throw new MissingMethodException("PitPerks", name);
                    ProtectedMutations.Add(method);
                }
                MachinesReady = M("Machines", "OnPlayerReady", typeof(void), typeof(Player));
                ProtectedMutations.Add(M("Machines", "Save", typeof(void)));
                ProtectedMutations.Add(M("Machines", "Place", typeof(void)));
                ProtectedMutations.Add(M("Machines", "DeleteNearest", typeof(void), typeof(Vector3)));
                NewPerkCleanup.Add(M("Bandolier", "Reset", typeof(void))); // restores shared grenade fuse templates
                NewPerkCleanup.Add(M("VultureAid", "Reset", typeof(void))); // releases old container/HTTP state
                NewPerkCleanup.Add(M("MeleeMacchiato", "Reset", typeof(void)));
                NewPerkCleanup.Add(M("NewPerkUtil", "Reset", typeof(void)));
                RerollRequest = M("Reroll", "Request", typeof(void), typeof(EFT.Interactive.LootableContainer));
                RerollApply = M("Reroll", "Apply", typeof(void), typeof(JObject), typeof(EFT.Interactive.LootableContainer));
                // Bind the reviewed HTTP continuation, not every task or a global
                // response handler. A late response must not repopulate a new raid.
                var callbacks = T("Reroll").GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                    .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    .Where(m => m.Name.StartsWith("<Request>b__", StringComparison.Ordinal)
                        && m.ReturnType == typeof(void) && !m.IsGenericMethod
                        && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(System.Threading.Tasks.Task<string>) })).ToArray();
                if (callbacks.Length != 1) throw new MissingMethodException("Reroll", "Request continuation");
                RerollCallback = callbacks[0];
                RerollClosureCtor = RerollCallback.DeclaringType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (RerollClosureCtor == null) throw new MissingMethodException("Reroll", "closure constructor");
                RerollReset = M("Reroll", "Reset", typeof(void));
                RerollPending = F("Reroll", "_pending", typeof(JObject));
                RerollPendingFor = F("Reroll", "_pendingFor", typeof(EFT.Interactive.LootableContainer));
                RerollContainer = RerollCallback.DeclaringType.GetField("c", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (RerollContainer?.FieldType != typeof(EFT.Interactive.LootableContainer)) throw new MissingFieldException("Reroll", "c");
            }
            else if (currentCatalog || assembly.GetType("TarkovaCola.Client.Machines", false) != null
                || assembly.GetType("TarkovaCola.Client.Apex", false) != null)
                throw new MissingMemberException("Incomplete Tarkova Cola 3.x protection contract.");
        }

        private static string RequireProgressCatalog(Assembly assembly)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
            IEnumerable Values(string type, string name, bool property = false)
            {
                var owner = assembly.GetType("TarkovaCola.Client." + type, true);
                var value = property ? owner.GetProperty(name, flags)?.GetValue(null) : owner.GetField(name, flags)?.GetValue(null);
                return value as IEnumerable ?? throw new MissingMemberException(type, name);
            }
            string Text(object value, string field) => value?.GetType().GetField(field)?.GetValue(value) as string
                ?? throw new MissingFieldException("Cola catalog", field);
            int Number(object value, string field) => value?.GetType().GetField(field)?.GetValue(value) is int number
                ? number : throw new MissingFieldException("Cola catalog", field);
            var research = new List<string>();
            foreach (var perk in Values("Perks", "All"))
            {
                string perkId = Text(perk, "Id");
                research.Add(TarkovaColaResearchCatalog.Perk(perkId));
                var augs = perk.GetType().GetField("Augs")?.GetValue(perk) as IEnumerable
                    ?? throw new MissingFieldException("Cola perk", "Augs");
                foreach (var aug in augs)
                {
                    if (Text(aug, "Perk") != perkId) throw new InvalidOperationException("Cola challenge owner changed.");
                    research.Add(TarkovaColaResearchCatalog.Challenge(perkId, Text(aug, "Id"), Number(aug, "Goal")));
                }
            }
            // v4 removed Synergy. Its complete research fingerprint and paired
            // achievement fingerprint must match; a missing old table still fails.
            if (assembly.GetType("TarkovaCola.Client.Synergy", false) != null)
                foreach (var synergy in Values("Synergy", "All"))
                    research.Add(TarkovaColaResearchCatalog.Synergy(Text(synergy, "Id"), Number(synergy, "Goal")));
            TarkovaColaResearchCatalog.RequireResearch(research);
            // Foreign progress keys are CLIENT keys (a_first, st_fresh, ...),
            // which intentionally differ from server localization keys.
            var achievements = new List<string>();
            foreach (var achievement in Values("Achievements", "All", property: true))
                achievements.Add(TarkovaColaResearchCatalog.Achievement(Text(achievement, "Key"), Text(achievement, "ServerId")));
            TarkovaColaResearchCatalog.RequirePair(research, achievements);
            return TarkovaColaResearchCatalog.Fingerprint(research);
        }
    }
}
