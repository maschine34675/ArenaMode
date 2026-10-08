using ArenaMode.Config;
using ArenaMode.Session;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using JsonType;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Effects;

/// <summary>
/// Reuses EFT's native HDR tracer mesh for every player-owned Arena projectile
/// of a TEAM mode (Team Deathmatch, protected Tactical 5v5), including ammunition
/// whose shared template has Tracer=false. Ammo templates are never changed: that
/// flag also alters bot scatter and is shared by normal raids. Colored tracers
/// are team-only like the laser colors: <see cref="Settings.TracerEntry"/> has no
/// entry for Deathmatch, Gun Game, solo Zombie or solo Tactical, so the prefix below
/// returns the complete native method there regardless of TeamTracers. Unknown
/// protected-5v5 identities and every non-Arena call fail open the same way.
/// </summary>
internal static class ArenaTracerPatches
{
    private const float MinimumSegmentSqrMagnitude = 0.001f;
    private const float FirstSegmentMinimumSqrMagnitude = 100f;

    private static bool _loggedRendererFailure;

    public static void Enable() => new TeamTracerPatch().Enable();

    internal static void ResetDiagnostics() => _loggedRendererFailure = false;

    private sealed class TeamTracerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(
                typeof(TracersLight),
                nameof(TracersLight.CreateTracer),
                new[] { typeof(Shot) });

        [PatchPrefix]
        private static bool Prefix(TracersLight __instance, Shot shot)
        {
            if (!ArenaSession.IsActive || Settings.TracerEntry(ArenaSession.ActiveMode, ArenaTracerTeams.ProtectedTeamMatch)?.Value != true)
                return true;

            try
            {
                string profileId = shot?.PlayerProfileID?.Trim();
                bool verifiedPlayerIdentity = HasMatchingPlayerIdentity(shot, profileId);
                ArenaTracerSide side = ArenaTracerTeams.Classify(
                    profileId,
                    verifiedPlayerIdentity);
                if (side == ArenaTracerSide.Unknown)
                {
                    if (ArenaTracerTeams.ShouldWarnUnknown(profileId))
                    {
                        Plugin.ModLogger.LogWarning(
                            $"Arena tracer kept native rendering for unknown protected-team shooter '{profileId}'.");
                    }

                    return true;
                }

                var history = shot.PositionHistory;
                if (history == null || history.Count < 2)
                    return false;

                Vector3 p0 = history[history.Count - 2];
                Vector3 p1 = history[history.Count - 1];
                float segmentSqrMagnitude = (p1 - p0).sqrMagnitude;
                if (segmentSqrMagnitude < MinimumSegmentSqrMagnitude
                    || (history.Count == 2
                        && segmentSqrMagnitude < FirstSegmentMinimumSqrMagnitude))
                {
                    return false;
                }

                __instance.Color = ArenaTracerPalette.Resolve(
                    side == ArenaTracerSide.Friendly
                        ? Settings.FriendlyTracerColor.Value
                        : Settings.OpponentTracerColor.Value);

                // Preserve EFT's special short/small visualization for native
                // yellow grenade tracers; all other and formerly tracerless ammo
                // uses the normal bullet trail.
                if (shot.Ammo is Ammo ammo
                    && ammo.TracerColor == TaxonomyColor.tracerYellow)
                {
                    __instance.CreateGrenadeTracer(shot, p0, p1);
                }
                else
                {
                    __instance.CreateShotTracer(shot, p0, p1);
                }

                return false;
            }
            catch (Exception ex)
            {
                if (!_loggedRendererFailure)
                {
                    _loggedRendererFailure = true;
                    Plugin.ModLogger.LogWarning(
                        $"Arena team tracer rendering failed open to EFT: {ex.GetBaseException().Message}");
                }

                return true;
            }
        }
    }

    private static bool HasMatchingPlayerIdentity(Shot shot, string profileId)
    {
        if (shot?.Player?.iPlayer == null || string.IsNullOrEmpty(profileId))
            return false;

        try
        {
            return string.Equals(
                shot.Player.iPlayer.ProfileId?.Trim(),
                profileId,
                StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
