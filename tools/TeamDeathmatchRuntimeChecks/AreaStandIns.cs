using ArenaMode.Shared;
using UnityEngine;

namespace ArenaMode.Areas;

// Consumer contract only: readiness and final position verdicts are controlled
// external inputs. The linked production TDM mode owns size/hold/abort behavior.
internal static class ArenaAreaRuntime
{
    internal static ArenaAreaDefinition CurrentArea;
    internal static int? FrozenTeamSize;
    internal static int? CurrentTeamSize => FrozenTeamSize ?? CurrentArea?.MaxTeamSize;
    internal static int TeamAssemblyReleases, PlayerReservationReleases;
    internal static void FinishTeamAssembly() { TeamAssemblyReleases++; }
    internal static void ReleasePlayerReservation(EFT.Player player) { PlayerReservationReleases++; }
    internal static bool IsPending;
    internal static Func<Vector3, bool> PositionAccepted = _ => true;
    internal static readonly List<string> Aborts = new();
    internal static int PositionChecks;
    internal static void RequirePosition(Vector3 position)
    {
        PositionChecks++;
        if (CurrentArea != null && (IsPending || !PositionAccepted(position)))
            throw new InvalidOperationException("host-supplied area position rejection");
    }
    internal static void Abort(string reason) { Aborts.Add(reason); IsPending = true; }
    internal static void Reset()
    { CurrentArea = null; FrozenTeamSize = null; IsPending = false; PositionAccepted = _ => true; Aborts.Clear(); PositionChecks = 0; TeamAssemblyReleases = PlayerReservationReleases = 0; }
}
