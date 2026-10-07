#nullable disable
using ArenaMode.Areas;
using ArenaMode.Modes;
using ArenaMode.Session;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Combat.Fire;

internal static class ArenaFireRuntime
{
    internal static ConfigEntry<bool> Enabled;
    internal static bool BotEscapeAvailable { get; set; }
    private static AccessTools.FieldRef<Player.ItemHandsController, Player> Owner;
    private static readonly Dictionary<Grenade, Throw> Throws = new();
    private static readonly List<Burn> Fires = new();
    private static readonly List<Grenade> Retired = new();
    private static readonly List<Player> Victims = new();
    private static readonly Dictionary<Player, float> Retain = new();
    private static readonly EBodyPart[] Parts = { EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach,
        EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg };
    private static ArenaMatch _match;
    private static GameWorld _world;
    private static int _round;
    private static bool _warned;
    private static float _nextDamage, _nextPrune;
    private static readonly List<Player> ExpiredSources = new();
    private static Player _damageVictim, _damageOwner;
    private static ThrowWeap _damageWeapon;
    internal static int Generation { get; private set; }
    private static readonly HashSet<Player> Ticked = new();

    private sealed class Throw
    {
        internal Player Owner;
        internal IObserverToPlayerBridge Bridge;
        internal ThrowWeap Weapon;
        internal float Born;
        internal bool Cancelled;
        internal Vector3? Impact;
    }
    private sealed class Burn : IDisposable
    {
        internal Throw Source;
        internal List<Vector3> Cells;
        internal float Until;
        internal ArenaFireView View;
        internal readonly HashSet<Player> Touched = new();
        public void Dispose() { View?.Dispose(); View = null; }
    }

    internal static void Enable(ConfigFile config)
    {
        Enabled = config.Bind("Arena incendiary prototype", "Enabled", false,
            $"Prototype: your RGD-5 throws become 6-second ground fire in live Arena only. Existing grenade model; bot grenades and ordinary raids are unchanged. {ArenaFirePolicy.DamagePerSecond} base total HP/s; fire does not stack. Requires the matching BigBrain companion for bot escape. May be toggled between throws; disabling extinguishes owned fire and retires owned grenades safely.");
        Owner = AccessTools.FieldRefAccess<Player.ItemHandsController, Player>("_player");
        new ThrowPatch().Enable(); new CollisionPatch().Enable(); new ExplosionPatch().Enable();
    }

    private static bool Context(out ArenaMatch match, out GameWorld world)
    {
        match = ArenaMatchService.Current;
        world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        return ArenaSession.IsActive && match != null && !match.Mode.IsComplete && world != null && game != null
            && game.Status == GameStatus.Started && ArenaSession.IsExactActiveRaid(game, world)
            && ReferenceEquals(game.GameWorld, world) && !ArenaMatchService.TeamStartAbortPending
            && !ArenaAreaRuntime.IsPending && (match.Mode switch
            {
                RoundsMode dm => dm.AssistScoringOpen,
                TeamDeathmatchMode tdm => tdm.AssistScoringOpen,
                TacticalMode tactical => tactical.AssistScoringOpen,
                GunGameMode gg => gg.AssistScoringOpen,
                _ => false
            });
    }
    private static int Round(ArenaMatch match) => match.Mode switch
    { RoundsMode dm => dm.Round.Number, TacticalMode tactical => tactical.RoundNumber, _ => 1 };
    private static bool Current() => Enabled?.Value == true && Context(out var match, out var world)
        && ReferenceEquals(_match, match) && ReferenceEquals(_world, world) && _round == Round(match);

    internal static bool LiveActor(Player player) => Current() && player != null && player.HealthController?.IsAlive == true
        && ReferenceEquals(_world.GetAlivePlayerByProfileID(player.ProfileId), player)
        && ReferenceEquals(_world.GetEverExistedPlayerByID(player.ProfileId), player);

    private static void Bind(Player.BaseGrenadeHandsController controller, Grenade grenade)
    {
        if (grenade == null || !Current() || !BotEscapeAvailable || Throws.Count >= ArenaFirePolicy.MaxThrows) return;
        var owner = Owner(controller);
        var local = _world.MainPlayer;
        if (!ArenaFirePolicy.Convert(Enabled.Value, true, ReferenceEquals(owner, local) && !owner.IsAI && owner.IsYourPlayer
            && LiveActor(owner) && ReferenceEquals(owner.HandsController, controller), controller.Item?.TemplateId)
            || !ReferenceEquals(grenade.WeaponSource, controller.Item) || !ReferenceEquals(grenade.Player?.iPlayer, owner)) return;
        Throws.Add(grenade, new Throw { Owner = owner, Bridge = grenade.Player, Weapon = controller.Item, Born = Time.time });
        Hold(owner, Time.time + 12f);
        Plugin.ModLogger.LogInfo($"Arena incendiary throw: {grenade.Id}; exact local RGD-5; bot escape={BotEscapeAvailable}.");
    }

    // All owned explosions remain suppressed, including when disabled or leaving.
    // Native InvokeBlowUpEvent still owns OnExplosion and world unregistration.
    private static bool Explosion(Grenade grenade)
    {
        if (!Throws.TryGetValue(grenade, out var source)) return true;
        try
        {
            if (!source.Cancelled && Current() && source.Owner != null && Fires.Count < ArenaFirePolicy.MaxFires)
            {
                var cells = ArenaFireGeometry.Build(source.Impact ?? grenade.transform.position);
                if (cells.Count != 0)
                {
                    var fire = new Burn { Source = source, Cells = cells, Until = Time.time + ArenaFirePolicy.Lifetime };
                    fire.View = new ArenaFireView(cells);
                    Fires.Add(fire); Hold(source.Owner, fire.Until + 2f);
                    Plugin.ModLogger.LogInfo($"Arena incendiary ignited: {cells.Count} floor cells at {cells[0]}; 6s, {ArenaFirePolicy.DamagePerSecond} base HP/s.");
                }
                else Plugin.ModLogger.LogInfo("Arena incendiary expired without a safe nearby floor; no blast or fire.");
            }
        }
        catch (Exception ex) { Warn(ex); }
        // The native caller destroys the projectile AFTER this prefix. Keep
        // suppression until that succeeds, including a throwing OnExplosion.
        finally { source.Cancelled = true; }
        return false;
    }

    internal static void Tick()
    {
        try
        {
            if (Enabled?.Value != true || !Context(out var match, out var world)) { Reset(); return; }
            if (!ReferenceEquals(_match, match) || !ReferenceEquals(_world, world) || _round != Round(match))
            {
                bool newWorld = !ReferenceEquals(_world, world);
                Reset();
                if (newWorld) Retain.Clear();
                _match = match; _world = world; _round = Round(match);
            }
            Retired.Clear();
            foreach (var pair in Throws)
                if (pair.Key == null || pair.Value.Cancelled || pair.Value.Impact.HasValue || Time.time - pair.Value.Born > 8f) Retired.Add(pair.Key);
            foreach (var grenade in Retired)
            {
                if (grenade == null) { Throws.Remove(grenade); continue; }
                grenade.StopTimer();
                if (Throws[grenade].Cancelled) grenade.OnExplosion();
                else grenade.InvokeBlowUpEvent();
                Throws.Remove(grenade);
            }
            if (Time.timeScale <= 0) return;
            Prune();
            if (Fires.Count == 0) return;
            bool damageTick = Time.time >= _nextDamage;
            if (damageTick) _nextDamage = Time.time + ArenaFirePolicy.TickSeconds;
            Victims.Clear();
            foreach (var p in world.AllAlivePlayersList) if (LiveActor(p)) Victims.Add(p);
            // One source per victim per scan, regardless of overlapping fire/cells.
            Ticked.Clear();
            for (int i = Fires.Count - 1; i >= 0; i--)
            {
                var fire = Fires[i];
                if (Time.time >= fire.Until || fire.Source.Owner == null)
                { fire.Dispose(); Fires.RemoveAt(i); continue; }
                fire.View.Tick(fire.Until - Time.time);
                if (!damageTick) continue;
                foreach (var victim in Victims)
                {
                    if (!LiveActor(victim) || Ticked.Contains(victim) || !ArenaFireGeometry.Contains(fire.Cells, victim.Position)) continue;
                    // Respawned thrower is the same participant, never an enemy of their old life.
                    if (victim.ProfileId == fire.Source.Owner.ProfileId && !ReferenceEquals(victim, fire.Source.Owner)) continue;
                    Ticked.Add(victim);
                    if (fire.Touched.Add(victim)) Plugin.ModLogger.LogInfo($"Arena incendiary contact: {victim.ProfileId}; bot={victim.IsAI}.");
                    Damage(fire, victim);
                }
            }
        }
        catch (Exception ex) { Warn(ex); Reset(); }
    }

    private static void Damage(Burn fire, Player victim)
    {
        if (!ReferenceEquals(fire.Source.Owner, victim) && _match.Mode is TacticalMode tactical
            && !tactical.IsOpposingAwardKill(victim, fire.Source.Owner)) return;
        var health = victim.ActiveHealthController;
        if (health == null) return;
        float total = 0;
        foreach (var part in Parts) { var hp = health.GetBodyPartHealth(part); if (hp.Current > 0) total += hp.Maximum; }
        if (total <= 0) return;
        var oldVictim = _damageVictim; var oldOwner = _damageOwner; var oldWeapon = _damageWeapon;
        _damageVictim = victim; _damageOwner = fire.Source.Owner; _damageWeapon = fire.Source.Weapon;
        try
        {
            foreach (var part in Parts)
            {
                if (!LiveActor(victim)) break;
                var hp = health.GetBodyPartHealth(part); if (hp.Current <= 0) continue;
                float multiplier = Singleton<GlobalConfiguration>.Instance.Health.ProfileHealthSettings.BodyPartsSettings[part].EnvironmentDamageMultiplier;
                float damage = ArenaFirePolicy.PartDamage(ArenaFirePolicy.TickDamage(ArenaFirePolicy.TickSeconds), hp.Maximum, total, multiplier);
                if (damage <= 0) continue;
                var info = new DamageInfo { DamageType = EDamageType.Flame,
                    Damage = damage,
                    Player = fire.Source.Bridge, Weapon = fire.Source.Weapon, SourceId = fire.Source.Weapon.TemplateId,
                    HitPoint = victim.Position, Direction = Vector3.zero, HitNormal = Vector3.up };
                victim.ApplyDamageInfo(info, part, EBodyPartColliderType.None, 0f);
            }
        }
        finally { _damageVictim = oldVictim; _damageOwner = oldOwner; _damageWeapon = oldWeapon; }
    }

    internal static bool OwnsDamage(Player victim, IPlayer attacker, EDamageType type)
        => type == EDamageType.Flame && _damageWeapon != null && ReferenceEquals(victim, _damageVictim)
        && ReferenceEquals(attacker, _damageOwner) && ArenaSession.IsActive;

    internal static string HoldsSource(string profileId, GameWorld world)
    {
        if (!ReferenceEquals(world, _world)) return null;
        foreach (var source in Retain)
            if (source.Key != null && source.Key.ProfileId == profileId && source.Value >= Time.time) return "Arena ground fire or its quiet period";
        return null;
    }
    private static void Hold(Player owner, float until)
    { Retain.TryGetValue(owner, out float previous); Retain[owner] = Math.Max(previous, until); }
    internal static bool HoldsBody(Player body, GameWorld world)
        => body != null && ReferenceEquals(world, _world) && Retain.TryGetValue(body, out float until) && until >= Time.time;

    internal static bool Hot(Vector3 feet, float padding = .3f)
    {
        if (!Current()) return false;
        foreach (var fire in Fires) if (Time.time < fire.Until && ArenaFireGeometry.Contains(fire.Cells, feet, padding)) return true;
        return false;
    }
    internal static bool HasFire => Current() && Fires.Count != 0;

    internal static void Reset()
    {
        if (_match == null && Fires.Count == 0 && Throws.Count == 0 && Retain.Count == 0) { _world = null; return; }
        if (_match != null || Fires.Count != 0 || Throws.Count != 0) Generation++;
        // Keep suppression registered until every native projectile is retired.
        foreach (var source in Throws.Values) source.Cancelled = true;
        Retired.Clear(); Retired.AddRange(Throws.Keys);
        foreach (var grenade in Retired)
            try
            {
                if (grenade != null) { grenade.StopTimer(); grenade.OnExplosion(); }
                Throws.Remove(grenade);
            }
            catch (Exception ex) { Warn(ex); }
        // A failed native teardown stays suppressed; never turn it back into a
        // real explosive just because cleanup threw. Retry on the next Tick.
        Retired.Clear();
        foreach (var fire in Fires) fire.Dispose();
        Fires.Clear(); Victims.Clear(); Ticked.Clear();
        // Retain old sources for delayed native/SAIN hit callbacks while the same world exists.
        Prune();
        _match = null; _round = 0; _nextDamage = 0;
        if (Retain.Count == 0) _world = null;
    }
    private static void Prune()
    {
        if (!Singleton<GameWorld>.Instantiated || !ReferenceEquals(_world, Singleton<GameWorld>.Instance))
        { Retain.Clear(); return; }
        if (Time.time < _nextPrune) return;
        _nextPrune = Time.time + 1f;
        ExpiredSources.Clear();
        foreach (var source in Retain.Keys)
            if (source == null || Time.time > Retain[source] || !Singleton<GameWorld>.Instantiated
                || !ReferenceEquals(_world, Singleton<GameWorld>.Instance)) ExpiredSources.Add(source);
        foreach (var source in ExpiredSources) Retain.Remove(source);
        ExpiredSources.Clear();
    }
    private static void Warn(Exception ex)
    { if (_warned) return; _warned = true; Plugin.ModLogger.LogWarning("Arena incendiary prototype failed safely: " + ex); }

    private sealed class ThrowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.DeclaredMethod(typeof(Player.BaseGrenadeHandsController),
            "CreateAndRegisterGrenade", new[] { typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(float) });
        [PatchPostfix] private static void Postfix(Player.BaseGrenadeHandsController __instance, Grenade __result)
        { try { Bind(__instance, __result); } catch (Exception ex) { Warn(ex); } }
    }
    private sealed class CollisionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.DeclaredMethod(typeof(Grenade), "OnCollisionEnter");
        [PatchPostfix] private static void Postfix(Grenade __instance, Collision collision)
        {
            if (!Throws.TryGetValue(__instance, out var source) || source.Impact.HasValue || Time.time - source.Born < .12f) return;
            for (int i = 0; i < collision.contactCount; i++)
            { var contact = collision.GetContact(i); if (contact.normal.y >= .7f) { source.Impact = contact.point; break; } }
        }
    }
    private sealed class ExplosionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.DeclaredMethod(typeof(Grenade), nameof(Grenade.Explosion), Type.EmptyTypes);
        [PatchPrefix] private static bool Prefix(Grenade __instance) => Explosion(__instance);
    }
}
