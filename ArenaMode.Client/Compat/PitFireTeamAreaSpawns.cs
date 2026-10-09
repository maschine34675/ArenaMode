using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ArenaMode.Areas;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;

namespace ArenaMode.Compat;

/// <summary>Defers native PFT startup until bounded navigation is ready and leases
/// real connected markers for each authenticated companion, including later rounds.</summary>
internal static class PitFireTeamAreaSpawns
{
    private static MethodInfo _spawnFollowers, _getBoss;
    private static FieldInfo _controller, _bossPlayer, _bossController, _spawnedPlayers;
    private static Pending _pending;
    private static readonly List<Arrival> Arrivals = new();
    // LocalGame creates a new BotCreatorClient for each raid. Canonical PFT
    // profile IDs (and potentially Profile objects) survive a menu-to-raid
    // transition, so neither duplicate admission nor raw creation may be keyed
    // globally by profile alone. Keep the old creator's revoked ledger alive
    // while its native continuation can still resume after Task.Yield.
    private sealed class CreatorLedger
    {
        internal readonly HashSet<string> InitialIds = new(StringComparer.Ordinal);
        internal readonly ConditionalWeakTable<Profile, Arrival> RawLeases = new();
    }
    private static readonly ConditionalWeakTable<BotCreatorClient, CreatorLedger> CreatorLedgers = new();

    private sealed class Pending
    {
        internal LocalGame Game;
        internal GameWorld World;
        internal Player Leader;
        internal BotsController Controller;
        internal object Boss;
        internal int Epoch;
    }
    private sealed class Arrival
    {
        internal Pending Raid;
        internal Profile Profile;
        internal BotCreatorClient Creator;
        internal BotSpawner Spawner;
        internal ArenaAreaRuntime.TeamSpawnLease Placement;
        internal bool Runtime, Revoked, Completed, CounterSettled;
        internal BotOwner Body;
        internal BotOwner RetiredBody;
    }

    internal static void Configure(Assembly assembly, Harmony harmony)
    {
        var patch = assembly.GetType("pitTeam.Patches.BotsControllerPatch", true);
        var boss = assembly.GetType("pitTeam.Components.pitAIBossPlayer", true);
        _spawnFollowers = assembly.GetType("pitTeam.Patches.BotsEventsControllerSpawnPatch", true)
            .GetMethod("SpawnFollowers", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        _controller = patch.GetField("Controller", BindingFlags.Public | BindingFlags.Static);
        _spawnedPlayers = patch.GetField("spawnedPlayers", BindingFlags.Public | BindingFlags.Static);
        _bossPlayer = boss.GetField("realPlayer", BindingFlags.Public | BindingFlags.Instance);
        _bossController = boss.GetField("_botsController", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        _getBoss = assembly.GetType("pitTeam.Modules.BossPlayers", true).GetMethod("GetBoss",
            BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
        var activate = patch.GetMethod("ActivateBotAtPosition", BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(BotCreatorClient), typeof(Profile), typeof(PositionNote), typeof(BotZone),
                typeof(bool), typeof(Func<BotOwner, BotZone, BotsGroup>), typeof(Action<BotOwner>), typeof(CancellationToken) }, null);
        if (_spawnFollowers?.ReturnType != typeof(void) || _controller?.FieldType != typeof(BotsController)
            || _spawnedPlayers == null || _bossPlayer?.FieldType != typeof(Player)
            || _bossController?.FieldType != typeof(BotsController) || _getBoss?.ReturnType != boss
            || activate?.ReturnType != typeof(Task<bool>))
            throw new InvalidOperationException("PFT bounded companion spawn contract changed.");
        harmony.Patch(_spawnFollowers, prefix: new HarmonyMethod(typeof(PitFireTeamAreaSpawns), nameof(DeferPrefix)));
        // The existing round lifecycle prefix validates/remaps runtime profiles
        // first. A rejected native call must not acquire a second placement lease.
        harmony.Patch(activate,
            prefix: new HarmonyMethod(typeof(PitFireTeamAreaSpawns), nameof(ActivatePrefix)) { priority = Priority.Last },
            postfix: new HarmonyMethod(typeof(PitFireTeamAreaSpawns), nameof(ActivatePostfix)) { priority = Priority.Last });
        var create = typeof(BotCreatorClient).GetMethod(nameof(BotCreatorClient.CreateBot),
            new[] { typeof(Profile), typeof(PositionNote), typeof(Action<BotOwner>), typeof(bool), typeof(CancellationToken) });
        if (create?.ReturnType != typeof(Task)) throw new MissingMethodException("BotCreatorClient.CreateBot contract changed.");
        harmony.Patch(create, prefix: new HarmonyMethod(typeof(PitFireTeamAreaSpawns), nameof(CreatePrefix)));
    }

    private static Pending Capture()
    {
        if (!PitFireTeamCompat.TeamArenaActive || PitFireTeamCompat.IsFinalizing
            || !Singleton<AbstractGame>.Instantiated || !Singleton<GameWorld>.Instantiated
            || Singleton<AbstractGame>.Instance is not LocalGame game) return null;
        var world = Singleton<GameWorld>.Instance;
        var controller = ((IBotGame)game).BotsController;
        if (world == null || !ReferenceEquals(game.GameWorld, world) || world.MainPlayer == null
            || !world.MainPlayer.IsYourPlayer || world.MainPlayer.ProfileId != game.Profile?.Id
            || controller == null || !ReferenceEquals(_controller.GetValue(null), controller)) return null;
        if (ArenaSession.IsActive)
        {
            if (game.Status != GameStatus.Started || !ArenaSession.IsExactActiveRaid(game, world)
                || (ArenaSession.ActiveMode != "tactical" && ArenaSession.ActiveMode != "zombie")) return null;
        }
        else if (game.Status != GameStatus.Running || !ArenaSession.ArmedForNextRaid
            || !string.Equals(world.LocationId, ArenaSession.ArmedMap, StringComparison.OrdinalIgnoreCase)
            || (ArenaSession.ArmedMode != "tactical" && ArenaSession.ArmedMode != "zombie")) return null;
        var boss = _getBoss.Invoke(null, new object[] { world.MainPlayer.ProfileId });
        if (boss == null || !ReferenceEquals(_bossPlayer.GetValue(boss), world.MainPlayer)
            || !ReferenceEquals(_bossController.GetValue(boss), controller)
            || _spawnedPlayers.GetValue(null) is not IList spawned || spawned.Count != 1
            || !ReferenceEquals(spawned[0], boss)) return null;
        return new Pending { Game = game, World = world, Leader = world.MainPlayer,
            Controller = controller, Boss = boss, Epoch = ArenaSession.ArmEpoch };
    }

    private static bool Current(Pending raid)
    {
        var now = Capture();
        return raid != null && now != null && raid.Epoch == now.Epoch
            && ReferenceEquals(raid.Game, now.Game) && ReferenceEquals(raid.World, now.World)
            && ReferenceEquals(raid.Leader, now.Leader) && ReferenceEquals(raid.Controller, now.Controller)
            && ReferenceEquals(raid.Boss, now.Boss);
    }

    private static bool DeferPrefix()
    {
        if (!ArenaAreaRuntime.ShouldBlockLoadingBotDispatch()) return true;
        var raid = Capture();
        if (raid == null) return true;
        if (_pending == null)
        {
            _pending = raid;
            Plugin.ModLogger.LogInfo("Arena bounded PFT: native companion dispatch waits for enclosure/navigation readiness.");
        }
        return false;
    }

    internal static void Tick()
    {
        if (_pending == null) return;
        if (!Current(_pending)) { _pending = null; return; }
        if (!ArenaAreaRuntime.IsReady) return;
        _pending = null; // native squadSpawned remains the duplicate-dispatch owner
        try
        {
            _spawnFollowers.Invoke(null, null);
            Plugin.ModLogger.LogInfo("Arena bounded PFT: released native companion dispatch after area readiness.");
        }
        catch (Exception ex)
        { Plugin.ModLogger.LogError("Arena bounded PFT dispatch failed: " + ex.GetBaseException().Message); }
    }

    private static bool ActivatePrefix(bool __runOriginal, BotCreatorClient botCreator, Profile profile,
        ref PositionNote position, ref BotZone zone, ref Action<BotOwner> OnActivate,
        ref Task<bool> __result, out Arrival __state)
    {
        __state = null;
        if (!__runOriginal || (ArenaAreaRuntime.CurrentArea == null
            && !ArenaAreaRuntime.ShouldBlockLoadingBotDispatch())) return true;
        var raid = Capture();
        if (raid == null || profile == null || botCreator == null
            || !ReferenceEquals(botCreator._game, raid.Game)
            || !ReferenceEquals(raid.Controller.BotSpawner?._botCreator, botCreator)) return true;
        bool runtime = PitFireTeamRoundLifecycle.IsCurrentBoundedActivation(profile, botCreator);
        bool initial = !PitFireTeamCompat.RoundTransitionActive
            && PitFireTeamCompat.CanonicalCompanionProfilesByAccountId.TryGetValue(profile.AccountId ?? "", out var id)
            && id == profile.ProfileId;
        if (!runtime && !initial) return true;
        var state = new Arrival { Raid = raid, Profile = profile, Creator = botCreator,
            Spawner = raid.Controller.BotSpawner, Runtime = runtime };
        __state = state;
        var ledger = CreatorLedgers.GetValue(botCreator, _ => new CreatorLedger());
        string admissionProblem = OnActivate == null ? "native companion callback is missing"
            : !runtime && !ledger.InitialIds.Add(profile.ProfileId) ? "companion identity was already dispatched by this raid's creator"
            : !runtime && ledger.RawLeases.TryGetValue(profile, out _) ? "companion profile already has a raw creation lease for this raid's creator"
            : null;
        if (admissionProblem != null)
        {
            Reject(state, null, admissionProblem);
            __result = Task.FromResult(false);
            return false;
        }
        if (!ArenaAreaRuntime.TryReserveCompanionSpawn(out var pickedZone, out var point, out var placement))
        {
            Reject(state, null, "no distinct safe companion marker is available");
            __result = Task.FromResult(false);
            return false;
        }
        state.Placement = placement;
        if (!runtime) ledger.RawLeases.Add(profile, state);
        Arrivals.Add(state);
        position = new PositionNote(point.Position, point.CorePointId, false);
        zone = pickedZone;
        var original = OnActivate;
        OnActivate = owner =>
        {
            if (state.Completed && ReferenceEquals(state.Body, owner)) return;
            if (state.Revoked || state.Completed || !Current(state.Raid) || owner == null
                || !ReferenceEquals(owner.Profile, profile) || owner.GetPlayer == null
                || !ReferenceEquals(owner.GetPlayer.GameWorld, state.Raid.World)
                || !state.Placement.Accept(owner.GetPlayer))
            { Reject(state, owner, "companion arrival lost its exact bounded placement lease"); return; }
            state.Body = owner;
            state.Completed = true;
            int beforeCallback = state.Spawner._inSpawnProcess;
            try
            {
                original(owner);
                state.CounterSettled = state.Spawner._inSpawnProcess < beforeCallback;
                if (!state.CounterSettled)
                    Reject(state, owner, "native companion callback did not settle its spawn charge");
            }
            catch
            {
                // Match the existing round lifecycle's native counter evidence:
                // only the unchanged charge still belongs to this failed callback.
                state.CounterSettled = state.Spawner._inSpawnProcess < beforeCallback;
                Reject(state, owner, "native companion callback threw");
                throw;
            }
            finally { state.Placement.Dispose(); Arrivals.Remove(state); }
        };
        return true;
    }

    private static bool CreatePrefix(BotCreatorClient __instance, Profile profile, ref Task __result)
    {
        if (__instance == null || profile == null
            || !CreatorLedgers.TryGetValue(__instance, out var ledger)
            || !ledger.RawLeases.TryGetValue(profile, out var state)
            || !ReferenceEquals(__instance, state.Creator)) return true;
        if (!state.Revoked && !state.Completed && Current(state.Raid)
            && state.Placement.IsCurrent) return true;
        Reject(state, null, "raw companion creation outlived its exact raid placement");
        __result = Task.CompletedTask;
        return false;
    }

    private static void ActivatePostfix(ref Task<bool> __result, Arrival __state)
    {
        if (__state != null) __result = Observe(__result, __state);
    }

    private static async Task<bool> Observe(Task<bool> task, Arrival state)
    {
        bool result;
        try { result = task != null && await task; }
        catch { Reject(state, state.Body, "native activation task threw"); throw; }
        if (!result) Reject(state, state.Body, "native activation task returned false");
        // PFT's async task can settle BEFORE its physical callback. Keep its
        // reservation until arrival or raid reset; never infer arrival from Task.
        return result && !state.Revoked;
    }

    private static void Reject(Arrival state, BotOwner body, string reason)
    {
        bool first = !state.Revoked;
        state.Revoked = true;
        state.Placement?.Dispose(); Arrivals.Remove(state);
        bool exactBody = body != null && !ReferenceEquals(body, state.RetiredBody)
            && ReferenceEquals(body.Profile, state.Profile)
            && ReferenceEquals(body.GetPlayer?.GameWorld, state.Raid.World);
        bool retiredByLifecycle = false;
        if (exactBody) state.RetiredBody = body;
        if (state.Runtime)
            retiredByLifecycle = PitFireTeamRoundLifecycle.RejectBoundedActivation(state.Profile, exactBody ? body : null, reason);
        else if (!state.CounterSettled)
        {
            state.CounterSettled = true;
            state.Spawner._inSpawnProcess = Math.Max(0, state.Spawner._inSpawnProcess - 1);
        }
        if (exactBody && !retiredByLifecycle)
            PitFireTeamRoundLifecycle.RetireBoundedFollower(body, state.Raid.Controller, state.Raid.World);
        if (first) Plugin.ModLogger.LogWarning("Arena bounded PFT rejected follower " + state.Profile.ProfileId + ": " + reason);
    }

    internal static void Reset()
    {
        _pending = null;
        foreach (var arrival in Arrivals.ToArray()) Reject(arrival, null, "raid scope ended");
        // Do not clear creator ledgers: an ended game's delayed raw creation
        // must remain rejected, while a new raid owns a new native creator.
        Arrivals.Clear();
    }
}
