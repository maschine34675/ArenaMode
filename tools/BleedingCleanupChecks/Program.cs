using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using Systems.Effects;
using UnityEngine;
using UObj = UnityEngine.Object;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new Exception("ASSERT: " + label); }
    private static List<KeyValuePair<IPlayer, float>> Entries(EffectsCommutator effects)
        => (List<KeyValuePair<IPlayer, float>>)typeof(EffectsCommutator)
            .GetField("_playerBleedingDecalTimes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(effects);
    private static (LocalGame Game, LocalPlayer Old, LocalPlayer New, LocalPlayer Other, Effects Effects) Setup()
    {
        UObj.Pending.Clear(); UObj.BeforeDestroy = null; UObj.ThrowOnDestroy = false;
        Time.time = 100; ArenaSession.IsActive = true;
        var effects = Singleton<Effects>.Instance = new Effects();
        var old = new LocalPlayer { ProfileId = "same-profile" };
        var current = new LocalPlayer { ProfileId = "same-profile" };
        var other = new LocalPlayer { ProfileId = "other" };
        foreach (var actor in new[] { old, current, other }) actor.SubscribeVisualEvents();
        var world = Singleton<GameWorld>.Instance = new GameWorld { MainPlayer = current };
        var game = new LocalGame { GameWorld = world, LocalPlayer = current };
        ArenaSession._startedGame = game; ArenaSession._startedWorld = world;
        return (game, old, current, other, effects);
    }
    private static void Bleed(params LocalPlayer[] actors)
    { foreach (var actor in actors) actor.HealthController.Start(new HeavyBleeding()); }
    private static void ExpectFailure(Action operation, string message)
    {
        try { operation(); throw new Exception("missing failure"); }
        catch (InvalidOperationException ex) { Check(ex.Message == message, "original failure preserved: " + message); }
    }
    private static void Run()
    {
        // Reproduce the actual invalid reference with the locally extracted native
        // add/remove/update methods, not a reimplementation of their algorithm.
        {
            var s = Setup(); Bleed(s.Old, s.New, s.Other);
            UObj.Destroy(s.Old.gameObject); UObj.Flush();
            bool failed = false;
            try { s.Effects.Commutator.UpdatePlayersBleedings(); }
            catch (NullReferenceException) { failed = true; }
            Check(failed, "old direct Destroy reproduces native bleeding NRE");
            Check(Entries(s.Effects.Commutator).Count == 3 && s.New.PositionReads == 0,
                "native first stale entry interrupts the remaining blood decals");
        }
        {
            var s = Setup(); Bleed(s.Old, s.New, s.Other);
            var untouched = Entries(s.Effects.Commutator).Skip(1).ToArray();
            int external = 0;
            s.Old.HealthController.EffectStartedEvent += _ => external++;
            UObj.BeforeDestroy = () =>
            {
                Check(!Entries(s.Effects.Commutator).Any(x => ReferenceEquals(x.Key, s.Old)),
                    "native old registration is removed before Destroy is requested");
                s.Old.HealthController.Start(new HeavyBleeding());
            };
            Check(ArenaRetiredBodyEffects.TryDestroy(s.Game, s.Old), "retired living body destroyed");
            Check(external == 1, "unrelated health subscriber remains active during retirement");
            Check(Entries(s.Effects.Commutator).SequenceEqual(untouched),
                "only exact old reference removed before destruction; replacement and others preserved");
            Check(s.Old.HealthController.StartedCount == 1 && s.Old.HealthController.ResidualCount == 0
                && s.Old.HealthController.DiedCount == 0, "only three retired visual delegates detached");
            UObj.Flush(); s.Effects.Commutator.UpdatePlayersBleedings();
            Check(s.Old.PositionReads == 0 && s.New.PositionReads == 1 && s.Other.PositionReads == 1,
                "native bleeding update remains healthy for replacement and other actor");
            Check(s.Effects.Emitted == 2, "replacement and other actor still emit native decals");
        }
        foreach (var scenario in new[] { "ordinary", "no-world", "foreign-world", "stopping", "stopped",
            "same-body", "still-main", "no-main", "no-replacement", "wrong-profile", "empty-profile",
            "other-game-same-world", "dead", "corpse", "no-health", "null-body", "null-game" })
        {
            var s = Setup(); Bleed(s.Old, s.New, s.Other);
            LocalGame game = s.Game; LocalPlayer body = s.Old;
            switch (scenario)
            {
                case "ordinary": ArenaSession.IsActive = false; break;
                case "no-world": Singleton<GameWorld>.Instance = null; break;
                case "foreign-world": Singleton<GameWorld>.Instance = new GameWorld(); break;
                case "stopping": game.Status = GameStatus.Stopping; break;
                case "stopped": game.Status = GameStatus.Stopped; break;
                case "same-body": game.LocalPlayer = s.Old; break;
                case "still-main": game.GameWorld.MainPlayer = s.Old; break;
                case "no-main": game.GameWorld.MainPlayer = null; break;
                case "no-replacement": game.LocalPlayer = null; break;
                case "wrong-profile": game.LocalPlayer.ProfileId = "foreign-profile"; break;
                case "empty-profile": game.LocalPlayer.ProfileId = s.Old.ProfileId = ""; break;
                case "other-game-same-world": game = new LocalGame { GameWorld = game.GameWorld, LocalPlayer = s.New }; break;
                case "dead": body.HealthController.IsAlive = false; break;
                case "corpse": body.Corpse = new EFT.Interactive.Corpse(); break;
                case "no-health": body.HealthController = null; break;
                case "null-body": body = null; break;
                case "null-game": game = null; break;
            }
            Check(!ArenaRetiredBodyEffects.TryDestroy(game, body), "out-of-scope body retained: " + scenario);
            Check(UObj.Pending.Count == 0 && Entries(s.Effects.Commutator).Count == 3,
                "out-of-scope registrations unchanged: " + scenario);
            if (s.Old.HealthController != null)
                Check(s.Old.HealthController.StartedCount == 1 && s.Old.HealthController.ResidualCount == 1
                    && s.Old.HealthController.DiedCount == 1, "out-of-scope subscriptions unchanged: " + scenario);
        }
        {
            var s = Setup(); Bleed(s.New, s.Other);
            Check(ArenaRetiredBodyEffects.TryDestroy(s.Game, s.Old), "nonbleeding retired body destroyed");
            UObj.Flush(); s.Effects.Commutator.UpdatePlayersBleedings();
            Check(Entries(s.Effects.Commutator).Count == 2 && s.Effects.Emitted == 2,
                "nonbleeding retirement never adds an entry or removes another actor");
        }
        {
            var s = Setup(); Bleed(s.Old, s.New, s.Other); Bleed(s.Old);
            Check(Entries(s.Effects.Commutator).Count == 3, "native start deduplicates exact reference");
            s.Old.HealthController.Die();
            Check(Entries(s.Effects.Commutator).Count == 2, "native death already removes old bleeding");
            Check(!ArenaRetiredBodyEffects.TryDestroy(s.Game, s.Old), "death corpse remains owned by native pipeline");
            s.New.HealthController.Residual(new HeavyBleeding());
            Check(Entries(s.Effects.Commutator).Count == 1, "native heavy-bleed residual still works");
            s.Other.HealthController.Start(new LightBleeding());
            Check(Entries(s.Effects.Commutator).Count == 1, "native light bleeding remains unregistered");
        }
        foreach (var failure in new[] { "started", "residual", "died", "effects", "destroy" })
        {
            var s = Setup(); Bleed(s.Old, s.New, s.Other);
            int external = 0;
            s.Old.HealthController.EffectStartedEvent += _ => external++;
            if (failure == "effects") s.Effects.ThrowOnCommutator = true;
            else if (failure == "destroy") UObj.ThrowOnDestroy = true;
            else s.Old.HealthController.ThrowOnRemove = failure;
            ExpectFailure(() => ArenaRetiredBodyEffects.TryDestroy(s.Game, s.Old),
                failure == "effects" ? "effects lookup failed" : failure == "destroy" ? "destroy failed" : failure + " detach failed");
            Check(UObj.Pending.Count == 0 && !s.Old.gameObject.Destroyed, "failure never destroys body: " + failure);
            Check(s.Old.HealthController.StartedCount == 2 && s.Old.HealthController.ResidualCount == 1
                && s.Old.HealthController.DiedCount == 1, "failure rolls back without duplicate delegates: " + failure);
            Check(Entries(s.Effects.Commutator).Any(x => ReferenceEquals(x.Key, s.New))
                && Entries(s.Effects.Commutator).Any(x => ReferenceEquals(x.Key, s.Other)),
                "failure leaves unrelated registrations untouched: " + failure);
            Check(Entries(s.Effects.Commutator).Count == (failure == "destroy" ? 2 : 3),
                "failure does not invent registration during rollback: " + failure);
            s.Old.HealthController.ThrowOnRemove = null; s.Effects.ThrowOnCommutator = false; UObj.ThrowOnDestroy = false;
            s.Old.HealthController.Start(new HeavyBleeding());
            Check(external == 1 && Entries(s.Effects.Commutator).Count == 3,
                "restored visual and independent subscribers respond once: " + failure);
            Check(ArenaRetiredBodyEffects.TryDestroy(s.Game, s.Old), "safe retry can retire body: " + failure);
        }
        {
            var s = Setup(); Bleed(s.Old, s.New, s.Other);
            s.Effects.MissingCommutator = true;
            ExpectFailure(() => ArenaRetiredBodyEffects.TryDestroy(s.Game, s.Old),
                "Retired Arena body has an effects singleton without its bleeding commutator.");
            Check(UObj.Pending.Count == 0 && !s.Old.gameObject.Destroyed && Entries(s.Effects.Commutator).Count == 3,
                "missing existing commutator retains body and all native entries");
            Check(s.Old.HealthController.StartedCount == 1 && s.Old.HealthController.ResidualCount == 1
                && s.Old.HealthController.DiedCount == 1, "missing existing commutator restores visual subscriptions");
        }
        {
            var s = Setup(); Singleton<Effects>.Instance = null;
            Check(ArenaRetiredBodyEffects.TryDestroy(s.Game, s.Old), "missing effects singleton does not prevent retirement");
            Check(s.Old.HealthController.StartedCount == 0, "missing effects still detaches own visual lifetime");
        }
    }
    public static int Main()
    {
        try { Run(); Console.WriteLine($"ARENA_BLEEDING_RUNTIME_PASSED ({_checks} assertions)"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine("ARENA_BLEEDING_RUNTIME_FAILED: " + ex); return 1; }
    }
}
