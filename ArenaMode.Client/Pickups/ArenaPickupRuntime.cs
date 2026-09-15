using ArenaMode.Areas;
using ArenaMode.Combat;
using ArenaMode.Kits;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Pickups;

/// <summary>Temporary scene/appearance owner. No actor inventory survives its native life.</summary>
internal static class ArenaPickupRuntime
{
    private sealed class Entry
    {
        internal ArenaPickupPoint Definition;
        internal Vector3 Floor;
        internal ArenaPickupState State = new();
        internal ArenaPickupView View;
        internal ArenaWeaponPickup.Handle Weapon;
        internal bool Preparing, NeedsEntry = true;
        internal readonly HashSet<Player> MustLeave = new();
    }
    private sealed class ActorSample
    {
        internal Vector3 Position;
        internal float At;
        internal bool Fresh;
    }

    private static ArenaMatch _match, _disabledMatch;
    private static LocalGame _game;
    private static GameWorld _world;
    private static ArenaPickupLayout _layout;
    private static bool _live, _faulted, _placementPending;
    private static int _round;
    private static float _nextScan, _placementDeadline, _nextPlacement;
    private static readonly List<Entry> Entries = new();
    private static readonly Dictionary<Player, ActorSample> Actors = new();
    internal static int Generation { get; private set; }
    internal static string LastNotice { get; private set; }
    internal static float NoticeUntil { get; private set; }

    private static bool TryContext(out ArenaMatch match, out LocalGame game, out GameWorld world)
    {
        match = ArenaMatchService.Current;
        game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        return ArenaSession.IsActive && match != null && game != null && world != null
            && game.Status == GameStatus.Started && ReferenceEquals(game.GameWorld, world)
            && ArenaSession.IsExactActiveRaid(game, world) && !ArenaMatchService.TeamStartAbortPending
            && !ArenaAreaRuntime.IsPending && !Compat.PitFireTeamCompat.TeamArenaActive
            && (match.Mode is RoundsMode || match.Mode is TeamDeathmatchMode);
    }

    private static bool PhaseLive(ArenaMatch match) => match?.Mode switch
    {
        RoundsMode dm => dm.AssistScoringOpen && !dm.IsComplete,
        TeamDeathmatchMode tdm => tdm.AssistScoringOpen && !tdm.IsComplete,
        _ => false
    };

    private static int Round(ArenaMatch match) => match.Mode is RoundsMode dm ? dm.Round.Number : 1;

    internal static bool IsLiveActor(Player player)
    {
        if (!_live || _faulted || ArenaPickupSettings.Enabled?.Value != true || player == null
            || !TryContext(out var match, out var game, out var world) || !PhaseLive(match)
            || !ReferenceEquals(match, _match) || !ReferenceEquals(game, _game)
            || !ReferenceEquals(world, _world) || Round(match) != _round
            || player.HealthController?.IsAlive != true || string.IsNullOrWhiteSpace(player.ProfileId)
            || !ReferenceEquals(world.GetAlivePlayerByProfileID(player.ProfileId), player)
            || !ReferenceEquals(world.GetEverExistedPlayerByID(player.ProfileId), player)) return false;
        if (player.IsAI)
        {
            var bot = player.AIData?.BotOwner;
            return !player.IsYourPlayer && bot != null && bot.BotState == EBotState.Active
                && ReferenceEquals(bot.GetPlayer, player);
        }
        return player.IsYourPlayer && ReferenceEquals(world.MainPlayer, player)
            && ReferenceEquals(game.LocalPlayer, player);
    }

    internal static void Tick()
    {
        try
        {
            if (!TryContext(out var match, out var game, out var world))
            {
                Reset();
                if (ArenaMatchService.Current == null) _disabledMatch = null;
                return;
            }
            if (ArenaPickupSettings.Enabled?.Value != true)
            {
                _disabledMatch = match;
                Reset();
                return;
            }
            if (ReferenceEquals(_disabledMatch, match)) return;
            if (!ReferenceEquals(match, _match) || !ReferenceEquals(game, _game) || !ReferenceEquals(world, _world))
            {
                Reset();
                _match = match; _game = game; _world = world;
                LoadLayout(world.LocationId, ArenaAreaRuntime.CurrentArea?.Id);
            }
            if (_faulted) return;
            if (!PhaseLive(match))
            {
                if (_live) ClearRound();
                return;
            }
            if (!_live || _round != Round(match))
            {
                ClearRound();
                _round = Round(match); _live = true;
                _placementPending = _layout != null;
                _placementDeadline = Time.time + 15f;
                _nextPlacement = 0;
            }
            if (_placementPending && Time.time >= _nextPlacement)
            {
                _nextPlacement = Time.time + .25f;
                if (ArenaPickupGeometry.TryAnchor(_world, out var anchor))
                { _placementPending = false; BeginRound(anchor); }
                else if (Time.time >= _placementDeadline)
                {
                    _placementPending = false;
                    Plugin.ModLogger.LogWarning("Arena pickups skipped this round: no grounded local navigation anchor within 15 seconds.");
                }
            }
            foreach (var entry in Entries)
            {
                if (entry.State.Tick(Time.time))
                {
                    entry.NeedsEntry = true;
                    if (entry.Definition.Kind == ArenaPickupKind.Weapon) PrepareWeapon(entry);
                }
                if (entry.State.Phase == ArenaPickupPhase.Retired && entry.Weapon != null)
                { entry.Weapon.Dispose(); entry.Weapon = null; }
                entry.View?.Tick(entry.State, entry.Preparing, entry.Weapon?.Visual);
            }
            if (Time.time < _nextScan || Time.timeScale <= 0f) return;
            _nextScan = Time.time + .075f;
            ScanActors();
        }
        catch (Exception ex)
        {
            if (!_faulted) Plugin.ModLogger.LogWarning("Arena pickups stopped for this match: " + ex.GetBaseException().Message);
            ClearRound(); _faulted = true;
        }
    }

    private static void LoadLayout(string map, string area)
    {
        string path = Path.Combine(Path.GetDirectoryName(Plugin.Instance.Info.Location), "ArenaMode", "Pickups.json");
        ArenaPickupLayoutFile file;
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length > 512 * 1024) throw new InvalidDataException("Pickups.json exceeds 512 KiB.");
            file = ArenaPickupLayouts.Parse(File.ReadAllText(path));
        }
        else file = ArenaPickupLayouts.Factory(ArenaPickupSettings.WeaponKit.Value);
        _layout = ArenaPickupLayouts.Select(file, map, area);
        Plugin.ModLogger.LogInfo($"Arena pickups layout: {map}/{area ?? "full map"}; {_layout?.Points.Count ?? 0} configured points. Frozen for this match.");
    }

    private static void BeginRound(Vector3 anchor)
    {
        if (_layout == null) return;
        foreach (var point in _layout.Points)
        {
            if (!ArenaPickupGeometry.TryPlace(new Vector3(point.X, point.Y, point.Z), anchor,
                out var floor, out var reason))
            {
                Plugin.ModLogger.LogWarning($"Arena pickup '{point.Id}' skipped: {reason}");
                continue;
            }
            var entry = new Entry { Definition = point, Floor = floor };
            Entries.Add(entry);
            entry.View = new ArenaPickupView(point, floor);
            if (point.Kind == ArenaPickupKind.Weapon) PrepareWeapon(entry);
        }
        Plugin.ModLogger.LogInfo($"Arena pickups round {_round}: {Entries.Count}/{_layout.Points.Count} points placed; damage 20s, shield 100/20s, weapon auto-take local only.");
    }

    private static bool Owns(int generation, Entry entry) => _live && Generation == generation
        && Entries.Contains(entry) && TryContext(out var match, out _, out _) && ReferenceEquals(match, _match)
        && PhaseLive(match) && Round(match) == _round && ArenaPickupSettings.Enabled?.Value == true;

    private static void PrepareWeapon(Entry entry)
    {
        if (entry.Preparing) return;
        entry.Weapon?.Dispose(); entry.Weapon = null;
        var kit = KitCatalog.Kits?.FirstOrDefault(candidate => string.Equals(candidate.Id, entry.Definition.KitId, StringComparison.Ordinal));
        if (kit == null)
        {
            entry.State.Retire();
            Plugin.ModLogger.LogWarning($"Arena pickup '{entry.Definition.Id}' disabled: kit '{entry.Definition.KitId}' missing.");
            return;
        }
        entry.Preparing = true;
        int generation = Generation;
        _game.StartCoroutine(PrepareRoutine());

        IEnumerator PrepareRoutine()
        {
            // Never let optional pickup pooling compete with a required kit or
            // opening bot load; this coroutine remains owned by the exact round.
            while (Owns(generation, entry) && (KitBundlePreloader.IsPreloading || ArenaRespawnDriver.IsRespawnInProgress))
                yield return null;
            if (!Owns(generation, entry)) yield break;
            yield return ArenaWeaponPickup.Prepare(_world, kit, entry.Floor + Vector3.up * .65f,
                Quaternion.Euler(0, entry.Definition.Yaw, 0), () => Owns(generation, entry), weapon =>
                {
                    if (!Owns(generation, entry)) { weapon?.Dispose(); return; }
                    entry.Preparing = false; entry.NeedsEntry = true;
                    entry.Weapon = weapon;
                    if (weapon == null) entry.State.Retire();
                });
        }
    }

    private static void ScanActors()
    {
        var live = _world.AllAlivePlayersList.Where(IsLiveActor).ToArray();
        foreach (var old in Actors.Keys.Where(player => !live.Contains(player)).ToArray()) Actors.Remove(old);
        foreach (var player in live)
        {
            if (!Actors.ContainsKey(player)) Actors[player] = new ActorSample { Position = player.Position, At = Time.time, Fresh = true };
        }
        foreach (var entry in Entries)
        {
            foreach (var old in entry.MustLeave.Where(player => !Actors.ContainsKey(player)).ToArray()) entry.MustLeave.Remove(old);
            bool ready = entry.State.Phase == ArenaPickupPhase.Available && !entry.Preparing
                && (entry.Definition.Kind != ArenaPickupKind.Weapon || entry.Weapon?.IsAvailable == true);
            Player closest = null;
            float best = float.MaxValue;
            foreach (var player in live)
            {
                var sample = Actors[player];
                var current = player.Position;
                bool inside = Reaches(current, current, entry.Floor, false, out _);
                if ((sample.Fresh || entry.NeedsEntry) && inside) entry.MustLeave.Add(player);
                // Leaving rearms only a later approach. The outward sample still
                // crosses the point and must never count as immediate re-entry.
                if (!inside && (current - entry.Floor).sqrMagnitude > 2.25f && entry.MustLeave.Remove(player)) continue;
                if (!ready || entry.MustLeave.Contains(player) || !CanCollect(player, entry.Definition.Kind)) continue;
                if (!Reaches(sample.Position, current, entry.Floor, !sample.Fresh && Time.time - sample.At <= .25f, out float fraction)) continue;
                var contact = Vector3.Lerp(sample.Position, current, fraction);
                if (!ArenaPickupGeometry.LineClear(contact + Vector3.up * .6f, entry.Floor + Vector3.up * .6f)) continue;
                // A sampled segment can cut a corner even when the native actor
                // walked around it. Interpolated contact alone must not reach through a wall.
                if (!ArenaPickupGeometry.LineClear(current + Vector3.up * .6f, entry.Floor + Vector3.up * .6f)) continue;
                float distance = (current - entry.Floor).sqrMagnitude;
                if (distance < best) { closest = player; best = distance; }
            }
            if (ready) entry.NeedsEntry = false;
            if (closest != null) Collect(entry, closest);
        }
        foreach (var player in live)
        {
            var sample = Actors[player]; sample.Position = player.Position; sample.At = Time.time; sample.Fresh = false;
        }
    }

    private static bool Reaches(Vector3 from, Vector3 to, Vector3 point, bool continuous, out float fraction)
        => ArenaPickupState.InReach(from.x, from.y, from.z, to.x, to.y, to.z, point.x, point.y, point.z, continuous, out fraction);

    private static bool CanCollect(Player player, ArenaPickupKind kind)
    {
        if (!IsLiveActor(player) || player.ActiveHealthController == null || player.ActiveHealthController.DamageCoeff <= 0f) return false;
        if (player.IsAI) return kind != ArenaPickupKind.Weapon && ArenaPickupSettings.BotsCollectPowerups.Value;
        return Hud.ArenaHudVisibility.IsBattleUiActive && !ArenaRespawnDriver.IsRespawnInProgress && !WeaponSwapService.IsSwapping
            && player.InventoryController != null && !player.InventoryController.IsChangingWeapon;
    }

    private static void Collect(Entry entry, Player player)
    {
        int reservation = entry.State.Reserve(Time.time);
        if (reservation == 0) return;
        entry.MustLeave.Add(player);
        int generation = Generation;
        if (entry.Definition.Kind == ArenaPickupKind.Weapon)
        {
            if (!entry.Weapon.TryTake(player, () => Owns(generation, entry) && CanCollect(player, ArenaPickupKind.Weapon), Complete))
                Complete(false);
        }
        else Complete(entry.Definition.Kind == ArenaPickupKind.DoubleDamage
            ? ArenaPowerups.GrantDamage(player, 20f) : ArenaPowerups.GrantShield(player, 100f, 20f));

        void Complete(bool success)
        {
            if (!Owns(generation, entry)) return;
            if (!entry.State.Complete(reservation, success, Time.time, entry.Definition.RespawnSeconds)) return;
            if (!success && entry.Definition.Kind == ArenaPickupKind.Weapon && entry.Weapon?.IsAvailable != true)
            {
                entry.State.Retire();
                entry.Weapon?.Dispose();
                Plugin.ModLogger.LogWarning($"Arena pickup '{entry.Definition.Id}' retired for this round after unconfirmed weapon ownership.");
                return;
            }
            if (success)
            {
                if (!player.IsAI) Notice(entry.Definition.Kind switch
                {
                    ArenaPickupKind.DoubleDamage => "DOUBLE DAMAGE — 20s",
                    ArenaPickupKind.Shield => "SHIELD — 100 / 20s",
                    _ => "WEAPON COLLECTED"
                });
                Plugin.ModLogger.LogInfo($"Arena pickup collected: {entry.Definition.Id}; actor {(player.IsAI ? "bot" : "local")}; round {_round}; returns in {entry.Definition.RespawnSeconds:0}s.");
            }
            else if (!player.IsAI && entry.Definition.Kind == ArenaPickupKind.Weapon)
                Notice("WEAPON LEFT — free an inactive weapon slot, then approach again");
        }
    }

    private static void Notice(string text) { LastNotice = text; NoticeUntil = Time.unscaledTime + 3f; }

    internal static bool OwnsCollider(Collider collider)
    {
        if (collider == null) return false;
        foreach (var entry in Entries)
        {
            var visual = entry.Weapon?.Visual;
            if (visual != null && collider.transform.IsChildOf(visual.transform)) return true;
        }
        return false;
    }

    private static void ClearRound()
    {
        unchecked { Generation++; }
        _live = false;
        ArenaPowerups.Reset();
        foreach (var entry in Entries)
        {
            entry.State.Retire();
            try { entry.Weapon?.Dispose(); } catch (Exception ex) { Plugin.ModLogger.LogWarning("Arena pickup cleanup: " + ex.GetBaseException().Message); }
            entry.View?.Dispose();
        }
        Entries.Clear(); Actors.Clear(); _nextScan = 0; _placementPending = false;
        LastNotice = null; NoticeUntil = 0;
    }

    internal static void Reset()
    {
        if (_match != null || _live || Entries.Count != 0) ClearRound();
        _match = null; _game = null; _world = null; _layout = null; _round = 0; _faulted = false;
    }
}
