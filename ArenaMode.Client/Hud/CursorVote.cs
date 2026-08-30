using Redeploy.Core.Ui;
using System.Collections.Generic;

namespace ArenaMode.Hud;

/// <summary>
/// Arbitrates the single cursor vote between the panels that want it.
///
/// CursorRequestNode.Want is one shared flag inside the process, not a count.
/// With two panels calling it directly, whichever closed first revoked the
/// cursor from the one still open, and the raid re-locked and warped the
/// pointer out from under it.
///
/// Same shape as ArenaInputBlocker.Request, and for the same reason: the
/// resource is shared, so the ownership has to be.
/// </summary>
internal static class CursorVote
{
    private static readonly HashSet<object> Wanters = new HashSet<object>();

    public static void Set(object owner, bool wanted)
    {
        if (owner == null)
            return;

        lock (Wanters)
        {
            if (wanted)
                Wanters.Add(owner);
            else
                Wanters.Remove(owner);

            CursorRequestNode.Want(Wanters.Count > 0);
        }
    }
}
