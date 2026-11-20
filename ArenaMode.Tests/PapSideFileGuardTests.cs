using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Server.Rewards;
using SPTarkov.Server.Core.Models.Spt.Mod;
using Xunit;

namespace ArenaMode.Tests;

// Pack A Punch keeps its profile data in a side file and sets the file aside as .stale when the profile is newer
// than it by more than two minutes. Arena's startup writes (WriteAtomic) must keep a fresh side file fresh, and
// nothing else. These tests use the real filesystem and PaP's own formula: stale means profile - side > 2:00.
// The serial collection: the guard is process-wide static state.
[Collection(ArenaContextSerialCollection.Name)]
public sealed class PapSideFileGuardTests : IDisposable
{
    private static readonly TimeSpan PapWindow = TimeSpan.FromMinutes(2);
    private readonly string _base = Path.Combine(Path.GetTempPath(), "ArenaMode.PapSideFile.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _root;
    private readonly string _pap;
    private int _next;

    public PapSideFileGuardTests()
    {
        PapSideFileGuard.ResetForTests();
        _root = Path.Combine(_base, "runtime");
        _pap = Path.Combine(_base, "pap");
        Directory.CreateDirectory(Path.Combine(_root, "user", "profiles"));
        Directory.CreateDirectory(Path.Combine(_root, "user", "arena-rewards", "cola"));
        Directory.CreateDirectory(Path.Combine(_pap, "profiles"));
        PapSideFileGuard.PapDirectory = () => _pap;
    }

    public void Dispose()
    {
        PapSideFileGuard.ResetForTests();
        try { Directory.Delete(_base, recursive: true); } catch { }
    }

    // ---- fixtures

    private string NewId() => (0xdad000 + ++_next).ToString("x6").PadLeft(24, 'd');
    private string ProfilePath(string id) => Path.Combine(_root, "user", "profiles", id + ".json");
    private string SidePath(string id) => Path.Combine(_pap, "profiles", id.ToLowerInvariant() + ".json");

    // A profile and its side file, `gap` apart (side older by gap), the profile ten minutes in the past.
    private (string Id, DateTime ProfileTime) Seed(TimeSpan gap, string? id = null)
    {
        id ??= NewId();
        var profileTime = DateTime.UtcNow.AddMinutes(-10);
        File.WriteAllText(ProfilePath(id), "{\"old\":true}");
        File.WriteAllText(SidePath(id), "{\"side\":true}");
        File.SetLastWriteTimeUtc(ProfilePath(id), profileTime);
        File.SetLastWriteTimeUtc(SidePath(id), profileTime - gap);
        return (id, profileTime);
    }

    private void Write(string id, string json = "{\"new\":true}") => RewardStartupFiles.WriteAtomic(_root, ProfilePath(id), json);

    // PaP's PaPProfileStore.Restore: the side file is set aside when the profile is strictly newer than it by > 2:00.
    private static bool PapWouldKeep(string profile, string side)
        => File.GetLastWriteTimeUtc(profile) - File.GetLastWriteTimeUtc(side) <= PapWindow;

    private static SptMod Mod(string guid, string version, params string[] assemblyNames) => new()
    {
        Directory = "synthetic",
        Assemblies = assemblyNames.Select(n => (Assembly)AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(n), AssemblyBuilderAccess.Run)).ToList(),
        ModMetadata = new TestMetadata { ModGuid = guid, Name = guid, Version = new SemanticVersioning.Version(version) },
    };

    // IModMetadata only: the production ModMetadata also implements the Blazor marker, whose assembly the test
    // host does not load.
    private record TestMetadata : IModMetadata
    {
        public string ModGuid { get; init; } = "";
        public string Name { get; init; } = "";
        public string Author { get; init; } = "tests";
        public List<string>? Contributors { get; init; }
        public SemanticVersioning.Version Version { get; init; } = new("1.0.0");
        public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
        public bool HasPrepatcher { get; init; }
        public List<string>? Incompatibilities { get; init; }
        public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
        public string? Url { get; init; }
        public string License { get; init; } = "MIT";
    }

    private void UseRealDiscovery(params SptMod[] mods)
    {
        PapSideFileGuard.ResetForTests();
        PapSideFileGuard.LocationOf = a => Path.Combine(_pap, a.GetName().Name + ".dll");
        PapSideFileGuard.Configure(mods);
    }

    // ---- 1 to 4

    [Theory]
    [InlineData(0)]
    [InlineData(119)]
    [InlineData(120)]
    public void FreshSideFileIsCarriedForward(int gapSeconds)
    {
        var (id, _) = Seed(TimeSpan.FromSeconds(gapSeconds));
        Write(id);
        Assert.Equal("{\"new\":true}", File.ReadAllText(ProfilePath(id)));
        Assert.True(PapWouldKeep(ProfilePath(id), SidePath(id)));
        Assert.Equal("{\"side\":true}", File.ReadAllText(SidePath(id))); // content is never touched
    }

    [Fact]
    public void CarriedSideTimeEqualsNewProfileTime()
    {
        var (id, profileTime) = Seed(TimeSpan.FromSeconds(90));
        Write(id);
        var profileNow = File.GetLastWriteTimeUtc(ProfilePath(id));
        Assert.True(profileNow > profileTime.AddMinutes(5)); // the write moved the profile forward
        Assert.Equal(profileNow, File.GetLastWriteTimeUtc(SidePath(id)));
    }

    [Theory]
    [InlineData(121)]
    [InlineData(600)]
    public void AlreadyStaleSideFileIsNotRevived(int gapSeconds)
    {
        var (id, profileTime) = Seed(TimeSpan.FromSeconds(gapSeconds));
        var sideTime = File.GetLastWriteTimeUtc(SidePath(id));
        Write(id);
        Assert.Equal(sideTime, File.GetLastWriteTimeUtc(SidePath(id)));
        Assert.Equal("{\"side\":true}", File.ReadAllText(SidePath(id)));
        Assert.False(PapWouldKeep(ProfilePath(id), SidePath(id)));
        Assert.True(File.GetLastWriteTimeUtc(ProfilePath(id)) > profileTime);
    }

    [Fact]
    public void MissingSideFileIsNeverCreatedAndStaleFileIsNeverTouched()
    {
        string id = NewId();
        File.WriteAllText(ProfilePath(id), "{\"old\":true}");
        File.SetLastWriteTimeUtc(ProfilePath(id), DateTime.UtcNow.AddMinutes(-10));
        string stale = SidePath(id) + ".stale";
        File.WriteAllText(stale, "{\"stale\":true}");
        var staleTime = DateTime.UtcNow.AddHours(-3);
        File.SetLastWriteTimeUtc(stale, staleTime);

        Write(id);

        Assert.False(File.Exists(SidePath(id)));
        Assert.Equal([Path.GetFileName(stale)], Directory.GetFiles(Path.Combine(_pap, "profiles")).Select(Path.GetFileName).ToArray());
        Assert.Equal(staleTime, File.GetLastWriteTimeUtc(stale));
        Assert.Equal("{\"stale\":true}", File.ReadAllText(stale));
    }

    // ---- 5 to 7: which mod is Pack A Punch

    [Fact]
    public void NoPapModMeansNoTouch()
    {
        var (id, _) = Seed(TimeSpan.Zero);
        var sideTime = File.GetLastWriteTimeUtc(SidePath(id));
        UseRealDiscovery(Mod("some.other.mod", "2.0.0", "SomethingElse"));
        Write(id);
        Assert.Equal(sideTime, File.GetLastWriteTimeUtc(SidePath(id)));

        UseRealDiscovery(); // not even a mod list
        Write(id);
        Assert.Equal(sideTime, File.GetLastWriteTimeUtc(SidePath(id)));
    }

    [Theory]
    [InlineData("1.9.0")]
    [InlineData("3.0.0")]
    [InlineData("0.1.0")]
    public void PapMajorVersionOtherThan2MeansNoTouch(string version)
    {
        var (id, _) = Seed(TimeSpan.Zero);
        var sideTime = File.GetLastWriteTimeUtc(SidePath(id));
        UseRealDiscovery(Mod(PapSideFileGuard.ModGuid, version, PapSideFileGuard.AssemblyName));
        Write(id);
        Assert.Equal(sideTime, File.GetLastWriteTimeUtc(SidePath(id)));
    }

    [Fact]
    public void LoadedPapModOfMajorVersion2IsFoundByItsGuidAndAssembly()
    {
        var (id, _) = Seed(TimeSpan.FromSeconds(60));
        UseRealDiscovery(Mod(PapSideFileGuard.ModGuid, "2.3.1", PapSideFileGuard.AssemblyName));
        Write(id);
        Assert.Equal(File.GetLastWriteTimeUtc(ProfilePath(id)), File.GetLastWriteTimeUtc(SidePath(id)));
    }

    [Fact]
    public void GuidMismatchOrAssemblyNameOnlyMeansNoTouch()
    {
        var (id, _) = Seed(TimeSpan.Zero);
        var sideTime = File.GetLastWriteTimeUtc(SidePath(id));
        // A different mod that merely ships an assembly of that name must not stand in for PaP.
        UseRealDiscovery(Mod("com.spoof.mod", "2.0.0", PapSideFileGuard.AssemblyName));
        Write(id);
        Assert.Equal(sideTime, File.GetLastWriteTimeUtc(SidePath(id)));
        // The PaP guid without its server assembly.
        UseRealDiscovery(Mod(PapSideFileGuard.ModGuid, "2.0.0", "NotThePaPServer"));
        Write(id);
        Assert.Equal(sideTime, File.GetLastWriteTimeUtc(SidePath(id)));
        // Two mods claiming the guid are ambiguous: nothing is touched.
        UseRealDiscovery(Mod(PapSideFileGuard.ModGuid, "2.0.0", PapSideFileGuard.AssemblyName),
            Mod(PapSideFileGuard.ModGuid, "2.0.1", PapSideFileGuard.AssemblyName));
        Write(id);
        Assert.Equal(sideTime, File.GetLastWriteTimeUtc(SidePath(id)));
    }

    // ---- 8 to 10

    [Fact]
    public void FourChainedWritesKeepSideFresh()
    {
        var (id, _) = Seed(TimeSpan.FromSeconds(119));
        for (int i = 0; i < 4; i++)
        {
            Write(id, "{\"write\":" + i + "}");
            Assert.True(PapWouldKeep(ProfilePath(id), SidePath(id)), "write " + i);
            Assert.Equal(File.GetLastWriteTimeUtc(ProfilePath(id)), File.GetLastWriteTimeUtc(SidePath(id)));
        }
        Assert.Equal("{\"write\":3}", File.ReadAllText(ProfilePath(id)));
    }

    [Fact]
    public void SetTimeFailureDoesNotFailTheWrite()
    {
        var (id, _) = Seed(TimeSpan.FromSeconds(30));
        var sideTime = File.GetLastWriteTimeUtc(SidePath(id));
        PapSideFileGuard.SetWriteTime = (_, _) => throw new IOException("synthetic: cannot set time");
        Write(id);
        Assert.Equal("{\"new\":true}", File.ReadAllText(ProfilePath(id)));
        Assert.Equal(sideTime, File.GetLastWriteTimeUtc(SidePath(id)));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "user", "profiles"), "*.tmp"));
    }

    [Fact]
    public void PrepareExceptionDoesNotFailTheWrite()
    {
        var (id, _) = Seed(TimeSpan.FromSeconds(30));
        PapSideFileGuard.PapDirectory = () => throw new InvalidOperationException("synthetic: discovery exploded");
        Write(id);
        Assert.Equal("{\"new\":true}", File.ReadAllText(ProfilePath(id)));
        Assert.Null(PapSideFileGuard.Prepare(_root, ProfilePath(id)));
    }

    // ---- 11 to 13

    [Fact]
    public void NonProfilePathsAreIgnored()
    {
        string id = NewId();
        var (profileId, _) = Seed(TimeSpan.FromSeconds(30), id);
        var sideTime = File.GetLastWriteTimeUtc(SidePath(id));
        var old = DateTime.UtcNow.AddMinutes(-10);

        // arena-rewards state and a Cola file under it carry a profile-looking name; the directory excludes them.
        string[] ignored =
        [
            Path.Combine(_root, "user", "arena-rewards", id + ".json"),
            Path.Combine(_root, "user", "arena-rewards", "cola", id + ".json"),
            Path.Combine(_root, "user", "other", id + ".json"),
            Path.Combine(_root, "user", "profiles", "notes.json"),
            Path.Combine(_root, "user", "profiles", id[1..] + ".json"),
            Path.Combine(_root, "user", "profiles", id + ".json.bak"),
        ];
        Directory.CreateDirectory(Path.Combine(_root, "user", "other"));
        foreach (var path in ignored)
        {
            File.WriteAllText(path, "{}");
            File.SetLastWriteTimeUtc(path, old);
            Assert.Null(PapSideFileGuard.Prepare(_root, path));
            RewardStartupFiles.WriteAtomic(_root, path, "{\"x\":1}");
            Assert.Equal(sideTime, File.GetLastWriteTimeUtc(SidePath(id)));
        }
        Assert.Equal(profileId, id);
        Assert.NotNull(PapSideFileGuard.Prepare(_root, ProfilePath(id))); // the real one still qualifies
    }

    [Fact]
    public void UppercaseIdIsMatchedAndSideNameIsLowercase()
    {
        string lower = NewId().Replace('d', 'a');
        string upper = lower.ToUpperInvariant();
        // The profile file carries an uppercase name (PaP's regex is case-insensitive); the side file is lowercase.
        File.WriteAllText(ProfilePath(upper), "{\"old\":true}");
        File.WriteAllText(SidePath(lower), "{\"side\":true}");
        var profileTime = DateTime.UtcNow.AddMinutes(-10);
        File.SetLastWriteTimeUtc(ProfilePath(upper), profileTime);
        File.SetLastWriteTimeUtc(SidePath(lower), profileTime - TimeSpan.FromSeconds(20));

        string? side = PapSideFileGuard.Prepare(_root, ProfilePath(upper));
        Assert.NotNull(side);
        Assert.Equal(lower + ".json", Path.GetFileName(side));
        RewardStartupFiles.WriteAtomic(_root, ProfilePath(upper), "{\"new\":true}");
        Assert.Equal(File.GetLastWriteTimeUtc(ProfilePath(upper)), File.GetLastWriteTimeUtc(SidePath(lower)));
    }

    [Fact]
    public void SideNewerThanProfileIsNotMovedBackwards()
    {
        var (id, _) = Seed(TimeSpan.Zero);
        var future = DateTime.UtcNow.AddHours(1);
        File.SetLastWriteTimeUtc(SidePath(id), future);
        Write(id);
        Assert.Equal(future, File.GetLastWriteTimeUtc(SidePath(id)));
        Assert.True(PapWouldKeep(ProfilePath(id), SidePath(id))); // a newer side file counts as fresh
    }

    // ---- 14

    [Fact]
    public async Task PayoutStartupWriteKeepsSideFresh()
    {
        using var f = new RewardStartupTests.StartupFixture();
        string id = f.Binding.ProfileId;
        string side = Path.Combine(_pap, "profiles", id + ".json");
        File.WriteAllText(side, "{\"side\":true}");
        var old = DateTime.UtcNow.AddMinutes(-10);
        File.SetLastWriteTimeUtc(f.ProfilePath, old);
        File.SetLastWriteTimeUtc(side, old - TimeSpan.FromSeconds(45));

        var result = await f.Process();

        Assert.Equal(RewardSettlementStatus.Paid, result.Result.Status);
        Assert.True(File.GetLastWriteTimeUtc(f.ProfilePath) > old.AddMinutes(5));
        Assert.Equal(File.GetLastWriteTimeUtc(f.ProfilePath), File.GetLastWriteTimeUtc(side));
        Assert.Equal("{\"side\":true}", File.ReadAllText(side));
    }

    // The carry comes before the move: when the move fails (the profile is locked), the side file already carries the
    // staged file's time, which is newer than the untouched old profile and so fresh to PaP. A carry after the move
    // would leave the side file as it was.
    [Fact]
    public void SideFileIsAlreadyCarriedWhenTheMoveFails()
    {
        var (id, profileTime) = Seed(TimeSpan.FromSeconds(90));
        var sideBefore = File.GetLastWriteTimeUtc(SidePath(id));
        using (new FileStream(ProfilePath(id), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<Exception>(() => Write(id)); // IOException or UnauthorizedAccessException, by platform
        }
        Assert.Equal("{\"old\":true}", File.ReadAllText(ProfilePath(id)));
        Assert.Equal(profileTime, File.GetLastWriteTimeUtc(ProfilePath(id)));
        Assert.True(File.GetLastWriteTimeUtc(SidePath(id)) > sideBefore.AddMinutes(5));
        Assert.True(PapWouldKeep(ProfilePath(id), SidePath(id)));
        Assert.Equal("{\"side\":true}", File.ReadAllText(SidePath(id)));
    }

    // The mtime is the only thing the carry changes; the source order (Carry before the move) is pinned here so a
    // reordering cannot slip through unnoticed.
    [Fact]
    public void CarryRunsBeforeTheMoveAndTakesTheStagedFilesTime()
    {
        string source = File.ReadAllText(FindRepoFile("ArenaMode.Server", "Rewards", "RewardStartupFiles.cs")).Replace("\r\n", "\n");
        int carry = source.IndexOf("PapSideFileGuard.Carry(papSideFile, File.GetLastWriteTimeUtc(temporary));", StringComparison.Ordinal);
        int move = source.IndexOf("File.Move(temporary, path, overwrite: true);", StringComparison.Ordinal);
        int prepare = source.IndexOf("PapSideFileGuard.Prepare(root, path)", StringComparison.Ordinal);
        int create = source.IndexOf("FileMode.CreateNew", StringComparison.Ordinal);
        Assert.True(prepare >= 0 && prepare < create, "Prepare comes before the staged file is created");
        Assert.True(create < carry && carry < move, "Carry comes after the staged file is closed and before the move");
        Assert.DoesNotContain("UtcNow", source[carry..(carry + 100)]);
    }

    private static string FindRepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) return Path.Combine([dir.FullName, .. parts]);
        throw new FileNotFoundException("repository root not found");
    }
}
