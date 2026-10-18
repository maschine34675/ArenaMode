using ArenaMode.Combat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;

internal static partial class Program
{
    private static Shot Continue(Fixture f, Shot parent, bool nativeContinuation = true, int fragmentDelta = 0,
        bool sameAmmo = true)
    {
        var child = f.Raw(parent.FireIndex, parent);
        child.FragmentIndex = parent.FragmentIndex + fragmentDelta;
        if (sameAmmo) child.Ammo = parent.Ammo;
        using (nativeContinuation ? ArenaShotAwardHooks.BeginContinuation(parent) : null)
            ArenaShotAwardHooks.Created(child);
        return child;
    }

    private static bool KillWith(Fixture f, Shot shot, object victim, bool bullet = true)
    {
        using var impact = ArenaShotAwardHooks.BeginImpact(f.World, shot);
        var facts = ArenaShotAwardHooks.CurrentFacts(Damage(shot), Id(shot));
        var lethal = Death(victim, f.Local, facts, facts, bullet);
        return ArenaShotAwardHistory.ReportCollateralDeath(ArenaShotAwardHooks.CaptureCollateral(lethal),
            ArenaHitHistory.BodyId(victim));
    }

    private static void Collateral()
    {
        foreach (bool aimed in new[] { false, true })
        {
            var f = new Fixture(); f.Gun.IsAiming = aimed;
            var root = f.Fire();
            // Native Tick creates the continuation BEFORE ShotDelegate kills A.
            var child = Continue(f, root); var grandchild = Continue(f, child);
            var a = new object(); var b = new object();
            Check(!KillWith(f, root, a), "first bullet victim alone earns no collateral");
            Check(!KillWith(f, root, a), "duplicate victim callback cannot earn collateral");
            Check(KillWith(f, child, b), "native penetration preserves first victim across precreated child");
            Check(!KillWith(f, grandchild, new object()), "third victim cannot repeat collateral");
            Check(!KillWith(f, root, b), "alternate ancestor callback cannot repeat collateral");
        }

        foreach (string reason in new[] { "separate-round", "same-fire-index", "fragment", "ricochet",
            "unknown-parent", "different-ammo-object", "pellets", "delayed-bleed", "branch", "self" })
        {
            var f = new Fixture(); var a = new object(); var b = new object(); Shot root;
            if (reason == "pellets")
            {
                root = f.Raw(); ((Ammo)root.Ammo).ProjectileCount = 8;
                using (ArenaShotAwardHooks.BeginFire(f.Gun, f.Weapon)) ArenaShotAwardHooks.Created(root);
            }
            else root = f.Fire();
            var child = reason switch
            {
                "separate-round" => f.Fire(12),
                "same-fire-index" => f.Fire(11),
                "fragment" => Continue(f, root, fragmentDelta: 1),
                "ricochet" => Continue(f, root, nativeContinuation: false),
                "unknown-parent" => Continue(f, f.Raw()),
                "different-ammo-object" => Continue(f, root, sameAmmo: false),
                _ => Continue(f, root)
            };
            if (reason == "branch") child = Continue(f, root);
            Check(!KillWith(f, root, reason == "self" ? f.Local : a,
                bullet: reason != "delayed-bleed"), "negative first death never awards: " + reason);
            Check(!KillWith(f, child, b), "independent fragments pellets rounds and invalid deaths do not combine: " + reason);
        }

        foreach (string reason in new[] { "pooled-child", "round", "world", "game", "respawn", "inactive", "closed" })
        {
            var f = new Fixture(); var root = f.Fire(); var child = Continue(f, root);
            Check(!KillWith(f, root, new object()), "lifetime first victim stored: " + reason);
            switch (reason)
            {
                case "pooled-child": child.Parent = null; ArenaShotAwardHooks.Created(child); break;
                case "round": ArenaShotAwardHistory.Reset(); break;
                case "world":
                    f.World = new ClientGameWorld { MainPlayer = f.Local };
                    ArenaSession.World = f.World; Singleton<GameWorld>.Instance = f.World; break;
                case "game":
                    f.Game = new LocalGame { LocalPlayer = f.Local, Status = GameStatus.Started };
                    ArenaSession.Game = f.Game; Singleton<AbstractGame>.Instance = f.Game; break;
                case "respawn": f.Local = new Player(); f.World.MainPlayer = f.Local; f.Game.LocalPlayer = f.Local; break;
                case "inactive": ArenaSession.IsActive = false; break;
                case "closed": ArenaMode.Modes.ArenaMatchService.Current.Mode.IsComplete = true; break;
            }
            Check(!KillWith(f, child, new object()), "stale bullet lineage cannot award: " + reason);
        }

        var recycle = new Fixture(); var original = recycle.Fire(); var continuation = Continue(recycle, original);
        var firstVictim = new object();
        Check(!KillWith(recycle, original, firstVictim), "pooled parent first victim accepted");
        using (ArenaShotAwardHooks.BeginFire(recycle.Gun, recycle.Weapon)) ArenaShotAwardHooks.Created(original);
        Check(KillWith(recycle, continuation, new object()), "legitimate child survives parent reuse without reading mutable chain");
        Check(!KillWith(recycle, original, new object()), "reused parent generation cannot borrow descendant kill history");

        var scopeFixture = new Fixture(); var parent = scopeFixture.Fire(); var scope = ArenaShotAwardHooks.BeginContinuation(parent);
        ArenaShotAwardHooks.Created(parent); // pooled in the middle of the native continuation stack
        var stale = scopeFixture.Raw(parent.FireIndex, parent); stale.Ammo = parent.Ammo;
        ArenaShotAwardHooks.Created(stale); scope.Dispose();
        Check(!KillWith(scopeFixture, stale, new object()), "recycled in-stack parent cannot pass frozen continuation token");
        Check(!KillWith(scopeFixture, stale, new object()), "invalid continuation never starts a new independent lineage");

        var finalized = new Fixture(); var p = finalized.Fire();
        var pending = ArenaShotAwardHooks.BeginContinuation(p);
        var nativeError = new Exception("native continuation failed");
        Check(ReferenceEquals(nativeError, Finalize("ArenaShotAwardHooksContinuationPatch", nativeError, pending)), "continuation finalizer preserves native exception");
        var after = Continue(finalized, p, nativeContinuation: false);
        Check(!KillWith(finalized, p, new object()) && !KillWith(finalized, after, new object()),
            "continuation exception finalizer does not lend scope to later child");
    }
}
