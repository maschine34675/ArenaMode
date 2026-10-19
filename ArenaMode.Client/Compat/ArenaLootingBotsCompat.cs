using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using JsonType;
using Redeploy.Core.GameAccess;
using UnityEngine;

namespace ArenaMode.Compat;

/// <summary>Pause autonomous looting only; never rewrite settings or PFT commands.</summary>
internal static class ArenaLootingBotsCompat
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".LootingBotsCompat");
    private static ArenaLootingBotsContract _contract;
    private static BaseUnityPlugin _plugin;
    private static string _failure, _lastWarning;
    private static bool _patchAttempted, _runtimeFault, _newerWarned;
    private static WeakReference<GameWorld> _reportedWorld;

    internal static void Initialize() => Refresh();
    internal static string GetRejectionReason() { Refresh(); return _failure; }

    private static void Refresh()
    {
        if (!Chainloader.PluginInfos.TryGetValue(ArenaLootingBotsContract.Guid, out var info))
        { Clear(); _failure = null; _runtimeFault = false; return; }
        if (_runtimeFault) return;
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Looting Bots is still loading");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance)
                && _contract.Version == ArenaLootingBotsContract.Normalize(info.Metadata?.Version))
            {
                if (!HasHooks()) throw new InvalidOperationException("Looting Bots protection hook was removed");
                return;
            }
            Clear();
            var contract = ArenaLootingBotsContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version);
            var settings = AccessTools.Field(typeof(LocalGame), "_raidSettings");
            if (settings == null || settings.IsStatic || settings.FieldType != typeof(LocalRaidSettings))
                throw new MissingFieldException("LocalGame._raidSettings changed");
            _patchAttempted = true;
            Patch(contract.Layer, nameof(BeforeLayer));
            Patch(contract.Search, nameof(BeforeSearch));
            Patch(contract.CorpseQueue, nameof(BeforeQueue));
            Patch(contract.AirdropQueue, nameof(BeforeQueue));
            Patch(contract.Loot, nameof(BeforeLoot));
            if (contract.Cleanup != null) Patch(contract.Cleanup, nameof(BeforeCleanup));
            _contract = contract; _plugin = info.Instance;
            if (!HasHooks()) throw new InvalidOperationException("Looting Bots protection hooks did not install");
            _failure = null;
            Plugin.ModLogger.LogDebug("Arena Looting Bots: exact-body autonomous-looting guards ready.");
            if (contract.Version > ArenaLootingBotsContract.Reviewed && !_newerWarned)
            {
                _newerWarned = true;
                Plugin.ModLogger.LogWarning("Arena Looting Bots " + contract.Version
                    + " is newer than reviewed 1.8.0; the layer and spawn inventory contracts were checked, native compatibility is unverified.");
            }
        }
        catch (Exception ex) { Clear(); Reject(ex); }
    }

    private static bool BeforeLayer(object __instance, ref bool __result)
    {
        if (!Suppress(__instance, _contract?.LayerOwner)) return true;
        __result = false;
        return false;
    }
    private static bool BeforeSearch(object __instance, int __0)
    {
        if (!Suppress(__instance, _contract?.FinderOwner)) return true;
        // FindLootLogic already acquired this scheduler ticket. A skipped
        // search must return it exactly once, including unlimited scans (0).
        try { _contract.ReturnTicket(__0); return false; }
        catch (Exception ex)
        {
            // The original owns the ticket after a foreign scheduler fault.
            // Do not swallow it or globally reset a shared scheduler.
            _runtimeFault = true; Reject(ex); return true;
        }
    }
    private static bool BeforeQueue(object __instance) => !Suppress(__instance, _contract?.FinderOwner);
    private static bool BeforeLoot(object __instance) => !Suppress(__instance, _contract?.BrainOwner);
    private static bool BeforeCleanup(object __instance, ref Task __result)
    {
        // In reviewed 1.8.1-RC this is called only by OnSpawnAsync, never by
        // command looting. Protect PFT entry gear too, leaving its Init intact.
        if (!Suppress(__instance, _contract?.InventoryOwner, preservePftCommands: false)) return true;
        __result = Task.CompletedTask;
        return false;
    }

    private static bool Suppress(object instance, MemberInfo ownerMember, bool preservePftCommands = true)
    {
        if (_contract == null || _runtimeFault || instance == null || ownerMember == null) return false;
        try
        {
            if (!Singleton<AbstractGame>.Instantiated || !Singleton<GameWorld>.Instantiated
                || Singleton<AbstractGame>.Instance is not LocalGame game) return false;
            var world = Singleton<GameWorld>.Instance;
            if (game == null || world == null || !ReferenceEquals(game.GameWorld, world)) return false;
            var settings = LocalGameAccessor.GetLocalRaidSettings(game);
            bool active = ArenaSession.IsExactActiveRaid(game, world);
            bool raid = ArenaLootingBotsScope.OwnsRaid(true,
                game.Status == GameStatus.Running || game.Status == GameStatus.Starting || game.Status == GameStatus.Started,
                settings?.playerSide == ESideType.Pmc, world.LocationId, settings?.location,
                active, ArenaSession.ArmedForNextRaid, ArenaServerClient.HasPendingArm,
                ArenaSession.ArmedMap, ArenaServerClient.PendingArmToken, settings?.serverId);
            if (!raid) return false;
            var bot = (ownerMember is PropertyInfo property ? property.GetValue(instance) : ((FieldInfo)ownerMember).GetValue(instance)) as BotOwner;
            var player = bot?.GetPlayer;
            // Player.Init registers this body before BotOwner.Create sets its
            // AIData owner and controller, before Brain.Activate constructs the
            // custom layer. Do not wait for Active or the mode's later roster.
            bool body = bot != null && player != null && player.IsAI && !string.IsNullOrEmpty(player.ProfileId)
                && ReferenceEquals(player.AIData?.BotOwner, bot)
                && ReferenceEquals(bot.BotsController, ((IBotGame)game).BotsController)
                && ReferenceEquals(world.GetEverExistedPlayerByID(player.ProfileId), player);
            if (!body) return false;
            if (instance is Component component && !ReferenceEquals(component.gameObject, player.gameObject)) return false;
            if (ReferenceEquals(ownerMember, _contract.InventoryOwner)
                && !ReferenceEquals(_contract.NativeInventory.GetValue(instance), player.InventoryController)) return false;
            string mode = active ? ArenaSession.ActiveMode : ArenaSession.ArmedMode;
            bool pftTeam = preservePftCommands && (string.Equals(mode, "tactical", StringComparison.Ordinal)
                || string.Equals(mode, "zombie", StringComparison.Ordinal)) && PitFireTeamCompat.TeamArenaActive;
            // This authenticated account exemption is evaluated only after the
            // exact current game/controller/registered physical body proof.
            // It also covers replacements before PFT publishes new profile IDs.
            bool companion = false;
            if (pftTeam)
            {
                string account = bot.Profile?.AccountId?.Trim();
                companion = !string.IsNullOrEmpty(account)
                    && PitFireTeamCompat.FrozenCompanionAccountIds.Contains(account, StringComparer.Ordinal);
            }
            bool suppress = ArenaLootingBotsScope.Suppress(raid, body,
                pftTeam, companion, mode);
            if (suppress && (_reportedWorld == null || !_reportedWorld.TryGetTarget(out var old) || !ReferenceEquals(old, world)))
            {
                _reportedWorld = new WeakReference<GameWorld>(world);
                Plugin.ModLogger.LogInfo("Arena: autonomous Looting Bots behavior is paused for this match; PFT companion commands and ordinary raids remain unchanged.");
            }
            return suppress;
        }
        catch (Exception ex)
        {
            // Unknown identity must never suppress an ordinary raid. Surface
            // the contract fault and refuse subsequent Arena admission.
            _runtimeFault = true; Reject(ex); return false;
        }
    }

    private static void Patch(MethodInfo target, string name)
        => Patches.Patch(target, prefix: new HarmonyMethod(typeof(ArenaLootingBotsCompat), name) { priority = Priority.First });
    private static bool Has(MethodInfo target, string name)
        => target != null && Harmony.GetPatchInfo(target)?.Prefixes.Any(p => p.owner == Patches.Id
            && p.PatchMethod.DeclaringType == typeof(ArenaLootingBotsCompat) && p.PatchMethod.Name == name) == true;
    private static bool HasHooks() => _contract != null && Has(_contract.Layer, nameof(BeforeLayer))
        && Has(_contract.Search, nameof(BeforeSearch)) && Has(_contract.Loot, nameof(BeforeLoot))
        && Has(_contract.CorpseQueue, nameof(BeforeQueue)) && Has(_contract.AirdropQueue, nameof(BeforeQueue))
        && (_contract.Cleanup == null || Has(_contract.Cleanup, nameof(BeforeCleanup)));
    private static void Reject(Exception ex)
    {
        _failure = "Arena cannot safely pause Looting Bots: " + ex.GetBaseException().Message
            + ". Update or remove Looting Bots and restart. Ordinary raids remain available.";
        if (_lastWarning == _failure) return;
        _lastWarning = _failure; Plugin.ModLogger.LogWarning(_failure);
    }
    private static void Clear()
    {
        _contract = null; _plugin = null; _reportedWorld = null;
        if (!_patchAttempted) return;
        _patchAttempted = false;
        try { Patches.UnpatchSelf(); } catch (Exception ex) { Reject(ex); }
    }
    internal static void Shutdown() { Clear(); _runtimeFault = false; _failure = _lastWarning = null; }
}
