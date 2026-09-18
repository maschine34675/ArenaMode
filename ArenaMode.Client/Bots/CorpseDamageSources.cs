using EFT;
using EFT.Ballistics;
using EFT.SynchronizableObjects;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ArenaMode.Bots;

/// <summary>
/// Keeps Arena from taking away a body that the world can still name as the
/// SOURCE of damage — the sibling of <see cref="CorpseAggressors"/>, which keeps
/// one that somebody is still owed a death by.
///
/// Measured, not argued. The 2026-09-17 Labyrinth raids logged 21
/// NullReferenceExceptions out of <c>Player.ApplyDamageInfo</c> and
/// <c>ClientGameWorld.ShotDelegate</c>, only in raids that release bodies, and
/// every one of them 3.5-4.2 s after SAIN logged "Player Null from ID" for a
/// grenade THROWN under a dead bot's profile id. There is one such profile per
/// raid: the Labyrinth boss bossTagillaAgro, whose <c>PrewarmMines</c> queues
/// tripwires that <c>BossLogicUpdate</c> then plants
/// (<c>BossTagillaAgro.cs:69-85</c>). The raids that did not release
/// bodies show the same activations, 2-9 of them, and no exception at all;
/// the release raid that had none of them had no exception either. Details
/// and the diagnostic reports that pinned it are in CORPSE-CLEANUP-REVIEW.md,
/// "Settled".
///
/// The chain, verified in ../_decompiled/:
///
/// 1. A tripwire keeps its planter as <c>PlacerPlayerId</c>
///    (<c>EFT.SynchronizableObjects/TripwireSynchronizableObject.cs:160</c>) for
///    as long as it exists — an id, not a reference, so nothing about the
///    planter's death or release touches it.
/// 2. A bot walks into it long after the planter died. <c>ActivateGrenade</c>
///    (<c>:274-298</c>) creates the grenade under that id, which is the
///    "throw" SAIN cannot resolve, and the fuse runs ~3.5 s.
/// 3. The explosion builds its <c>DamageInfo</c> through <c>Shot.Create</c>
///    with the thrower's id (<c>EFT/Grenade.cs:363-369</c>), and every fragment
///    is a <c>Shot</c> as well. <c>Shot</c> resolves the id through
///    <c>GameWorld.GetEverExistedBridgeByProfileID</c>
///    (<c>EFT.Ballistics/Shot.cs:460</c>), the one table nothing prunes — so
///    the victim is handed a live bridge whose <c>iPlayer</c> is the body Arena
///    destroyed.
/// 4. The victim bot's <c>BotMemory.GetHit</c> → <c>SetUnderFire</c> →
///    <c>BotWeaponManager.OnBulletNear</c> reads <c>source.Position</c> behind an
///    <c>IPlayer</c> null check that Unity's lifetime operator never reaches
///    (<c>BotWeaponManager.cs:199-205</c>), and throws. A fragment reaches the
///    same frames through <c>Player.ApplyShot</c>, which is why
///    <c>ShotDelegate</c> reported the same exceptions a second time.
///
/// So a body stays hidden while anything in the world still names it:
///
/// - a TRIPWIRE it planted that has not finished. The measured case. Read from
///   the sync-object processor's own two lists, which hold a tripwire from
///   <c>TakeFromPool</c> until it is returned — unlike <c>TripwireManager</c>,
///   which drops it the moment it is TRIGGERED (<c>EFT/GameWorld.cs:1698-1702</c>),
///   exactly when its grenade starts burning. Finished means Exploded (30 s
///   after the grenade went, <c>DestroyedSeconds</c> in globals.json, so the
///   fragments have long landed) or Inert (disarmed or expired, the grenade
///   dropped as plain loot). Every other state names the planter, and a state
///   this code does not know counts as naming it.
/// - a GRENADE it threw or a FLARE it fired, still in <c>GameWorld.Grenades</c>.
///   Not observed; the same resolution as above, reached by a grenade thrown or
///   a flare fired just before death. A tripwire's grenade is never in that
///   list (<c>RegisterGrenade</c> is only called for thrown grenades and flares,
///   <c>EFT/Player.cs:14381,15664-15668</c>), which is why the tripwire itself is
///   read. A grenade whose explosion reaches nobody — a smoke — does not
///   count; any other kind of throwable counts as naming every body.
/// - a SHOT it fired, still in flight — bullets and every fragment, because a
///   grenade's calculator IS the world's shared one
///   (<c>EFT/GameWorld.cs:1061-1064</c>).
///
/// What this cannot see is the one deferral left after all three are gone:
/// <c>BotMemory.GetHit</c> may run <c>GetHitInner</c> from a timer with the
/// <c>DamageInfo</c> captured (<c>EFT/BotMemory.cs:679-700</c>). The database
/// sets that delay to -1 (never) for every bot type, but SAIN sets 0.4 s and
/// 0.1 s. CorpseCleanupService covers it with a quiet period instead of a
/// reading: see <c>DamageSourcesQuiet</c> there. Bleeding does NOT belong here:
/// it applies damage through the health controller directly
/// (<c>ActiveHealthController.Bleeding.BleedHealth</c>), never through
/// <c>Player.ApplyDamageInfo</c>, so it never reaches <c>OnBulletNear</c>; the
/// death it may cause is <see cref="CorpseAggressors"/>' business.
///
/// Profile id, not reference, because an id is what each of these resolves at
/// impact — the flare keeps an <c>IPlayer</c>, and reads its id there too.
/// It can only ever match more bodies, never fewer, and the only participant
/// whose bodies share one — the local player — is never released anyway.
///
/// The cost is the one a hidden body has always had. A tripwire nobody walks
/// into keeps its planter hidden until it expires (<c>TRIPWIRE_INERT_TIME</c>,
/// 1200 s unless the bot's difficulty says otherwise), and there is one planter
/// in a Labyrinth raid.
/// </summary>
internal static class CorpseDamageSources
{
    private static bool _reportedFailure;

    internal static void Reset()
    {
        _reportedFailure = false;
    }

    /// <summary>
    /// What <see cref="Describe"/> answers when it cannot read the world. The
    /// warning is said once; the sweep does not repeat it per body.
    /// </summary>
    internal const string Unreadable = "a world this code cannot read";

    /// <summary>
    /// True while anything in the world still names this profile as the source
    /// of damage, and true as well when that cannot be established. Fails
    /// CLOSED, for the reason <see cref="CorpseAggressors"/> gives: a body that
    /// stays hidden costs what it always cost, a body that is destroyed too early
    /// throws inside another bot's damage handling.
    /// </summary>
    internal static bool NamesAsSource(string profileId, GameWorld world)
        => Describe(profileId, world) != null;

    /// <summary>
    /// The same question, answered with what still names this profile — for the
    /// sweep's log line, which is what shows in a raid that the guard held a body
    /// and why. Null when nothing does.
    /// </summary>
    internal static string Describe(string profileId, GameWorld world)
    {
        if (string.IsNullOrEmpty(profileId) || world == null)
            return Unreadable;

        try
        {
            return UnfinishedTripwire(profileId, world)
                ?? LiveThrowable(profileId, world)
                ?? ShotInFlight(profileId, world);
        }
        catch (Exception ex)
        {
            WarnOnce(ex.GetBaseException().Message);
            return Unreadable;
        }
    }

    private static string UnfinishedTripwire(string profileId, GameWorld world)
    {
        // Created with the world (GameWorld.InitLevel) and never cleared before
        // it: missing means this is not a world this code understands.
        var processor = world.SynchronizableObjectLogicProcessor
            ?? throw new InvalidOperationException("the world has no synchronizable-object processor");

        return AnyUnfinished(processor._activeSynchronizableObjects, profileId, "active")
            ?? AnyUnfinished(processor._staticSynchronizableObjects, profileId, "static");
    }

    private static string AnyUnfinished(List<SynchronizableObject> objects, string profileId, string which)
    {
        if (objects == null)
            throw new InvalidOperationException($"the {which} synchronizable-object list is missing");

        for (var i = 0; i < objects.Count; i++)
        {
            // By state, never by Unity lifetime: a burning grenade is its own
            // object and goes off whatever happens to the tripwire it came from.
            if (objects[i] is TripwireSynchronizableObject tripwire
                && !IsFinished(tripwire.TripwireState)
                && tripwire.PlacerPlayerId.EqualsToString(profileId))
            {
                return $"a tripwire it planted ({tripwire.TripwireState})";
            }
        }

        return null;
    }

    /// <summary>
    /// The two states after which a tripwire cannot hurt anybody any more
    /// (TripwireSynchronizableObject.cs: Exploded at 360-366, Inert at 246-259),
    /// plus None, which a pooled tripwire has after Reset cleared its planter
    /// (382-408). Wait (armed), Active (grenade burning), Exploding (fragments in
    /// the air) and anything added later all still name the planter.
    /// </summary>
    private static bool IsFinished(ETripwireState state)
        => state == ETripwireState.Exploded
            || state == ETripwireState.Inert
            || state == ETripwireState.None;

    private static string LiveThrowable(string profileId, GameWorld world)
    {
        var throwables = world.Grenades
            ?? throw new InvalidOperationException("the world has no grenade registry");

        for (var i = 0; i < throwables.Count; i++)
        {
            // Unity lifetime check on purpose: a destroyed throwable has gone off
            // already, and its fragments are read below. Left in the list it
            // would otherwise hold its thrower forever.
            var throwable = throwables.GetByIndex(i);
            if (throwable == null)
                continue;

            switch (throwable)
            {
                case Grenade grenade:
                    if (Same(grenade.ProfileId, profileId) && CanReachAnybody(grenade.WeaponSource))
                        return "a grenade it threw";
                    break;

                case FlareCartridge flare:
                    if (Same(FlareShooter(flare)?.ProfileId, profileId))
                        return "a flare it fired";
                    break;

                default:
                    // A kind this code has not read may name anybody.
                    return $"a {throwable.GetType().Name} this code cannot read";
            }
        }

        return null;
    }

    /// <summary>
    /// Whether this grenade's explosion can touch anybody at all. The native
    /// explosion collects its victims inside
    /// max(2 x MaxExplosionDistance, Blindness.y, Contusion.y) and fires its
    /// fragments only at them (<c>EFT/ExplosionSharedMethods.cs:306-328,366</c>).
    /// Both smoke grenades in the database carry zero for all three, and a smoke
    /// is the one grenade never destroyed after it goes off
    /// (<c>SmokeGrenade.OnExplosion</c> replaces the base DestroyImmediate), so
    /// it stays in the registry and counting it would keep its thrower hidden
    /// for the rest of the raid for nothing. Found by the adversarial review.
    /// An item that cannot be read counts.
    /// </summary>
    private static bool CanReachAnybody(IExplosiveItem item)
        => item == null
            || item.MaxExplosionDistance > 0f
            || item.Blindness.y > 0f
            || item.Contusion.y > 0f;

    private static FieldInfo _flareShooter;

    /// <summary>
    /// A flare keeps its shooter as a private <c>IPlayer</c> and resolves it on
    /// impact through the same never-pruned table, by
    /// <c>GetEverExistedBridgeByProfileID(_player.ProfileId)</c>
    /// (<c>EFT/FlareCartridge.cs:30,176-190</c>). Reflection rather than a
    /// Harmony accessor because this is read only while a flare is in the air.
    /// </summary>
    private static IPlayer FlareShooter(FlareCartridge flare)
    {
        if (_flareShooter == null)
        {
            var field = typeof(FlareCartridge).GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || !typeof(IPlayer).IsAssignableFrom(field.FieldType))
                throw new MissingFieldException("FlareCartridge._player changed");

            _flareShooter = field;
        }

        return _flareShooter.GetValue(flare) as IPlayer;
    }

    private static string ShotInFlight(string profileId, GameWorld world)
    {
        // The shared calculator is created with the world (GameWorld.InitLevel),
        // so missing is a world this code does not understand. The client one
        // exists only once a networked ClientPlayer has fired; offline, where
        // every body is a LocalPlayer, it normally never does.
        var inFlight = AnyShot(world.SharedBallisticsCalculator ?? throw new InvalidOperationException("the world has no ballistics calculator"), profileId)
            || (world.ClientBallisticCalculator != null && AnyShot(world.ClientBallisticCalculator, profileId));

        return inFlight ? "a shot it fired, still in flight" : null;
    }

    private static bool AnyShot(IBallisticsCalculator calculator, string profileId)
    {
        if (!(calculator is BallisticsCalculator concrete))
            throw new InvalidOperationException($"unknown ballistics calculator {calculator.GetType().Name}");

        var shots = concrete.Shots
            ?? throw new InvalidOperationException("the ballistics calculator has no shot list");

        for (var i = 0; i < shots.Count; i++)
        {
            var shot = shots[i];
            if (shot != null && Same(shot.PlayerProfileID, profileId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Case-insensitive like MongoID.EqualsToString, the comparison the
    /// tripwire side uses: matching more can only keep more bodies.
    /// </summary>
    private static bool Same(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void WarnOnce(string reason)
    {
        if (_reportedFailure)
            return;

        _reportedFailure = true;
        Plugin.ModLogger.LogWarning(
            "Arena corpse release cannot read what may still name a body as a damage source ("
            + reason
            + "); no body will be taken out of the scene while that holds.");
    }
}
