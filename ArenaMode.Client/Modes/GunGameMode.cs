using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Kits;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections;
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

    /// <summary>
    /// True when the rungs carry a weapon and nothing else, so a respawn has to
    /// be given the rung spliced into an ordinary kit. False for the fallback
    /// ladder built from whole kits, where the rung IS the loadout.
    /// </summary>
    private bool _rungsAreWeaponsOnly;
    private int _tier;
    private int _wins;
    private bool _loadoutDirty;
    private float _nextSwapAt;
    private int _consecutiveFailures;
    private bool _givenUp;
    private bool _preloading;

    /// <summary>Which tier the respawn was handed, so a kill during it is not lost.</summary>
    private int _tierHandedToRespawn = -1;

    public string Id => "gungame";

    public string DisplayName => "Gun Game";

    /// <summary>The ladder restarts instead of ending the raid — see OnKill.</summary>
    public bool IsComplete => false;

    public int Tier => _tier;

    public int TierCount => _ladder.Count;

    /// <summary>The rung itself — the weapon the player has earned.</summary>
    public KitDefinition CurrentRung =>
        _ladder.Count > 0 ? _ladder[Mathf.Clamp(_tier, 0, _ladder.Count - 1)] : null;

    /// <summary>
    /// What a respawn should hand out: the ordinary kit for this life with the
    /// earned weapon in it. Only the gun is the ladder's business.
    /// </summary>
    public KitDefinition CurrentKit
    {
        get
        {
            var rung = CurrentRung;
            if (rung == null || !_rungsAreWeaponsOnly)
                return rung;

            return KitMerge.WithPrimaryWeapon(KitCatalog.GetBaseKit(), rung);
        }
    }

    /// <summary>Instant respawn leaves no room for one.</summary>
    public float DeathReplaySeconds => 0f;

    /// <summary>Death costs nothing here — you are back before the body lands.</summary>
    public bool HoldRespawn => false;

    /// <summary>Population comes from the ambient burst spawner.</summary>
    public bool ControlsOwnSpawns => false;

    public string HudStatusLine
    {
        get
        {
            if (_ladder.Count == 0)
                return "Gun Game — no ladder";

            // The RUNG, deliberately: CurrentKit builds a merged loadout on every
            // read, and reading it four times a second would re-roll the random
            // base kit, flood the log and hammer the catalog fetch.
            var kit = CurrentRung;
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
        _ladder = BuildLadder(out _rungsAreWeaponsOnly);
        _tier = 0;
        _wins = 0;
        _loadoutDirty = false;
        _consecutiveFailures = 0;
        _givenUp = false;
        _preloading = false;
        _tierHandedToRespawn = -1;

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
        KitCatalog.KitProvider = () =>
        {
            // Remembered, because a kill can land between here and the moment
            // the player is standing again — see OnLocalRespawn.
            _tierHandedToRespawn = _tier;
            return CurrentKit;
        };

        Plugin.ModLogger.LogInfo($"Gun Game ladder: {_ladder.Count} tiers — {string.Join(" > ", _ladder.Select(k => k.Id))}");
    }

    /// <summary>
    /// The generated weapon ladder, or the kit catalog when there is none. Only
    /// entries that actually build are allowed in: a rung that throws halfway
    /// through the equipment swap would strand the player mid-ladder.
    ///
    /// Internal, and with a quiet mode, because the loading-screen preload asks
    /// it too: the first rung it returns is the weapon the raid opens on, and
    /// warming that rung before the raid means resolving it the same way. The
    /// second, quiet call must not print the mode's own log lines twice.
    /// </summary>
    internal static List<KitDefinition> BuildLadder(out bool weaponsOnly, bool quiet = false)
    {
        var catalog = LadderCatalog.Rungs;
        weaponsOnly = catalog != null && catalog.Count > 0;

        if (!weaponsOnly)
        {
            catalog = KitCatalog.Kits;
            if (!quiet)
                Plugin.ModLogger.LogInfo("Gun Game: no weapon ladder — laddering through the kit catalog instead.");
        }

        if (catalog == null || catalog.Count == 0)
            return new List<KitDefinition>();

        // The selection frozen at arming while a raid is armed or running:
        // the loading screen warmed the first rung of THAT ladder, and a redraw
        // later in the raid climbs the same selection. The live setting only
        // outside a raid.
        var wanted = ArenaSession.GunGameLadderSelection ?? Settings.GunGameLadder.Value;
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
                else if (!quiet)
                    Plugin.ModLogger.LogWarning($"Gun Game ladder: no rung '{id}' — skipped.");
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
            else if (!quiet)
                Plugin.ModLogger.LogWarning($"Gun Game ladder: rung '{kit.Id}' left out — unknown templates.");
        }

        return ladder;
    }

    public void Tick(float deltaTime)
    {
        if (_match == null || !_loadoutDirty || _givenUp || _ladder.Count == 0
            || !ArenaSession.IsActive)
            return;

        var host = Comfort.Common.Singleton<AbstractGame>.Instantiated
            ? Comfort.Common.Singleton<AbstractGame>.Instance as LocalGame
            : null;
        if (host == null || host.Status != GameStatus.Started)
            return;

        if (Time.time < _nextSwapAt || _preloading || Combat.WeaponSwapService.IsSwapping)
            return;

        // A respawn is already handing out the current tier's whole kit; swapping
        // a weapon into a body that is being rebuilt would race it.
        if (Respawn.ArenaRespawnDriver.IsRespawnInProgress)
            return;

        var player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
        if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
            return;

        TrySwapToCurrentTier(host, player);
    }

    private void TrySwapToCurrentTier(LocalGame host, Player player)
    {
        var kit = CurrentRung;
        if (kit == null)
        {
            _loadoutDirty = false;
            return;
        }

        int raidEpoch = ArenaRespawnDriver.CaptureRaidEpoch();
        if (!IsCurrentRaidWork(host, player, raidEpoch))
            return;

        _preloading = true;
        host.StartCoroutine(SwapAfterPreload(host, player, kit, raidEpoch));
    }

    /// <summary>
    /// The bundles come first, always.
    ///
    /// A weapon whose assets were never loaded into this raid has no prefab
    /// pool, so the hands-controller factory hands back null and the draw dies
    /// on it — and it dies AFTER the game has already opened a weapon-change
    /// operation and before anything closes it. The player is then left with
    /// empty hands and a change that never finishes, which blocks every weapon,
    /// medkit and grenade for the rest of the raid. Both respawn paths preload
    /// for exactly this reason; the promotion path has to as well, and it cannot
    /// lean on the background warmup, which only runs for random kits.
    /// </summary>
    private IEnumerator SwapAfterPreload(
        LocalGame host,
        Player player,
        KitDefinition kit,
        int raidEpoch)
    {
        KitDefinition current;

        // try/finally, not a flag cleared on each exit: every path out of this
        // loop — including the coroutine being stopped when the player object
        // dies — has to release the gate, or Tick never starts another swap.
        try
        {
            while (true)
            {
                // A no-op once this kit is warm.
                yield return KitBundlePreloader.Preload(kit);

                // The preload yields, sometimes for seconds. Re-check everything.
                if (!IsCurrentRaidWork(host, player, raidEpoch)
                    || player == null
                    || player.HealthController == null
                    || !player.HealthController.IsAlive
                    || Combat.WeaponSwapService.IsSwapping
                    || Respawn.ArenaRespawnDriver.IsRespawnInProgress
                    || !ReferenceEquals(Comfort.Common.Singleton<GameWorld>.Instance?.MainPlayer, player))
                {
                    yield break;
                }

                current = CurrentRung;
                if (current == null)
                {
                    _loadoutDirty = false;
                    yield break;
                }

                // Whatever the tier is NOW is what the player gets, so a burst of
                // kills collapses into one swap — but it has to be a rung THIS
                // routine warmed. Kills land freely across the yield above, and
                // after a ladder redraw every rung is a weapon the raid has never
                // built, so taking the new tier without warming it would hand the
                // draw a weapon with no prefab pool: exactly the softlock the
                // comment above this method exists to prevent. Loop instead, and
                // it converges — each pass warms one more rung.
                if (ReferenceEquals(current, kit))
                    break;

                kit = current;
            }
        }
        finally
        {
            _preloading = false;
        }

        // Building an item tree is cheap compared with applying it, but it still
        // belongs to this raid. More importantly, the following Swap edits the
        // live inventory, so a preload continuation from Stopping/GameEnd may
        // never reach either operation.
        if (!IsCurrentRaidWork(host, player, raidEpoch))
            yield break;

        var tierAtSwap = _tier;

        Weapon weapon;
        List<Item> spares;

        try
        {
            weapon = KitBuilder.BuildPrimaryWeapon(current, Settings.GunGameSpareMagazines.Value, out spares);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Gun Game: building tier {tierAtSwap + 1} ({current.Id}) failed: {ex.Message}");
            weapon = null;
            spares = null;
        }

        if (weapon == null)
        {
            OnSwapFailed(current);
            yield break;
        }

        _nextSwapAt = Time.time + SwapRetrySeconds;

        // A refusal here means "busy" (reloading, mid weapon change) — not a
        // failure worth counting, the dirty flag brings us back in a moment.
        Combat.WeaponSwapService.Swap(
            host,
            raidEpoch,
            player,
            weapon,
            spares,
            success =>
            {
                // WeaponSwapService already suppresses stale callbacks. Keep the
                // mode boundary explicit too: this object must never publish a
                // tier/toast after its LocalGame stopped.
                if (IsCurrentRaidWork(host, player, raidEpoch))
                    OnSwapFinished(success, tierAtSwap, current);
            });
    }

    private static bool IsCurrentRaidWork(LocalGame host, Player player, int raidEpoch)
    {
        if (!ArenaRespawnDriver.IsSameActiveRaid(host, raidEpoch)
            || !Comfort.Common.Singleton<AbstractGame>.Instantiated
            || !ReferenceEquals(Comfort.Common.Singleton<AbstractGame>.Instance, host)
            || !Comfort.Common.Singleton<GameWorld>.Instantiated)
        {
            return false;
        }

        return ReferenceEquals(Comfort.Common.Singleton<GameWorld>.Instance?.MainPlayer, player);
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

            var fresh = RedrawLadder();
            ArenaToasts.Show(fresh
                ? $"Ladder complete x{_wins} — new weapons!"
                : (_wins == 1 ? "Ladder complete — starting over!" : $"Ladder complete x{_wins} — starting over!"));
        }

        _loadoutDirty = true;

        // Named, not numbered. Without this the spare-magazine warnings that fire
        // during a promotion sit in the log next to nothing that says which weapon
        // was being handed over, which made seven of them unattributable across a
        // whole raid.
        Plugin.ModLogger.LogInfo(
            $"Gun Game: promoted to tier {_tier + 1}/{_ladder.Count} - {CurrentRung?.Name ?? CurrentRung?.Id}.");

        // The swap itself waits out the kill animation on its own; this just
        // keeps it from starting in the same frame as the shot that earned it.
        _nextSwapAt = Mathf.Max(_nextSwapAt, Time.time + 0.25f);
    }

    /// <summary>
    /// Cuts a fresh ladder for the next climb.
    ///
    /// Only for a real weapon ladder: when Gun Game is walking the KIT catalogue
    /// instead — which it does when no ladder.json exists — there is no pool to
    /// draw a different one from, and asking would return the same list.
    ///
    /// The tier has already wrapped to zero by the time this runs, so replacing
    /// the list underneath is safe; and _loadoutDirty is set by the caller, so
    /// the swap that follows picks up the new rung one rather than the old one.
    /// </summary>
    private bool RedrawLadder()
    {
        if (!_rungsAreWeaponsOnly || !Settings.GunGameRedrawLadder.Value)
            return false;

        if (!LadderCatalog.Redraw())
            return false;

        // Built into a local first. BuildLadder drops rungs whose templates the
        // client cannot resolve, so a fresh draw can in principle come back
        // empty — and committing that would leave the mode with nothing to hand
        // out for the rest of the raid. The old ladder is a worse prize than a
        // new one, but it is not nothing.
        var rebuilt = BuildLadder(out var weaponsOnly);
        if (rebuilt.Count == 0)
        {
            Plugin.ModLogger.LogWarning("Gun Game: the new ladder had no buildable rungs — keeping the old one.");
            return false;
        }

        _ladder = rebuilt;
        _rungsAreWeaponsOnly = weaponsOnly;

        // The new rungs are weapons this raid has never built. Warming them now,
        // while the player is on rung one with a pistol, is what keeps the
        // promotions that follow from waiting on a cold bundle.
        ArenaController.RequestKitWarmup();

        Plugin.ModLogger.LogInfo(
            $"Gun Game: new ladder drawn — {string.Join(" > ", _ladder.Select(k => k.Name ?? k.Id))}");

        return true;
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
        _preloading = false;
        _consecutiveFailures = 0;

        // The spares handed out in this life stay with the corpse. Forgetting
        // them is the point: the next promotion must not go looking for
        // magazines that now belong to a body on the floor.
        Combat.WeaponSwapService.ForgetSpares();
    }

    public void OnLocalRespawn(Player player)
    {
        // The respawn handed out whatever tier was current when it ASKED, and a
        // kill can land in the seconds between that question and the player
        // standing again. Clearing the flag unconditionally would leave them
        // holding one rung below what the scoreboard says until their next kill.
        _loadoutDirty = _tierHandedToRespawn >= 0 && _tier != _tierHandedToRespawn;
        _tierHandedToRespawn = -1;
    }

    public void OnMatchEnd()
    {
        _preloading = false;
        KitCatalog.KitProvider = null;
        Combat.WeaponSwapService.Reset();
        Plugin.ModLogger.LogInfo($"Gun Game ended on tier {_tier + 1}/{Mathf.Max(1, _ladder.Count)}, {_wins} ladders completed.");
    }
}
