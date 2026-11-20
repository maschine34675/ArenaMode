using System.Reflection;
using System.Text.RegularExpressions;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace ArenaMode.Server.Rewards;

// Pack A Punch (PaP 2.x) keeps its data out of the profile file: it strips it as SPT writes the profile and keeps
// it in a side file <PaP folder>/profiles/<id>.json. At the next start PaP restores the data only when the side
// file is not older than the profile by more than two minutes (PaPProfileStore.Restore: a profile saved without
// the mod since means the data could overlap whatever took its place, so the side file is set aside as .stale).
//
// Arena's startup writes a profile directly (enrollment, recovery, payout) and without PaP's write pipeline, so the
// profile's mtime moves forward and a side file older than two minutes would be set aside at the next start, which
// loses the player's PaP data. This guard carries a FRESH side file forward to the new profile's mtime. It only ever
// touches the mtime of an existing, fresh side file, never its content, never a .stale file, and never creates one.
// Every failure here is swallowed: the profile write must go on exactly as without the guard.
internal static class PapSideFileGuard
{
    internal const string ModGuid = "com.kennerz.paphideoutmodule";
    internal const string AssemblyName = "PaPHideoutServer";
    internal const int SupportedMajorVersion = 2;
    // PaPProfileStore.StaleAfter: the stale test is strictly "profile - side > 2:00".
    internal static readonly TimeSpan FreshWindow = TimeSpan.FromMinutes(2);

    private static readonly Regex ProfileFile = new(@"^([0-9a-fA-F]{24})\.json$", RegexOptions.CultureInvariant);

    private static IReadOnlyList<SptMod> _mods = [];
    private static string? _directory;
    private static bool _resolved;
    private static int _warned;
    private static int _noted;

    // Tests: the PaP server folder. The default finds the loaded PaP mod (cached per process, reset by Configure).
    internal static Func<string?> PapDirectory = DefaultPapDirectory;
    // Tests: an assembly's folder. A dynamic assembly has no Location.
    internal static Func<Assembly, string?> LocationOf = a => a.Location;
    // Tests: the time setter, to prove that a failure does not fail the write.
    internal static Action<string, DateTime> SetWriteTime = File.SetLastWriteTimeUtc;

    // Called by ArenaRewardStartup before it writes anything. The mod list is the one SPT injected, so a similarly
    // named assembly that is not the PaP mod can never stand in for it.
    internal static void Configure(IReadOnlyList<SptMod> mods)
    {
        _mods = mods ?? [];
        _directory = null;
        _resolved = false;
    }

    private static string? DefaultPapDirectory()
    {
        if (_resolved) return _directory;
        string? directory = null;
        try { directory = Discover(_mods, LocationOf); }
        catch { /* not discoverable: PaP is treated as absent */ }
        _directory = directory;
        _resolved = true;
        return directory;
    }

    // The folder of the PaP server assembly of the loaded mod whose GUID is PaP's, for PaP 2.x only.
    internal static string? Discover(IReadOnlyList<SptMod> mods, Func<Assembly, string?> locationOf)
    {
        var matches = mods.Where(m => string.Equals(m.ModMetadata?.ModGuid, ModGuid, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) return null;
        var mod = matches[0];
        if (mod.ModMetadata.Version.Major != SupportedMajorVersion)
        {
            Note("PaP side-file carry is off: only PaP major version 2 is known (side file stays as the mod left it).");
            return null;
        }
        var assembly = mod.Assemblies.FirstOrDefault(a => a.GetName().Name == AssemblyName);
        string? location = assembly is null ? null : locationOf(assembly);
        if (string.IsNullOrEmpty(location)) return null;
        string? directory = Path.GetDirectoryName(location);
        return string.IsNullOrEmpty(directory) ? null : directory;
    }

    // The side file to carry past this write of the profile, or null (nothing to do). Never throws.
    internal static string? Prepare(string root, string profilePath)
    {
        try
        {
            string? papDirectory = PapDirectory();
            if (papDirectory is null) return null;
            // WriteAtomic also serves user/arena-rewards/**, the Cola files and the private root: only the real
            // runtime profiles directory and a 24-hex-digit profile name qualify.
            var match = ProfileFile.Match(Path.GetFileName(profilePath));
            if (!match.Success) return null;
            string? directory = Path.GetDirectoryName(Path.GetFullPath(profilePath));
            string profiles = Path.Combine(RewardStartupFiles.Canonical(root), "user", "profiles");
            if (directory is null
                || !string.Equals(Path.TrimEndingDirectorySeparator(directory), profiles, RewardStartupFiles.PathComparison))
                return null;
            // File.GetLastWriteTimeUtc of a missing file is 1601 and does not throw: guard both files explicitly.
            if (!File.Exists(profilePath)) return null; // a new profile has nothing to carry
            string side = Path.Combine(papDirectory, "profiles", match.Groups[1].Value.ToLowerInvariant() + ".json");
            if (!File.Exists(side)) return null; // never create one
            DateTime previous = File.GetLastWriteTimeUtc(profilePath);
            DateTime sideTime = File.GetLastWriteTimeUtc(side);
            // Already stale (strictly more than two minutes older): PaP sets it aside at its next start anyway, and
            // a carry must never revive it. A side file newer than the profile is fresh.
            return previous - sideTime > FreshWindow ? null : side;
        }
        catch { return null; }
    }

    // Sets the side file to the time the staged profile will carry after the move (the move preserves it, so the
    // two end up exactly equal). Never moves a side file backwards; never throws.
    internal static void Carry(string sidePath, DateTime tempWriteUtc)
    {
        try
        {
            if (File.GetLastWriteTimeUtc(sidePath) >= tempWriteUtc) return;
            SetWriteTime(sidePath, tempWriteUtc);
            ArenaContext.Logger?.Debug($"[ArenaMode] Pack A Punch side file kept fresh for profile {Alias(sidePath)}.");
        }
        catch (Exception e)
        {
            if (Interlocked.Exchange(ref _warned, 1) == 0)
                ArenaContext.Logger?.Warning($"[ArenaMode] Could not keep the Pack A Punch side file of profile {Alias(sidePath)} "
                    + $"fresh ({e.GetType().Name}); PaP may set its data aside at the next start.");
        }
    }

    private static string Alias(string sidePath)
    {
        try { return RewardIdentity.Hash(Path.GetFileNameWithoutExtension(sidePath))[..8]; }
        catch { return "unavailable"; }
    }

    private static void Note(string message)
    {
        if (Interlocked.Exchange(ref _noted, 1) == 0) ArenaContext.Logger?.Debug("[ArenaMode] " + message);
    }

    internal static void ResetForTests()
    {
        _mods = [];
        _directory = null;
        _resolved = false;
        _warned = 0;
        _noted = 0;
        PapDirectory = DefaultPapDirectory;
        LocationOf = a => a.Location;
        SetWriteTime = File.SetLastWriteTimeUtc;
    }
}
