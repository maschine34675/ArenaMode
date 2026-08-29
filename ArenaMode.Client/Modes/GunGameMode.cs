using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Kits;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Modes;

/// <summary>
/// Gun Game: every kill promotes you to the next weapon on the ladder, and the
/// ladder is the whole game. Reach the top and it starts over, with the wins
/// kept on the board.
///
/// The tier index is the single source of truth. Kills keep landing while a
/// weapon swap is still animating, so nothing is queued: a kill raises the tier
/// and marks the loadout dirty, and the swap always builds whatever tier is
/// current at the moment it runs. Three kills during one swap therefore produce
/// one swap to the third gun, not three swaps racing each other through the
/// player's hands.
///
/// Dying does not go through the swap at all — the respawn rebuilds the whole
/// loadout, and it asks the kit catalog for the kit to use. The mode answers
/// that question while it runs, so a respawn hands back the tier the player had
/// reached instead of a random kit.
/// </summary>
public sealed class GunGameMode : IArenaMode
{
    /// <summary>After this many consecutive failures the mode stops fighting the inventory.</summary>
    private const int MaxConsecutiveSwapFailures = 3;

    private const float SwapRetrySeconds = 1f;

    private ArenaMatch _match;
    private List<KitDefinition> _ladder = new List<KitDefinition>();
    private int _tier;
    private int _wins;
    private bool _loadoutDirty;
    private float _nextSwapAt;
    private int _consecutiveFailures;
    private bool _givenUp;

    public string Id => "gungame";

    public string DisplayName => "Gun Game";

    /// <summary>The ladder restarts instead of ending the raid — see OnKill.</summary>
    public bool IsComplete => false;

    public int Tier => _tier;

    public int TierCount => _ladder.Count;

    public KitDefinition CurrentKit =>
        _ladder.Count > 0 ? _ladder[Mathf.Clamp(_tier, 0, _ladder.Count - 1)] : null;

    public string HudStatusLine
    {
        get
        {
            if (_ladder.Count == 0)
                return "Gun Game — no ladder";

            var kit = CurrentKit;
            var line = $"Tier {_tier + 1}/{_ladder.Count}";
            if (kit != null)
                line += $"   {kit.Name ?? kit.Id}";

            if (_wins > 0)
                line += $"   {_wins}x complete";

            return line;
        }
    }

    public void OnMatchStart(ArenaMatch match)
    {
        _match = match;
        _ladder = BuildLadder();
        _tier = 0;
        _wins = 0;
        _loadoutDirty = false;
        _consecutiveFailures = 0;
        _givenUp = false;

        if (_ladder.Count == 0)
        {
            Plugin.ModLogger.LogError(
                "Gun Game has no buildable kits to make a ladder from — the mode will do nothing. "
                + "Check the kit catalog and the GunGameLadder setting.");
            return;
        }

        // From here on every kit the respawn asks for is the current tier's, so
        // dying costs progress only if the config says it should. Installed
        // before the raid-start kit swap runs, which is what puts the player on
        // tier 1 in the first place.
        KitCatalog.KitProvider = () => CurrentKit;

        Plugin.ModLogger.LogInfo($"Gun Game ladder: {_ladder.Count} tiers — {string.Join(" > ", _ladder.Select(k => k.Id))}");
    }

    /// <summary>
    /// The configured ladder, or the whole catalog in its own order. Only kits
    /// that actually build are allowed in: a tier that throws halfway through
    /// the equipment swap would strand the player mid-ladder.
    /// </summary>
    private static List<KitDefinition> BuildLadder()
    {
        var catalog = KitCatalog.Kits;
        if (catalog == null || catalog.Count == 0)
            return new List<KitDefinition>();

        var wanted = Settings.GunGameLadder.Value;
        IEnumerable<KitDefinition> ordered;

        if (string.IsNullOrWhiteSpace(wanted))
        {
            ordered = catalog;
        }
        else
        {
            var picked = new List<KitDefinition>();
            foreach (var id in wanted.Split(',').Select(part => part.Trim()).Where(part => part.Length > 0))
            {
                var kit = catalog.FirstOrDefault(k => string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase));
                if (kit != null)
                    picked.Add(kit);
                else
                    Plugin.ModLogger.LogWarning($"Gun Game ladder: no kit '{id}' in the catalog — skipped.");
            }

            // An entirely unusable ladder setting must not leave the mode dead.
            ordered = picked.Count > 0 ? picked : catalog;
        }

        var ladder = new List<KitDefinition>();
        foreach (var kit in ordered)
        {
            var invalid = KitBuilder.ValidateKit(kit);
            if (invalid.Count == 0)
                ladder.Add(kit);
            else
                Plugin.ModLogger.LogWarning($"Gun Game ladder: kit '{kit.Id}' left out — unknown templates.");
        }

        return ladder;
    }

    public void Tick(float deltaTime)
    {
        if (_match == null || !_loadoutDirty || _givenUp || _ladder.Count == 0)
            return;

        if (Time.time < _nextSwapAt || Combat.WeaponSwapService.IsSwapping)
            return;

        // A respawn is already handing out the current tier's whole kit; swapping
        // a weapon into a body that is being rebuilt would race it.
        if (Respawn.ArenaRespawnDriver.IsRespawnInProgress)
            return;

        var player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
        if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
            return;

        TrySwapToCurrentTier(player);
    }

    private void TrySwapToCurrentTier(Player player)
    {
        var kit = CurrentKit;
        if (kit == null)
        {
            _loadoutDirty = false;
            return;
        }

        // Built here rather than when the kill landed: whatever the tier is NOW
        // is what the player gets, so a burst of kills collapses into one swap.
        var tierAtSwap = _tier;
        Weapon weapon;
        List<Item> spares;

        try
        {
            weapon = KitBuilder.BuildPrimaryWeapon(kit, Settings.GunGameSpareMagazines.Value, out spares);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Gun Game: building tier {tierAtSwap + 1} ({kit.Id}) failed: {ex.Message}");
            weapon = null;
            spares = null;
        }

        if (weapon == null)
        {
            OnSwapFailed(kit);
            return;
        }

        _nextSwapAt = Time.time + SwapRetrySeconds;

        if (!Combat.WeaponSwapService.Swap(player, weapon, spares, success => OnSwapFinished(success, tierAtSwap, kit)))
        {
            // Busy right now (reloading, mid weapon change) — not a failure worth
            // counting, just try again shortly.
            return;
        }
    }

    private void OnSwapFinished(bool success, int tierAtSwap, KitDefinition kit)
    {
        if (!success)
        {
            OnSwapFailed(kit);
            return;
        }

        _consecutiveFailures = 0;

        // Kills that landed while the swap ran already moved the tier on — stay
        // dirty so the next tick catches up.
        _loadoutDirty = _tier != tierAtSwap;

        ArenaToasts.Show($"Tier {tierAtSwap + 1}/{_ladder.Count} — {kit.Name ?? kit.Id}");
    }

    /// <summary>
    /// A tier that will not build is skipped rather than retried forever: being
    /// stuck one gun short beats a ladder that never moves again. If skipping
    /// does not help either, the mode stops touching the inventory and the
    /// player finishes the raid with whatever they are holding.
    /// </summary>
    private void OnSwapFailed(KitDefinition kit)
    {
        _consecutiveFailures++;
        _nextSwapAt = Time.time + SwapRetrySeconds;

        if (_consecutiveFailures < MaxConsecutiveSwapFailures)
            return;

        _consecutiveFailures = 0;

        if (_tier + 1 < _ladder.Count)
        {
            Plugin.ModLogger.LogWarning($"Gun Game: skipping tier {_tier + 1} ({kit?.Id}) — it would not swap in.");
            _tier++;
            return;
        }

        Plugin.ModLogger.LogError("Gun Game: no tier would swap in — leaving the player with their current weapon.");
        _givenUp = true;
        _loadoutDirty = false;
    }

    public void OnKill(ArenaKill kill)
    {
        if (_match == null || _ladder.Count == 0 || !kill.KilledByLocal || kill.LocalIsVictim)
            return;

        _tier++;

        if (_tier >= _ladder.Count)
        {
            _wins++;
            _tier = 0;
            Plugin.ModLogger.LogInfo($"Gun Game ladder completed ({_wins}x).");
            ArenaToasts.Show(_wins == 1
                ? "Ladder complete — starting over!"
                : $"Ladder complete x{_wins} — starting over!");
        }

        _loadoutDirty = true;

        // The swap itself waits out the kill animation on its own; this just
        // keeps it from starting in the same frame as the shot that earned it.
        _nextSwapAt = Mathf.Max(_nextSwapAt, Time.time + 0.25f);
    }

    public void OnLocalDeath()
    {
        if (_ladder.Count == 0)
            return;

        if (Settings.GunGameLoseTierOnDeath.Value && _tier > 0)
        {
            _tier--;
            ArenaToasts.Show($"Demoted to tier {_tier + 1}");
        }

        // The respawn rebuilds the whole loadout from the tier kit, so there is
        // nothing left for a weapon swap to do.
        _loadoutDirty = false;
        _givenUp = false;
        _consecutiveFailures = 0;
    }

    public void OnLocalRespawn()
    {
        // Whatever the respawn handed out came from CurrentKit — already correct.
        _loadoutDirty = false;
    }

    public void OnMatchEnd()
    {
        KitCatalog.KitProvider = null;
        Combat.WeaponSwapService.Reset();
        Plugin.ModLogger.LogInfo($"Gun Game ended on tier {_tier + 1}/{Mathf.Max(1, _ladder.Count)}, {_wins} ladders completed.");
    }
}
