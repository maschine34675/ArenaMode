using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using AudioSourceContainer = global::Audio.SpatialSystem.AudioSourceContainer;
using AudioSourceContainerData = global::Audio.SpatialSystem.AudioSourceContainers.AudioSourceContainerData;

namespace ArenaMode.Audio;

/// <summary>
/// Keeps the game's sound container pool from holding one container twice
/// (docs/reports/2026-11/AUDIO-OCCLUDER-EXCEPTION-20261120.md; contract in
/// docs/reviews/AUDIO-CONTAINER-GUARD-20261120.md).
///
/// The game (decompile-verified, ../_decompiled): <c>AudioSourceContainerData.ReturnContainer</c> clears a container and
/// hands it to <c>PoolWithoutAlloc.PutObject</c>, which writes it into the lowest empty slot without looking for it
/// in the pool. The spine container of an infected bot that the game's <c>ObservedPlayerAudioSourceLimiter</c> paused
/// and resumed is also in <c>SpatialAudioSystem._activeSoundContainers</c> (only the resume adds it there). At that
/// bot's death the dead-player coroutine <c>ClearPlayerOcclusionWithDelay</c> returns it, and in the same frame
/// <c>method_26</c> finds it invalid in the active list and returns it a second time through
/// <c>UnregisterSourceContainer</c>. The pool then hands the one object to two owners: two infected that share one
/// container for the rest of the raid (each of their deaths returns it again), a one-shot that re-initialises a living
/// bot's container, and, when a bot draws it after a one-shot that the main multi-source occluder still lists under
/// its old id, <c>Can't handle not proposed occlusion test : Combined</c> every frame until that bot dies.
///
/// Measured on SPT-GPU-2 on 2026-11-20, three Zombie raids without this patch: each of the 6 containers seen in both
/// registries lay in two pool slots after its holder's death, and the duplicates lived on as shared containers wave
/// after wave (up to three at once). The exception itself came in run A of the profile pool check (5259 times in five
/// bursts), not in those three raids.
///
/// The prefix skips a return whose container the pool array already holds, by object reference
/// (<see cref="AudioContainerReturnPolicy.IsAlreadyPooled{T}"/>). For the path above that is the second return: the
/// first one has cleared the container and pooled it, and <c>UnregisterSourceContainer</c> has already removed it from
/// the active list and the registry before it calls this. Skipping the whole method loses nothing: the first return's
/// <c>Clear()</c> removed every source (and with it the source subscriptions) and reset the state, and
/// <c>GetContainer</c> clears every container again when it draws it. It measures instead of assuming: a return of a
/// container the pool does not hold runs as in the game, so does any other caller.
///
/// Two cases stay as in the game. A container that somebody draws between the two returns is not in the pool at the
/// second one and is pooled again while its new owner holds it. And a return that the game nests inside another return
/// of the same container is not seen: at the raid's end <c>SpatialAudioSystem.Dispose</c> returns game-object
/// containers that still have sources, their <c>Clear()</c> empties them, the empty-container event runs
/// <c>ClearGoComponents</c> and with it a second <c>UnregisterSourceContainer</c>, which pools the container before the
/// outer call does. The prefix of the outer call ran before either, so that container is pooled twice; the pool lives
/// for one raid and is dropped with it.
///
/// The target is a two-call method. Load order is no protection against inlining: Mono decides it when it compiles a
/// caller, from the method's IL. A copy inlined into a caller would bypass the patch without any error, so the Debug
/// line of a skip is the native proof that the patch is reached; if it never comes while a container stands in two
/// pool slots, the callers are the place to patch.
///
/// Scope: the exact active Arena raid, every mode (the limiter registers only infected, so in practice Zombies).
/// Nothing but such a return changes. Fails open: any exception lets the native method run.
/// </summary>
internal sealed class ArenaAudioContainerReturnGuardPatch : ModulePatch
{
    private static bool _loggedFailure;

    /// <summary>Per raid, so a failure in one raid is reported again in the next.</summary>
    internal static void Reset() => _loggedFailure = false;

    /// <summary>
    /// Enabled behind its own guard like its siblings: a signature that drifted in a game build must cost this guard
    /// only, not every patch listed after it.
    /// </summary>
    internal static void EnablePatch()
    {
        try
        {
            new ArenaAudioContainerReturnGuardPatch().Enable();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                $"Arena audio container guard unavailable; a sound container returned twice is pooled twice as in the game: {ex.Message}");
        }
    }

    protected override MethodBase GetTargetMethod()
        => AccessTools.Method(typeof(AudioSourceContainerData), nameof(AudioSourceContainerData.ReturnContainer),
            new[] { typeof(AudioSourceContainer) });

    /// <summary>
    /// <c>__0</c> is the method's only argument, bound by position so that the binding does not rest on a parameter
    /// name. Returning false skips the game's method (no <c>Clear()</c>, no <c>PutObject</c>); true runs it.
    /// </summary>
    [PatchPrefix]
    internal static bool Prefix(AudioSourceContainerData __instance, AudioSourceContainer __0)
    {
        if (!ArenaSession.IsActive)
            return true;

        try
        {
            if (__0 == null || __instance == null || !IsExactActiveRaid())
                return true;

            var slots = __instance._audioContainersPool?.gparam_0;
            if (!AudioContainerReturnPolicy.IsAlreadyPooled(slots, __0))
                return true;

            Plugin.ModLogger.LogDebug(
                $"Arena audio container guard: sound container {__0.ID} is already in the pool and was not pooled a second time.");
            return false;
        }
        catch (Exception ex)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Plugin.ModLogger.LogWarning(
                    $"Arena audio container guard skipped; this sound container is returned as in the game: {ex.Message}");
            }

            return true;
        }
    }

    private static bool IsExactActiveRaid()
        => Singleton<GameWorld>.Instantiated && Singleton<AbstractGame>.Instantiated
            && ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, Singleton<GameWorld>.Instance);
}
