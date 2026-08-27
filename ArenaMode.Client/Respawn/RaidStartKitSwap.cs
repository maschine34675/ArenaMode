using ArenaMode.Config;
using ArenaMode.Kits;
using EFT;
using Redeploy.Core.Engine;
using Redeploy.Core.GameAccess;
using Redeploy.Core.Spawning;
using System;
using System.Collections;
using UnityEngine;

namespace ArenaMode.Respawn;

/// <summary>
/// At arena raid start the player is respawned in place with the selected kit:
/// the battle-tested engine pipeline rebuilds the body from the kit profile, so
/// the real stash gear never enters the raid. The old (alive) body is destroyed
/// after the takeover — unlike a death respawn there is no corpse to keep.
/// </summary>
internal static class RaidStartKitSwap
{
    private sealed class FixedSpawnSelector : ISpawnSelector
    {
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;

        public FixedSpawnSelector(Vector3 position, Quaternion rotation)
        {
            _position = position;
            _rotation = rotation;
        }

        public (Vector3 position, Quaternion rotation) Resolve(LocalGame game) => (_position, _rotation);
    }

    public static void Run(LocalGame game)
    {
        var kit = KitCatalog.GetSelectedKit();
        if (kit == null)
        {
            Plugin.ModLogger.LogWarning("Arena: no kit available — raid continues with real gear (still nothing persists).");
            return;
        }

        game.StartCoroutine(SwapRoutine(game, kit));
    }

    private static IEnumerator SwapRoutine(LocalGame game, KitDefinition kit)
    {
        var oldBody = game.LocalPlayer;
        if (oldBody == null)
            yield break;

        // The game auto-draws a weapon right before OnGameStarted; never start the
        // swap while that hands animation is in flight (HandsController trap).
        var drawDeadline = Time.time + 3f;
        while (oldBody.InventoryController != null
               && oldBody.InventoryController.IsChangingWeapon
               && Time.time < drawDeadline)
        {
            yield return null;
        }

        // Kit items never existed in this raid — their bundles/pools must be loaded
        // BEFORE the pipeline, or the replacement's weapon auto-draw NREs.
        yield return KitBundlePreloader.Preload(kit);

        ArenaRespawnDriver.BeginPipelineUse();

        var request = new RespawnRequest
        {
            Game = game,
            DeadPlayer = oldBody,
            SpawnSelector = new FixedSpawnSelector(oldBody.Position, oldBody.Transform.rotation),
            EquipmentStrategy = (profile, dead) => KitBuilder.ApplyToProfile(profile, dead, kit, requireCorpse: false),
            HealthPercent = 100f,
            Protection = null,
        };

        Exception failure = null;
        yield return game.StartCoroutine(RespawnEngine.RunRespawn(request, e => failure = e));

        ArenaRespawnDriver.EndPipelineUse();

        if (failure != null)
        {
            Plugin.ModLogger.LogError($"Arena raid-start kit swap failed: {failure}");
            RecoverOldBody(game, oldBody);
            yield break;
        }

        // The alive pre-swap body would otherwise linger in the scene overlapping the
        // replacement (a death respawn leaves a corpse instead). If it somehow died
        // during the swap window, its GameObject IS the corpse now — leave it alone.
        try
        {
            if (oldBody != null
                && oldBody.HealthController != null
                && oldBody.HealthController.IsAlive
                && CorpseField(oldBody) == null)
            {
                UnityEngine.Object.Destroy(oldBody.gameObject);
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena: destroying pre-swap body failed: {ex.Message}");
        }

        Plugin.ModLogger.LogInfo($"Arena raid-start kit swap complete: {kit.Name}.");
    }

    private static readonly HarmonyLib.AccessTools.FieldRef<Player, EFT.Interactive.Corpse> CorpseField =
        HarmonyLib.AccessTools.FieldRefAccess<Player, EFT.Interactive.Corpse>("Corpse");

    /// <summary>
    /// The engine's teardown (owner destroyed, death handler unsubscribed, player
    /// unregistered) ran before the failure surfaced — without recovery the raid is
    /// alive but unplayable: no input, and dying would never end it. Two cases:
    /// the failure hit BEFORE the replacement took over (rewire the old body), or
    /// DURING its wiring — game.LocalPlayer already points at the replacement
    /// (finish wiring THAT body; rewiring the old one would leave the half-installed
    /// replacement as MainPlayer NRE-spamming every LateUpdate — first playtest bug).
    /// If everything fails, end the raid (the profile is snapshot-protected).
    /// </summary>
    private static void RecoverOldBody(LocalGame game, LocalPlayer oldBody)
    {
        try
        {
            var current = game.LocalPlayer;
            if (current != null && !ReferenceEquals(current, oldBody))
            {
                RecoverReplacementBody(game, current, oldBody);
                return;
            }

            LocalGameAccessor.UnsubscribeDeathHandler(game, oldBody);
            LocalGameAccessor.SubscribeDeathHandler(game, oldBody);

            if (Comfort.Common.Singleton<GameWorld>.Instantiated)
                Comfort.Common.Singleton<GameWorld>.Instance.RegisterPlayer(oldBody);

            var bots = LocalGameAccessor.GetBotsController(game);
            bots.AddActivePLayer(oldBody);

            var owner = LocalGameAccessor.CreatePlayerOwner(game, oldBody);
            LocalGameAccessor.SetPlayerOwner(game, owner);
            Redeploy.Core.GameAccess.GamePlayerOwnerHelper.SetMyPlayer(oldBody);
            owner.vmethod_0();

            Plugin.ModLogger.LogWarning("Arena: kit swap failed — old body rewired, raid continues with real gear.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError($"Arena: old-body recovery failed ({ex.Message}) — ending raid.");
            EndRaidAsLastResort(game);
        }
    }

    /// <summary>The engine failed mid-rewire: complete the replacement's wiring instead.</summary>
    private static void RecoverReplacementBody(LocalGame game, LocalPlayer replacement, LocalPlayer oldBody)
    {
        try
        {
            var owner = LocalGameAccessor.GetPlayerOwner(game);
            if (owner == null || !ReferenceEquals(owner.Player, replacement))
            {
                owner = LocalGameAccessor.CreatePlayerOwner(game, replacement);
                LocalGameAccessor.SetPlayerOwner(game, owner);
            }

            Redeploy.Core.GameAccess.GamePlayerOwnerHelper.SetMyPlayer(replacement);
            Redeploy.Core.View.CameraRebindHelper.RebindToPlayer(replacement);
            Redeploy.Core.View.DeathEffectsHelper.RebindEffectsController(replacement);

            if (owner.State != PlayerOwner.EState.Started)
                owner.vmethod_0();

            // The pre-swap body is unregistered and ownerless — remove it unless it
            // somehow became a corpse in the failure window.
            if (oldBody != null
                && oldBody.HealthController != null
                && oldBody.HealthController.IsAlive
                && CorpseField(oldBody) == null)
            {
                UnityEngine.Object.Destroy(oldBody.gameObject);
            }

            Plugin.ModLogger.LogWarning("Arena: kit swap failed mid-rewire — recovered onto the replacement body.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError($"Arena: replacement-body recovery failed ({ex.Message}) — ending raid.");
            EndRaidAsLastResort(game);
        }
    }

    private static void EndRaidAsLastResort(LocalGame game)
    {
        ArenaRespawnDriver.StandDown = true;
        try
        {
            LocalGameAccessor.InvokeDeathScreen(game);
        }
        catch (Exception inner)
        {
            Plugin.ModLogger.LogError($"Arena: could not end raid either: {inner.Message}");
        }
    }
}
