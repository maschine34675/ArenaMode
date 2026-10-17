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
internal static partial class ArenaPickupRuntime
{
    private sealed class Entry
    {
        internal ArenaPickupPoint Definition;
        internal ArenaPickupPoint Candidates;
        internal Vector3 Floor;
        internal ArenaPickupState State = new();
        internal ArenaPickupView View;
        internal ArenaWeaponPickup.Handle Weapon;
        internal bool Preparing, Rotating, NeedsEntry = true;
        internal float NextRotationAt;
        internal float AvailableSince;
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
    private static bool _live, _faulted, _placementPending, _builtInLayout;
    private static int _round;
    private static float _nextScan, _placementDeadline, _nextPlacement;
    private static int _placementAttempts;
    private static readonly Dictionary<string, string> PlacementFailures = new(StringComparer.Ordinal);
    private static readonly List<Entry> Entries = new();
    private static readonly System.Random PositionRandom = new(); // Never change Unity's shared RNG state.
    private const float AvailableRotationSeconds = 60f;
    private static readonly Dictionary<Player, ActorSample> Actors = new();
    internal static int Generation { get; private set; }
    internal static bool BotObjectivesAvailable { get; set; }
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
            ArenaPickupAudio.Tick();
            if (_placementPending && Time.timeScale > 0f)
            {
                // The window bounds placement as well as anchor acquisition.
                // A valid anchor on a disconnected island is not a successful setup.
                if (Time.time >= _placementDeadline) FinishPlacement("15s placement window expired");
                else if (Time.time >= _nextPlacement)
                {
                    _nextPlacement = Time.time + .25f;
                    if (!ArenaRespawnDriver.IsRespawnInProgress && !WeaponSwapService.IsSwapping
                        && ArenaPickupGeometry.TryAnchor(_world, out var anchor))
                    {
                        _nextPlacement = Time.time + 1f;
                        PlaceMissing(anchor);
                        if (Entries.Count == _layout.Points.Count) FinishPlacement("complete");
                    }
                }
            }
            int tickGeneration = Generation;
            foreach (var entry in Entries)
            {
                if (entry.State.Tick(Time.time))
                {
                    entry.NeedsEntry = true;
                    entry.AvailableSince = Time.time;
                    entry.NextRotationAt = Time.time + AvailableRotationSeconds;
                    TryRotate(entry, rearming: true);
                    if (entry.Definition.Kind == ArenaPickupKind.Weapon) PrepareWeapon(entry);
                }
                else if (entry.Rotating && entry.State.Phase == ArenaPickupPhase.Available && !entry.Preparing
                    && (entry.Definition.Kind != ArenaPickupKind.Weapon || entry.Weapon?.IsAvailable == true)
                    && Time.timeScale > 0f && Time.time >= entry.NextRotationAt)
                {
                    // Bounded retries also retain an available marker when all
                    // other authored positions are obstructed or reserved.
                    entry.NextRotationAt = Time.time + AvailableRotationSeconds;
                    if (TryRotate(entry) && entry.Definition.Kind == ArenaPickupKind.Weapon) PrepareWeapon(entry);
                }
                if (entry.State.Phase == ArenaPickupPhase.Retired && entry.Weapon != null)
                { entry.Weapon.Dispose(); entry.Weapon = null; }
                if (tickGeneration != Generation) return;
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
        string path = Path.Combine(ArenaAssetPaths.FromPluginFile(Plugin.Instance.Info.Location), "Pickups.json");
        ArenaPickupLayoutFile file;
        _builtInLayout = !File.Exists(path);
        if (!_builtInLayout)
        {
            if (new FileInfo(path).Length > 512 * 1024) throw new InvalidDataException("Pickups.json exceeds 512 KiB.");
            file = ArenaPickupLayouts.Parse(File.ReadAllText(path));
        }
        else file = ArenaPickupMapLayout.Create(_game, map, area, ArenaPickupSettings.WeaponKit.Value);
        _layout = ArenaPickupLayouts.Select(file, map, area);
        var weapons = _layout?.Points.Where(point => point.Kind == ArenaPickupKind.Weapon)
            .Select(point => point.Id + ":" + point.KitId) ?? Enumerable.Empty<string>();
        Plugin.ModLogger.LogDebug($"Arena pickups layout: {map}/{area ?? "full map"}; {_layout?.Points.Count ?? 0} configured points. "
            + $"Source={(_builtInLayout ? "built-in" : "Pickups.json")}; weapons=[{string.Join(", ", weapons)}]. Frozen for this match.");
    }

    private static void PlaceMissing(Vector3 anchor)
    {
        if (_layout == null) return;
        _placementAttempts++;
        var total = new ArenaPickupGeometry.PlacementDiagnostics();
        int candidates = 0;
        // Reserve legacy fixed custom points first; rotation must not displace
        // authored positions or change the spacing of existing fixed layouts.
        foreach (var definition in _layout.Points.OrderBy(point => (point.Alternatives?.Count ?? 0) > 0 ? 1 : 0))
        {
            // Preserve successful markers, pending weapon loads and cooldowns.
            if (Entries.Any(placedEntry => placedEntry.Definition.Id == definition.Id)) continue;
            bool rotating = (definition.Alternatives?.Count ?? 0) > 0;
            bool placed = false;
            int alternatives = 0;
            var diagnostics = new ArenaPickupGeometry.PlacementDiagnostics();
            foreach (var point in ArenaPickupLayouts.Candidates(definition, PositionRandom))
            {
                alternatives++; candidates++;
                var requested = new Vector3(point.X, point.Y, point.Z);
                if (!ArenaPickupGeometry.TryPlacePoint(requested, anchor, _builtInLayout,
                    out var floor, out var reason, out var relocated, diagnostics)) continue;
                // A fixed point recovering on a later attempt must not overlap a
                // previously placed rotating point. Fixed/fixed authoring stays exact.
                if (Entries.Any(other => (rotating || other.Rotating) && (other.Floor - floor).sqrMagnitude < 9f))
                { diagnostics.Refused("another pickup is within 3m"); continue; }
                if (relocated)
                    Plugin.ModLogger.LogDebug(FormattableString.Invariant($"Arena pickup '{point.Id}' relocated: requested ({requested.x:F3}, {requested.y:F3}, {requested.z:F3}), floor ({floor.x:F3}, {floor.y:F3}, {floor.z:F3}), shift {(floor - requested).magnitude:F2}m; original point: {reason}."));
                var entry = new Entry { Definition = point, Candidates = definition, Floor = floor, Rotating = rotating,
                    AvailableSince = Time.time, NextRotationAt = Time.time + AvailableRotationSeconds };
                Entries.Add(entry);
                entry.View = new ArenaPickupView(point, floor);
                if (point.Kind == ArenaPickupKind.Weapon) PrepareWeapon(entry);
                Plugin.ModLogger.LogDebug(FormattableString.Invariant($"Arena pickup '{point.Id}' selected ({floor.x:F3}, {floor.y:F3}, {floor.z:F3}) for round {_round}; {(definition.Alternatives?.Count ?? 0) + 1} candidates."));
                placed = true;
                PlacementFailures.Remove(definition.Id);
                break;
            }
            total.Merge(diagnostics);
            if (!placed) PlacementFailures[definition.Id] = FormattableString.Invariant(
                $"anchor=({anchor.x:F3}, {anchor.y:F3}, {anchor.z:F3}); alternatives={alternatives}; geometryProbes={diagnostics.Probes}; refusals=[{diagnostics}]");
        }
        var actor = _world.MainPlayer.Position;
        Plugin.ModLogger.LogDebug(FormattableString.Invariant(
            $"Arena pickup placement attempt {_placementAttempts}: round {_round}; actor=({actor.x:F3}, {actor.y:F3}, {actor.z:F3}); anchor=({anchor.x:F3}, {anchor.y:F3}, {anchor.z:F3}); placed={Entries.Count}/{_layout.Points.Count}; alternatives={candidates}; geometryProbes={total.Probes}; refusals=[{total}]."));
    }

    private static bool TryRotate(Entry entry, bool rearming = false)
    {
        if (!CanRelocate(entry, rearming)) return false;
        if (Time.timeScale <= 0f || ArenaRespawnDriver.IsRespawnInProgress || WeaponSwapService.IsSwapping)
        { RotationBlocked(entry, "gameplay paused or body/weapon swap active"); return false; }
        if (!ArenaPickupGeometry.TryAnchor(_world, out var anchor))
        { RotationBlocked(entry, "local actor has no ready grounded navigation anchor"); return false; }

        var diagnostics = new ArenaPickupGeometry.PlacementDiagnostics();
        var candidates = ArenaPickupLayouts.Candidates(entry.Candidates, PositionRandom);
        foreach (var point in candidates)
        {
            if (point.X == entry.Definition.X && point.Y == entry.Definition.Y && point.Z == entry.Definition.Z) continue;
            // Built-in prototypes use the same bounded repair as initial placement.
            // Custom points stay exact; all final floors still pass spacing/path checks.
            if (!ArenaPickupGeometry.TryPlacePoint(new Vector3(point.X, point.Y, point.Z), anchor, _builtInLayout,
                out var floor, out _, out _, diagnostics)) continue;
            if ((floor - entry.Floor).sqrMagnitude < 9f
                || Entries.Any(other => !ReferenceEquals(other, entry) && (other.Floor - floor).sqrMagnitude < 9f))
            { diagnostics.Refused("reserved pickup position within 3m"); continue; }

            ArenaPickupView view;
            try { view = new ArenaPickupView(point, floor); }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning($"Arena pickup '{entry.Definition.Id}' kept its position: replacement marker failed ({ex.GetBaseException().Message}).");
                return false;
            }
            if (!entry.State.Relocate()) { view.Dispose(); return false; }
            MoveAppearance(entry, point, floor, view);
            return true;
        }
        // Factory can have exactly six safe slots for six pickups. A sequential
        // move then has no free destination. Exchange two AVAILABLE appearances
        // only after proving both reciprocal authored slots and staging both views.
        if (TryExchange(entry, candidates, anchor, diagnostics, rearming)) return true;
        RotationBlocked(entry, $"geometryProbes={diagnostics.Probes}; refusals=[{diagnostics}]");
        return false;
    }

    private static bool CanRelocate(Entry entry, bool rearming = false) => entry.Rotating
        && entry.State.Phase == ArenaPickupPhase.Available && !entry.Preparing
        && entry.Weapon?.IsPending != true
        && (entry.Definition.Kind != ArenaPickupKind.Weapon || rearming || entry.Weapon?.IsAvailable == true);

    private static bool SamePosition(ArenaPickupPoint a, ArenaPickupPoint b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    private static bool TryExchange(Entry entry, List<ArenaPickupPoint> candidates, Vector3 anchor,
        ArenaPickupGeometry.PlacementDiagnostics diagnostics, bool rearming)
    {
        int generation = Generation;
        foreach (var partner in Entries)
        {
            if (ReferenceEquals(partner, entry) || !CanRelocate(partner)
                || Time.time < partner.AvailableSince + AvailableRotationSeconds) continue;
            var point = candidates.FirstOrDefault(p => SamePosition(p, partner.Definition));
            var reverse = ArenaPickupLayouts.Candidates(partner.Candidates, PositionRandom)
                .FirstOrDefault(p => SamePosition(p, entry.Definition));
            if (point == null || reverse == null) continue;
            // Recheck the ACTUAL already resolved floors, including bounded repairs
            // made at initial placement. The authored slot association never changes.
            if (!ArenaPickupGeometry.TryPlacePoint(partner.Floor, anchor, false, out var floor, out _, out _, diagnostics)
                || !ArenaPickupGeometry.TryPlacePoint(entry.Floor, anchor, false, out var reverseFloor, out _, out _, diagnostics)) continue;
            if ((floor - partner.Floor).sqrMagnitude > .0625f || (reverseFloor - entry.Floor).sqrMagnitude > .0625f
                || (floor - reverseFloor).sqrMagnitude < 9f
                || Entries.Any(other => !ReferenceEquals(other, entry) && !ReferenceEquals(other, partner)
                    && ((other.Floor - floor).sqrMagnitude < 9f || (other.Floor - reverseFloor).sqrMagnitude < 9f)))
            { diagnostics.Refused("exchange floor drift or reserved position within 3m"); continue; }

            ArenaPickupView view = null, reverseView = null;
            try
            {
                view = new ArenaPickupView(point, floor);
                reverseView = new ArenaPickupView(reverse, reverseFloor);
            }
            catch (Exception ex)
            {
                view?.Dispose(); reverseView?.Dispose();
                RotationBlocked(entry, "exchange marker preparation failed: " + ex.GetBaseException().Message);
                return false;
            }
            // Constructors are Unity boundaries. Nothing old has changed if the
            // raid or either transfer changed while the two views were prepared.
            if (!Owns(generation, entry) || !Owns(generation, partner) || !CanRelocate(entry, rearming) || !CanRelocate(partner))
            { view.Dispose(); reverseView.Dispose(); return false; }
            // Relocate is pure, synchronous and cannot fail after both Available
            // checks. No native callbacks occur between the two state commits.
            if (!entry.State.Relocate() || !partner.State.Relocate())
            { view.Dispose(); reverseView.Dispose(); return false; }
            MoveAppearance(entry, point, floor, view);
            MoveAppearance(partner, reverse, reverseFloor, reverseView);
            if (partner.Definition.Kind == ArenaPickupKind.Weapon) PrepareWeapon(partner);
            return true;
        }
        return false;
    }

    private static void MoveAppearance(Entry entry, ArenaPickupPoint point, Vector3 floor, ArenaPickupView view)
    {
        var previous = entry.Floor;
        entry.View?.Dispose();
        entry.View = view; entry.Definition = point; entry.Floor = floor;
        entry.MustLeave.Clear(); entry.NeedsEntry = true;
        entry.AvailableSince = Time.time; entry.NextRotationAt = Time.time + AvailableRotationSeconds;
        Plugin.ModLogger.LogDebug(FormattableString.Invariant($"Arena pickup '{point.Id}' rotated during round {_round}: ({previous.x:F3}, {previous.y:F3}, {previous.z:F3}) -> ({floor.x:F3}, {floor.y:F3}, {floor.z:F3}); appearance {entry.State.Appearance}."));
    }

    // Invoked only at an availability deadline/cooldown completion, never per frame.
    private static void RotationBlocked(Entry entry, string reason)
        => Plugin.ModLogger.LogDebug($"Arena pickup '{entry.Definition.Id}' kept its rotation position: {reason}.");

    private static void FinishPlacement(string outcome)
    {
        _placementPending = false;
        if (_placementAttempts == 0)
            Plugin.ModLogger.LogWarning("Arena pickups skipped this round: no ready grounded local navigation anchor within 15 seconds (body must be outside respawn/weapon swap).");
        foreach (var definition in _layout.Points)
        {
            if (Entries.Any(entry => entry.Definition.Id == definition.Id)) continue;
            if (PlacementFailures.TryGetValue(definition.Id, out var failure))
                Plugin.ModLogger.LogWarning($"Arena pickup '{definition.Id}' skipped after {_placementAttempts} placement attempts; last attempt: {failure}.");
        }
        Plugin.ModLogger.LogDebug($"Arena pickups round {_round}: {Entries.Count}/{_layout.Points.Count} points placed ({outcome}, {_placementAttempts} attempts); damage 20s, shield 220/30s, health up to {ArenaHealthPickup.Amount:0}HP, jump boots +100%/30s, infinite ammo 10s; weapon/boots/ammo local only.");
        Plugin.ModLogger.LogDebug($"Arena pickup bot routing: {(BotObjectivesAvailable && ArenaPickupSettings.BotsCollectPowerups.Value ? "available via BigBrain companion" : "inactive; passive collection only if enabled")}.");
    }

    private static bool Owns(int generation, Entry entry) => _live && Generation == generation
        && Entries.Contains(entry) && TryContext(out var match, out _, out _) && ReferenceEquals(match, _match)
        && PhaseLive(match) && Round(match) == _round && ArenaPickupSettings.Enabled?.Value == true;

    private static void PrepareWeapon(Entry entry)
    {
        if (entry.Preparing || !Owns(Generation, entry)) return;
        int generation = Generation;
        var previous = entry.Weapon; entry.Weapon = null;
        try { previous?.Dispose(); }
        catch (Exception ex)
        {
            // Unknown cleanup cannot justify creating another source weapon or
            // resetting unrelated entries and actor effects.
            entry.State.Retire();
            Plugin.ModLogger.LogWarning($"Arena pickup '{entry.Definition.Id}' retired after source cleanup failed ({ex.GetBaseException().Message}).");
            return;
        }
        // Native DestroyLoot emits synchronous removal listeners. A listener
        // ending/replacing the round must not start an orphaned preparation.
        if (!Owns(generation, entry)) return;
        var kit = ArenaPickupWeapons.Find(entry.Definition.KitId);
        if (kit == null)
        {
            entry.State.Retire();
            Plugin.ModLogger.LogWarning($"Arena pickup '{entry.Definition.Id}' disabled: kit '{entry.Definition.KitId}' missing.");
            return;
        }
        entry.Preparing = true;
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
                    entry.AvailableSince = Time.time;
                    entry.NextRotationAt = Time.time + AvailableRotationSeconds;
                    entry.Weapon = weapon;
                    if (weapon == null) entry.State.Retire();
                });
        }
    }

    private static void ScanActors()
    {
        int generation = Generation;
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
                if (!Reaches(sample.Position, current, entry.Floor, !entry.NeedsEntry && !sample.Fresh && Time.time - sample.At <= .25f, out float fraction)) continue;
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
            // Native healing/inventory listeners can synchronously end or
            // replace the round. Never advance the old Entries enumerator or
            // mutate the replacement generation's actor samples afterwards.
            if (generation != Generation) return;
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
        if (kind == ArenaPickupKind.Health && ArenaHealthPickup.Missing(player) <= .001f) return false;
        if (player.IsAI) return (kind == ArenaPickupKind.DoubleDamage || kind == ArenaPickupKind.Shield || kind == ArenaPickupKind.Health) && ArenaPickupSettings.BotsCollectPowerups.Value;
        if (kind == ArenaPickupKind.JumpBoots && !Movement.ArenaMovementScope.IsLocal(player)) return false;
        return Hud.ArenaHudVisibility.IsBattleUiActive && !ArenaRespawnDriver.IsRespawnInProgress && !WeaponSwapService.IsSwapping
            && player.InventoryController != null && !player.InventoryController.IsChangingWeapon;
    }

    private static void Collect(Entry entry, Player player)
    {
        int reservation = entry.State.Reserve(Time.time);
        if (reservation == 0) return;
        entry.MustLeave.Add(player);
        int generation = Generation;
        float healed = 0;
        if (entry.Definition.Kind == ArenaPickupKind.Weapon)
        {
            if (!entry.Weapon.TryTake(player, () => Owns(generation, entry) && CanCollect(player, ArenaPickupKind.Weapon), Complete))
                Complete(false);
        }
        else if (entry.Definition.Kind == ArenaPickupKind.Health)
        {
            var result = ArenaHealthPickup.Apply(player, out healed);
            if (result == ArenaHealingResult.Indeterminate)
            {
                // A native listener can throw after HP already changed. Do not
                // rearm an appearance whose health mutation cannot be proven.
                if (Owns(generation, entry)) entry.State.Retire();
                Plugin.ModLogger.LogWarning($"Arena health pickup '{entry.Definition.Id}' retired after unconfirmed healing.");
            }
            else Complete(result == ArenaHealingResult.Applied);
        }
        else Complete(entry.Definition.Kind switch
        {
            ArenaPickupKind.DoubleDamage => ArenaPowerups.GrantDamage(player, 20f),
            ArenaPickupKind.Shield => ArenaPowerups.GrantShield(player, 220f, 30f),
            ArenaPickupKind.JumpBoots => ArenaPowerups.GrantJump(player, 30f),
            ArenaPickupKind.InfiniteAmmo => ArenaPowerups.GrantAmmo(player, 10f),
            _ => false
        });

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
                ArenaPickupAudio.Collected(player);
                if (!player.IsAI) Notice(entry.Definition.Kind switch
                {
                    ArenaPickupKind.DoubleDamage => "DOUBLE DAMAGE — 20s",
                    ArenaPickupKind.Shield => "SHIELD — 220 / 30s",
                    ArenaPickupKind.JumpBoots => "JUMP BOOTS — +100% HEIGHT / 30s",
                    ArenaPickupKind.InfiniteAmmo => "INFINITE AMMO — 10s / LOADED MAGAZINE WEAPONS",
                    ArenaPickupKind.Health => $"HEALTH — +{healed:0.#} HP",
                    _ => "WEAPON COLLECTED"
                });
                Plugin.ModLogger.LogDebug($"Arena pickup collected: {entry.Definition.Id}; actor {(player.IsAI ? "bot" : "local")}; round {_round}; returns in {entry.Definition.RespawnSeconds:0}s.");
            }
            else if (!player.IsAI && entry.Definition.Kind == ArenaPickupKind.Weapon)
                Notice("WEAPON LEFT — " + (entry.Weapon?.LastRefusal ?? "transfer unavailable") + "; approach again");
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
        ArenaPickupAudio.Reset();
        unchecked { Generation++; }
        _placementAttempts = 0; PlacementFailures.Clear();
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
        _match = null; _game = null; _world = null; _layout = null; _round = 0; _faulted = false; _builtInLayout = false;
    }
}
