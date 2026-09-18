using ArenaMode.Config;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Diagnostics;

/// <summary>
/// A per-raid record of the bodies <see cref="Bots.CorpseCleanupService"/> has
/// destroyed, so that a later exception can be asked the one question that
/// matters about the objects on its stack: was this one a body Arena took away,
/// and how long ago?
///
/// It deliberately keeps no <c>Player</c> reference. A destroyed body's managed
/// shell still holds its profile, inventory and controllers, and the long-raid
/// heap is an open question of its own (CORPSE-CLEANUP-REVIEW.md) — a
/// diagnostic must not be the reason anything stays reachable. The profile id
/// is identity enough here: every bot's own id is unique to it, and the local
/// player's own repeats across every body they have ever had — including an
/// older one this service releases since 2026-09-18 (only the NEWEST own body
/// stays exempt), so <see cref="Describe"/>'s newest-match-wins read gives the
/// answer that matters for that id: how long ago the MOST RECENT release of
/// it was, own or not.
///
/// Recording is gated on the same switch as the reports, so this costs nothing
/// while it is off.
/// </summary>
internal static class CorpseReleaseLedger
{
    /// <summary>
    /// Bounded on purpose: a long Deathmatch releases hundreds of bodies, and
    /// this is a debugging aid rather than a ledger anybody audits. The oldest
    /// entry goes, and <see cref="Describe"/> says so rather than reporting a
    /// body it has forgotten as never released.
    /// </summary>
    private const int Capacity = 256;

    private readonly struct Entry
    {
        internal readonly string ProfileId;
        internal readonly float At;
        internal readonly int Sweep;

        internal Entry(string profileId, float at, int sweep)
        {
            ProfileId = profileId;
            At = at;
            Sweep = sweep;
        }
    }

    private static readonly List<Entry> Released = new List<Entry>();
    private static int _total;

    internal static void Reset()
    {
        Released.Clear();
        _total = 0;
    }

    /// <summary>
    /// Called on a confirmed release only, with an identity read BEFORE the body
    /// was destroyed.
    /// </summary>
    internal static void Record(string profileId, int sweep)
    {
        if (!Settings.LogDamageExceptions.Value || string.IsNullOrEmpty(profileId))
            return;

        _total++;
        if (Released.Count >= Capacity)
            Released.RemoveAt(0);

        Released.Add(new Entry(profileId, Time.time, sweep));
    }

    /// <summary>One field for a report line; never throws and never guesses.</summary>
    internal static string Describe(string profileId)
    {
        if (string.IsNullOrEmpty(profileId))
            return "release=?";

        // Newest first: an id can only be in here once, but the answer that
        // matters is "how long ago", so read from the end regardless.
        for (var i = Released.Count - 1; i >= 0; i--)
        {
            var entry = Released[i];
            if (!string.Equals(entry.ProfileId, profileId, StringComparison.Ordinal))
                continue;

            var ago = (Time.time - entry.At).ToString("0.0");
            return $"release=RELEASED({ago}s ago, sweep {entry.Sweep})";
        }

        // "Not in the record" and "not released" are the same answer only while
        // nothing has been evicted.
        return Released.Count >= Capacity
            ? $"release=not-in-last-{Capacity}-of-{_total}"
            : "release=no";
    }
}
