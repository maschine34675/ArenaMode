using System;

namespace ArenaMode.Session;

/// <summary>
/// Frozen Arena affiliation, separate from the real PMC faction. The caller
/// proves the exact loading/active game before asking this lease to bind it.
/// Only scalars and one exact game/world pair survive the loading boundary.
/// </summary>
internal static class ArenaTeamSideSelection
{
    internal const string ProfileChoice = "Profile";
    internal const string UsecChoice = "USEC";
    internal const string BearChoice = "BEAR";
    private static string _profileId, _token;
    private static int _epoch;
    private static object _game, _world;
    internal static bool? SelectedUsec { get; private set; }
    internal static bool? NativeUsec { get; private set; }

    internal static bool TryResolveChoice(string choice, bool nativeUsec, out bool usec)
    {
        usec = nativeUsec;
        if (string.Equals(choice, ProfileChoice, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(choice, UsecChoice, StringComparison.OrdinalIgnoreCase)) { usec = true; return true; }
        if (string.Equals(choice, BearChoice, StringComparison.OrdinalIgnoreCase)) { usec = false; return true; }
        return false;
    }

    internal static void Capture(string mode, string choice, bool nativeUsec, string profileId, int epoch, string token)
    {
        Reset();
        if (!string.Equals(mode, "teamdeathmatch", StringComparison.Ordinal)) return;
        if (!TryResolveChoice(choice, nativeUsec, out bool usec)
            || string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(token))
            throw new InvalidOperationException("The Team DM side or local identity is unavailable.");
        SelectedUsec = usec;
        NativeUsec = nativeUsec;
        _profileId = profileId;
        _epoch = epoch;
        _token = token;
    }

    internal static bool TryResolve(int epoch, string token, object game, object world,
        string profileId, bool nativeUsec, out bool selectedUsec)
    {
        selectedUsec = false;
        if (!SelectedUsec.HasValue || !NativeUsec.HasValue || NativeUsec.Value != nativeUsec
            || epoch != _epoch || !string.Equals(token, _token, StringComparison.Ordinal)
            || !string.Equals(profileId, _profileId, StringComparison.Ordinal)
            || ReferenceEquals(game, null) || ReferenceEquals(world, null)) return false;
        if (_game != null && (!ReferenceEquals(game, _game) || !ReferenceEquals(world, _world))) return false;
        _game = game;
        _world = world;
        selectedUsec = SelectedUsec.Value;
        return true;
    }

    internal static void Reset()
    {
        SelectedUsec = NativeUsec = null;
        _profileId = _token = null;
        _game = _world = null;
        _epoch = 0;
    }
}
