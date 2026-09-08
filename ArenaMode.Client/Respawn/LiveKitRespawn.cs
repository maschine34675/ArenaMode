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
/// Respawns a LIVING player with a fresh kit.
///
/// Two occasions need this, and they only differ in where the player lands:
/// the arena raid start (in place — the point is the kit, not the position) and
/// the break between rounds (at a spawn point, deliberately away from the fight
/// that just ended). Everything else is the same battle-tested engine pipeline
/// that a death respawn uses, so the real stash gear never enters the raid.
///
/// Unlike a death respawn there is no corpse to keep, so the old body is
/// destroyed after the takeover.
/// </summary>
internal static class LiveKitRespawn
{
    private static int _initialSwapEpoch = -1;
    internal static bool IsInitialSwapPending => _initialSwapEpoch >= 0
        && _initialSwapEpoch == ArenaRespawnDriver.CaptureRaidEpoch();
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

    /// <summary>Raid start: same spot, fresh kit, no protection.</summary>
    public static void AtRaidStart(LocalGame game)
    {
        int raidEpoch = ArenaRespawnDriver.CaptureRaidEpoch();
        if (!ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch))
            return;

        var kit = KitCatalog.GetSelectedKit();
        if (kit == null)
        {
            Plugin.ModLogger.LogWarning("Arena: no kit available — raid continues with real gear (still nothing persists).");
            return;
        }

        // No selector: "in place" has to mean the place the player is standing
        // when the swap actually lands, and everything before that point waits
        // (for the draw animation, for the kit's bundles). Deciding the spot up
        // here would teleport them back to where the raid started.
        _initialSwapEpoch = raidEpoch;
        game.StartCoroutine(InitialSwapRoutine(game, kit, raidEpoch));
    }

    private static IEnumerator InitialSwapRoutine(LocalGame game, KitDefinition kit, int raidEpoch)
    {
        try
        {
            yield return game.StartCoroutine(SwapRoutine(
            game,
            kit,
            null,
            null,
            "raid-start kit swap",
            null,
            null,
            raidEpoch));
        }
        finally
        {
            if (_initialSwapEpoch == raidEpoch) _initialSwapEpoch = -1;
        }
    }

    /// <summary>
    /// Round break: a spawn point instead of the spot where the round ended,
    /// with protection that covers the whole pause. Landing somewhere else is
    /// the point — the bots that were shooting at you go on searching the place
    /// you left, which is what makes the pause feel like a pause.
    ///
    /// Returns whether the respawn was actually started. The callback reports
    /// the outcome (true = the player is standing in a new body, protected).
    /// </summary>
    public static bool AtRoundBreak(
        LocalGame game,
        float protectionSeconds,
        Func<bool> stillWanted,
        Action<bool> onComplete)
    {
        int raidEpoch = ArenaRespawnDriver.CaptureRaidEpoch();
        if (!ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch)
            || game.LocalPlayer == null
            || ArenaRespawnDriver.IsRespawnInProgress
            || ArenaRespawnDriver.StandDown)
            return false;

        if (game.LocalPlayer.HealthController == null || !game.LocalPlayer.HealthController.IsAlive)
            return false;

        var kit = KitCatalog.GetSelectedKit();
        if (kit == null)
            return false;

        var here = game.LocalPlayer.Position;
        var spawn = new ArenaSpawnSelector(
            new InfilSpawnSelector(new NearPositionSpawnSelector(() => here, () => 25f)),
            () => here,
            Config.Settings.RespawnMinDistance.Value);

        var protection = new SpawnProtectionOptions
        {
            DurationSeconds = protectionSeconds,
            // A scoreboard is not a fight: drawing a weapon must not end it.
            EndEarlyOnWeapon = false,
            StowAutoDrawnWeapon = false,
            ShowToasts = false,
        };

        game.StartCoroutine(SwapRoutine(
            game,
            kit,
            spawn,
            protection,
            "round respawn",
            stillWanted,
            onComplete,
            raidEpoch));
        return true;
    }

    private static IEnumerator SwapRoutine(
        LocalGame game,
        KitDefinition kit,
        ISpawnSelector spawn,
        SpawnProtectionOptions protection,
        string what,
        Func<bool> stillWanted,
        Action<bool> onComplete,
        int raidEpoch)
    {
        while (Areas.ArenaAreaRuntime.IsPending
               && ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch)
               && !ArenaRespawnDriver.StandDown)
            yield return null;

        if (!ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch)
            || ArenaRespawnDriver.StandDown)
        {
            yield break;
        }

        var oldBody = game.LocalPlayer;
        if (oldBody == null)
        {
            Report(onComplete, false);
            yield break;
        }

        // The game auto-draws a weapon right before OnGameStarted; never start the
        // swap while that hands animation is in flight (HandsController trap).
        var drawDeadline = Time.time + 3f;
        while (oldBody.InventoryController != null
               && oldBody.InventoryController.IsChangingWeapon
               && Time.time < drawDeadline
               && ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch)
               && !ArenaRespawnDriver.StandDown)
        {
            yield return null;
        }

        if (!ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch)
            || ArenaRespawnDriver.StandDown)
        {
            yield break;
        }

        // Kit items never existed in this raid — their bundles/pools must be loaded
        // BEFORE the pipeline, or the replacement's weapon auto-draw NREs.
        yield return KitBundlePreloader.Preload(kit);

        // Everything above yields, and a lot can happen in those frames — most
        // of all dying, which hands the body to the death driver and starts ITS
        // pipeline. Running a second one on top of that is how you end up with
        // two players. Re-check the ground truth right before taking over.
        if (!ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch)
            || ArenaRespawnDriver.StandDown)
        {
            yield break;
        }

        if (ArenaRespawnDriver.IsRespawnInProgress
            || !StillWanted(stillWanted)
            || !ReferenceEquals(game.LocalPlayer, oldBody)
            || oldBody.HealthController == null
            || !oldBody.HealthController.IsAlive)
        {
            Plugin.ModLogger.LogInfo($"Arena {what} stood down — the body changed hands while it was preparing.");
            Report(onComplete, false);
            yield break;
        }

        if (!ArenaRespawnDriver.BeginPipelineUse(game, raidEpoch))
        {
            Plugin.ModLogger.LogInfo($"Arena {what} stood down — its raid lease expired before takeover.");
            if (ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch)
                && !ArenaRespawnDriver.StandDown)
            {
                Report(onComplete, false);
            }
            yield break;
        }

        var requestedSpawn = spawn ?? new FixedSpawnSelector(oldBody.Position, oldBody.Transform.rotation);
        if (!Areas.ArenaAreaRespawn.TryPrepare(game, requestedSpawn, out var preparedSpawn))
        {
            ArenaRespawnDriver.EndPipelineUse(raidEpoch);
            Report(onComplete, false);
            yield break;
        }

        var request = new RespawnRequest
        {
            Game = game,
            DeadPlayer = oldBody,
            // Resolved here, not by the caller: for the raid-start swap "where
            // the player is" means where they are NOW, after the waits above.
            SpawnSelector = preparedSpawn,
            EquipmentStrategy = (profile, dead) => KitBuilder.ApplyToProfile(profile, dead, kit, requireCorpse: false),
            HealthPercent = 100f,
            Protection = protection,
        };

        Exception failure = null;
        yield return game.StartCoroutine(RespawnEngine.RunRespawn(request, e => failure = e));

        bool sameActiveRaid = ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch)
                              && !ArenaRespawnDriver.StandDown;
        ArenaRespawnDriver.EndPipelineUse(raidEpoch);

        if (!sameActiveRaid)
        {
            Plugin.ModLogger.LogInfo(
                $"Arena {what} finished after its raid ended — stale recovery and callbacks were suppressed.");
            yield break;
        }

        if (failure != null)
        {
            Plugin.ModLogger.LogError($"Arena {what} failed: {failure}");
            RecoverOldBody(game, oldBody);
            Report(onComplete, false);
            yield break;
        }

        // The alive pre-swap body would otherwise linger in the scene overlapping the
        // replacement (a death respawn leaves a corpse instead). If it somehow died
        // during the swap window, its GameObject IS the corpse now — leave it alone.
        try
        {
            ArenaRetiredBodyEffects.TryDestroy(game, oldBody);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena: destroying pre-swap body failed: {ex.Message}");
        }

        Plugin.ModLogger.LogInfo($"Arena {what} complete: {kit.Name}.");
        Report(onComplete, true);
    }

    /// <summary>
    /// Whether the reason for this respawn still holds. A round break can expire
    /// while the swap is still preparing — the driver's flag is not raised until
    /// the takeover, so nothing else stops the next round from starting — and
    /// landing the pipeline after that would teleport the player out of a live
    /// fight and hand them a protection nobody asked for. A predicate that
    /// throws counts as "no": standing down is always the safe answer.
    /// </summary>
    private static bool StillWanted(Func<bool> predicate)
    {
        if (predicate == null)
            return true;

        try
        {
            return predicate();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena respawn precondition threw ({ex.Message}) — standing down.");
            return false;
        }
    }

    /// <summary>A throwing subscriber must never take the pipeline down with it.</summary>
    private static void Report(Action<bool> onComplete, bool success)
    {
        try
        {
            onComplete?.Invoke(success);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena respawn callback failed: {ex.Message}");
        }
    }

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
        if (Areas.ArenaAreaRuntime.CurrentArea != null)
        {
            Areas.ArenaAreaRuntime.Abort("live body replacement failed; unsafe rewiring was refused");
            return;
        }
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

            Plugin.ModLogger.LogWarning("Arena: live respawn failed — old body rewired, raid continues with real gear.");
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
            ArenaRetiredBodyEffects.TryDestroy(game, oldBody);

            Plugin.ModLogger.LogWarning("Arena: live respawn failed mid-rewire — recovered onto the replacement body.");
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
