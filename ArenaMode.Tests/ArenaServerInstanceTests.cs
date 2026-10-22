using System.Reflection;
using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Routers;
using ArenaMode.Server.Services;
using ArenaMode.SessionHelper;
using ArenaMode.Shared;
using ArenaMode.Shared.Session;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace ArenaMode.Tests;

/// <summary>
/// Server half of the Arena session feature (docs/ARENA-SESSION-20261021.md,
/// section 8): the extra ping fields (arenaInstance, serverMods and, on the Arena
/// instance only, the policyHash of its marker), the Arena-instance refusal of
/// non-Arena raid starts and the startup lines. A normal server must behave
/// exactly as before; several cases below pin that explicitly.
/// </summary>
[Collection(ArenaContextSerialCollection.Name)]
public sealed class ArenaServerInstanceTests : IDisposable
{
    private const string Token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ProcessId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string RequestId = "cccccccccccccccccccccccccccccccc";
    private static readonly MongoId Session = new("0123456789abcdef01234567");

    private static readonly FieldInfo RouterInstance = typeof(ArenaModeRouter).GetField(
        "Instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "arena-server-instance-" + Guid.NewGuid().ToString("N"));
    private readonly string _noMarkerDirectory;

    private readonly ArenaSessionService? _oldSessions = ArenaContext.Sessions;
    private readonly ProfileSnapshotService? _oldSnapshots = ArenaContext.Snapshots;
    private readonly ArenaProtectedRaidStartService? _oldProtectedStart = ArenaContext.ProtectedRaidStart;
    private readonly OrdinaryRaidLifecycleService? _oldOrdinary = ArenaContext.OrdinaryRaids;
    private readonly ISptLogger<PatchManager>? _oldLogger = ArenaContext.Logger;
    private readonly object? _oldRouter = RouterInstance.GetValue(null);

    public ArenaServerInstanceTests()
    {
        Directory.CreateDirectory(_root);
        _noMarkerDirectory = Path.Combine(_root, "no-marker");
        Directory.CreateDirectory(_noMarkerDirectory);
        ResetInstance();

        // Every start case below runs without any service, so a refusal can only
        // come from the Arena-instance check and the fall-through cases show
        // exactly what the patch did before this feature.
        ArenaContext.Sessions = null;
        ArenaContext.Snapshots = null;
        ArenaContext.ProtectedRaidStart = null;
        ArenaContext.OrdinaryRaids = null;
    }

    public void Dispose()
    {
        ResetInstance();
        ArenaContext.Sessions = _oldSessions;
        ArenaContext.Snapshots = _oldSnapshots;
        ArenaContext.ProtectedRaidStart = _oldProtectedStart;
        ArenaContext.OrdinaryRaids = _oldOrdinary;
        ArenaContext.Logger = _oldLogger;
        RouterInstance.SetValue(null, _oldRouter);
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* a temp folder left behind is harmless */ }
    }

    private void ResetInstance() => ArenaServerInstance.Initialize([], _noMarkerDirectory);

    private const string MarkerHash = "3755e0db97983440";

    /// <summary>
    /// Makes <see cref="_root"/>/<paramref name="name"/> an Arena server tree. Unless
    /// <paramref name="markerText"/> says otherwise the marker is the one the session
    /// helper itself writes (its own writer, so the reader here is tested against the
    /// real format), carrying <see cref="MarkerHash"/>.
    /// </summary>
    private string ArenaTree(string name = "tree", string? markerText = null)
    {
        string tree = Path.Combine(_root, name);
        Directory.CreateDirectory(tree);
        File.WriteAllText(
            Path.Combine(tree, ArenaSessionContract.MarkerFileName),
            markerText ?? HelperMarker.Create(
                new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc), "6ab7db1164f04b565cf78be7", 6970,
                @"C:\SPT41\SPT_Runtime", MarkerHash, "0.1.3", @"C:\t\server\SPT.Server.exe").ToJson());
        return tree;
    }

    private static SptMod Mod(string guid, string name, string version, string directory,
        Dictionary<string, SemanticVersioning.Range>? dependencies = null) => new()
    {
        Directory = directory,
        Assemblies = [],
        ModMetadata = new TestModMetadata
        {
            ModGuid = guid,
            Name = name,
            Version = new SemanticVersioning.Version(version),
            ModDependencies = dependencies,
        },
    };

    // ArenaMode.Server.ModMetadata also implements the Blazor marker, whose
    // assembly the test host does not carry; the ping only needs the base contract.
    private sealed record TestModMetadata : IModMetadata
    {
        public string ModGuid { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Author { get; init; } = "test";
        public List<string>? Contributors { get; init; }
        public SemanticVersioning.Version Version { get; init; } = new("1.0.0");
        public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
        public bool HasPrepatcher { get; init; }
        public List<string>? Incompatibilities { get; init; }
        public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
        public string? Url { get; init; }
        public string License { get; init; } = "MIT";
    }

    private static SptMod[] SampleMods() =>
    [
        Mod("com.maschine.ArenaMode", "ArenaMode", "0.1.3", "./user/mods/ArenaMode"),
        Mod("me.sol.sain", "SAIN", "4.5.1", "./user/mods/Solarint-SAIN-ServerMod"),
    ];

    private static StartLocalRaidRequestData Start(string? identity) => new()
    {
        ServerId = identity,
        Location = ArenaMapCatalog.FactoryDayId,
    };

    private static (bool Continue, ValueTask<string> Result) RunPrefix(StartLocalRaidRequestData? info)
    {
        ValueTask<string> result = default;
        bool proceed = ArenaStartLocalRaidPatch.Prefix(info!, Session, CancellationToken.None, ref result);
        return (proceed, result);
    }

    private ArenaModeRouter Router()
    {
        var router = new ArenaModeRouter(null!, null!, new InRaidConfig
            {
                RaidMenuSettings = new RaidMenuSettings { AiAmount = "AsOnline", AiDifficulty = "AsOnline" },
                CarExtracts = [],
                CoopExtracts = [],
            },
            BotTierScalePatchTests.TestLogger.Create<ArenaModeRouter>());
        return router;
    }

    private static async Task<JsonDocument> Ping(ArenaModeRouter router)
        => JsonDocument.Parse((string)await router.HandleStaticAsync(
            Protocol.PingRoute, null, Session, string.Empty));

    /// <summary>Installs a recording logger as <see cref="ArenaContext.Logger"/>.</summary>
    private static Captured Recording()
    {
        var logger = DispatchProxy.Create<ISptLogger<PatchManager>, RecordingLogger>();
        ArenaContext.Logger = logger;
        return new Captured(logger, (RecordingLogger)(object)logger);
    }

    private sealed record Captured(ISptLogger<PatchManager> Logger, RecordingLogger Log)
    {
        public List<(string Level, string Text)> Lines => Log.Lines;
    }

    // ------------------------------------------------------------------ marker

    [Fact]
    public void TheMarkerFileInTheWorkingDirectoryMakesAnArenaInstance()
    {
        ArenaServerInstance.Initialize(SampleMods(), _noMarkerDirectory);
        Assert.False(ArenaServerInstance.IsArenaInstance);

        ArenaServerInstance.Initialize(SampleMods(), ArenaTree());
        Assert.True(ArenaServerInstance.IsArenaInstance);
    }

    [Fact]
    public void AMarkerOnlyCountsDirectlyInTheWorkingDirectory()
    {
        string tree = ArenaTree("outer");
        string below = Path.Combine(tree, "user");
        Directory.CreateDirectory(below);

        ArenaServerInstance.Initialize([], below);

        Assert.False(ArenaServerInstance.IsArenaInstance);
    }

    [Fact]
    public void AFolderWithTheMarkerNameIsNotAMarker()
    {
        string tree = Path.Combine(_root, "folder-marker");
        Directory.CreateDirectory(Path.Combine(tree, ArenaSessionContract.MarkerFileName));

        ArenaServerInstance.Initialize([], tree);

        Assert.False(ArenaServerInstance.IsArenaInstance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnUnusableWorkingDirectoryIsNeverAnArenaInstance(string? workingDirectory)
        => Assert.False(ArenaServerInstance.HasMarker(workingDirectory));

    [Fact]
    public void AMissingWorkingDirectoryIsNeverAnArenaInstance()
    {
        ArenaServerInstance.Initialize([], Path.Combine(_root, "does", "not", "exist"));

        Assert.False(ArenaServerInstance.IsArenaInstance);
    }

    [Fact]
    public void InitializeReplacesEarlierState()
    {
        ArenaServerInstance.Initialize(SampleMods(), ArenaTree());
        Assert.True(ArenaServerInstance.IsArenaInstance);
        Assert.Equal(2, ArenaServerInstance.Mods.Count);
        Assert.Equal(MarkerHash, ArenaServerInstance.PolicyHash);

        ArenaServerInstance.Initialize([], _noMarkerDirectory);

        Assert.False(ArenaServerInstance.IsArenaInstance);
        Assert.Empty(ArenaServerInstance.Mods);
        Assert.Null(ArenaServerInstance.PolicyHash);
    }

    // ------------------------------------------------------------------ policy hash

    [Fact]
    public void TheArenaInstanceReadsThePolicyHashOfTheMarkerTheHelperWrites()
    {
        ArenaServerInstance.Initialize(SampleMods(), ArenaTree());

        Assert.Equal(MarkerHash, ArenaServerInstance.PolicyHash);
    }

    [Fact]
    public void ANormalServerHasNoPolicyHash()
    {
        ArenaServerInstance.Initialize(SampleMods(), _noMarkerDirectory);

        Assert.Null(ArenaServerInstance.PolicyHash);
    }

    [Fact]
    public void TheHashIsReturnedAsWrittenNotTrimmedOrNormalised()
    {
        // The client compares the string with the hash of its own policy, so the server must not
        // second-guess it: no trimming, no case change, a marker from a newer helper with extra
        // members and a byte order mark still reads.
        string tree = ArenaTree("as-written");
        File.WriteAllText(
            Path.Combine(tree, ArenaSessionContract.MarkerFileName),
            "{ \"schema\": 1, \"future\": [1, 2], \"policyHash\": \" AbC123 \" }",
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        ArenaServerInstance.Initialize([], tree);

        Assert.True(ArenaServerInstance.IsArenaInstance);
        Assert.Equal(" AbC123 ", ArenaServerInstance.PolicyHash);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schema\":1}")]
    [InlineData("{\"policyHash\":null}")]
    [InlineData("{\"policyHash\":\"\"}")]
    [InlineData("{\"policyHash\":\"   \"}")]
    [InlineData("{\"policyHash\":42}")]
    [InlineData("{\"policyHash\":true}")]
    [InlineData("{\"policyHash\":[\"3755e0db97983440\"]}")]
    [InlineData("{\"policyHash\":{\"value\":\"3755e0db97983440\"}}")]
    // The shared reader is strict on purpose: these are not "almost JSON that still works".
    [InlineData("{\"policyHash\":\"a\",\"policyHash\":\"b\"}")]
    [InlineData("{\"policyHash\":\"3755e0db97983440\"")]
    [InlineData("{\"policyHash\":\"3755e0db97983440\",}")]
    [InlineData("[\"3755e0db97983440\"]")]
    [InlineData("\"3755e0db97983440\"")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("")]
    public void AMarkerWithoutAUsableHashGivesNull(string markerText)
    {
        string tree = ArenaTree("no-hash", markerText);

        Assert.Null(ArenaServerInstance.ReadPolicyHash(tree));

        // It is still the Arena instance: the marker exists, only its hash is unknown.
        ArenaServerInstance.Initialize(SampleMods(), tree);
        Assert.True(ArenaServerInstance.IsArenaInstance);
        Assert.Null(ArenaServerInstance.PolicyHash);
    }

    [Fact]
    public void AMarkerThatCannotBeReadGivesNullAndStaysAnArenaInstance()
    {
        string tree = ArenaTree("locked");

        // Exclusive handle: File.Exists still sees the marker, reading it fails with a sharing violation.
        using (new FileStream(
                   Path.Combine(tree, ArenaSessionContract.MarkerFileName), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ArenaServerInstance.Initialize(SampleMods(), tree);
        }

        Assert.True(ArenaServerInstance.IsArenaInstance);
        Assert.Null(ArenaServerInstance.PolicyHash);
        // Once the lock is gone the file is fine, so the null above came from the failed read only.
        Assert.Equal(MarkerHash, ArenaServerInstance.ReadPolicyHash(tree));
    }

    [Fact]
    public void ReadPolicyHashNeverThrowsForAnUnusableDirectory()
    {
        string folderMarker = Path.Combine(_root, "folder-marker-hash");
        Directory.CreateDirectory(Path.Combine(folderMarker, ArenaSessionContract.MarkerFileName));

        Assert.Null(ArenaServerInstance.ReadPolicyHash(null));
        Assert.Null(ArenaServerInstance.ReadPolicyHash(string.Empty));
        Assert.Null(ArenaServerInstance.ReadPolicyHash(_noMarkerDirectory));
        Assert.Null(ArenaServerInstance.ReadPolicyHash(Path.Combine(_root, "does", "not", "exist")));
        Assert.Null(ArenaServerInstance.ReadPolicyHash(folderMarker));
        Assert.Null(ArenaServerInstance.ReadPolicyHash("bad\0path"));
    }

    [Fact]
    public async Task TheMarkerIsReadOnceAtStartupNotPerPing()
    {
        string tree = ArenaTree("read-once");
        string marker = Path.Combine(tree, ArenaSessionContract.MarkerFileName);
        ArenaServerInstance.Initialize(SampleMods(), tree);
        var router = Router();

        File.WriteAllText(marker, "{\"policyHash\":\"ffffffffffffffff\"}");
        using (var changed = await Ping(router))
            Assert.Equal(MarkerHash, changed.RootElement.GetProperty("policyHash").GetString());

        File.Delete(marker);
        using (var removed = await Ping(router))
        {
            Assert.True(removed.RootElement.GetProperty("arenaInstance").GetBoolean());
            Assert.Equal(MarkerHash, removed.RootElement.GetProperty("policyHash").GetString());
        }
    }

    // ------------------------------------------------------------------ mod list

    [Fact]
    public void ModsCarryGuidNameVersionAndAnAbsoluteFolder()
    {
        ArenaServerInstance.Initialize(SampleMods(), _noMarkerDirectory);

        var mods = ArenaServerInstance.Mods;
        Assert.Equal(2, mods.Count);
        Assert.Equal("com.maschine.ArenaMode", mods[0].Guid);
        Assert.Equal("ArenaMode", mods[0].Name);
        Assert.Equal("0.1.3", mods[0].Version);
        Assert.Equal("me.sol.sain", mods[1].Guid);
        Assert.Equal("SAIN", mods[1].Name);
        Assert.Equal("4.5.1", mods[1].Version);
        Assert.All(mods, m => Assert.True(Path.IsPathRooted(m.Folder), m.Folder));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "user", "mods", "Solarint-SAIN-ServerMod")),
            mods[1].Folder);
    }

    [Fact]
    public void ModsCarryTheGuidsOfTheirModDependenciesSortedAndNeverNull()
    {
        ArenaServerInstance.Initialize(
        [
            Mod("x.none", "None", "1.0.0", "./user/mods/None"),
            Mod("x.empty", "Empty", "1.0.0", "./user/mods/Empty", []),
            Mod("x.user", "User", "1.0.0", "./user/mods/User", new Dictionary<string, SemanticVersioning.Range>
            {
                ["com.wtt.commonlib"] = new("~3.0.6"),
                ["a.first"] = new("*"),
                [" "] = new("*"),
            }),
        ], _noMarkerDirectory);

        var mods = ArenaServerInstance.Mods;
        Assert.Empty(mods[0].Dependencies);
        Assert.Empty(mods[1].Dependencies);
        Assert.Equal(["a.first", "com.wtt.commonlib"], mods[2].Dependencies);
    }

    [Fact]
    public void ModOrderIsTheServersLoadOrder()
    {
        SptMod[] mods =
        [
            Mod("b.second", "B", "1.0.0", "./user/mods/B"),
            Mod("a.first", "A", "1.0.0", "./user/mods/A"),
        ];

        ArenaServerInstance.Initialize(mods, _noMarkerDirectory);

        Assert.Equal(["b.second", "a.first"], ArenaServerInstance.Mods.Select(m => m.Guid));
    }

    [Fact]
    public void AnAbsoluteFolderStaysAndATrailingSeparatorIsDropped()
    {
        string absolute = Path.Combine(_root, "elsewhere", "Mod");
        ArenaServerInstance.Initialize(
        [
            Mod("x.plain", "Plain", "1.0.0", absolute),
            Mod("x.slash", "Slash", "1.0.0", absolute + Path.DirectorySeparatorChar),
        ], _noMarkerDirectory);

        Assert.Equal(absolute, ArenaServerInstance.Mods[0].Folder);
        Assert.Equal(absolute, ArenaServerInstance.Mods[1].Folder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnresolvableFolderIsEmptyNeverRelative(string directory)
    {
        ArenaServerInstance.Initialize([Mod("x.none", "None", "1.0.0", directory)], _noMarkerDirectory);

        var mod = Assert.Single(ArenaServerInstance.Mods);
        Assert.Equal(string.Empty, mod.Folder);
        Assert.Equal("x.none", mod.Guid);
    }

    [Fact]
    public void ANullModListIsEmpty()
    {
        ArenaServerInstance.Initialize(null, _noMarkerDirectory);

        Assert.Empty(ArenaServerInstance.Mods);
    }

    // ------------------------------------------------------------------ ping

    [Fact]
    public async Task PingOfANormalServerGainsExactlyTheTwoNewFields()
    {
        ArenaServerInstance.Initialize(SampleMods(), _noMarkerDirectory);

        using var ping = await Ping(Router());
        var root = ping.RootElement;

        // The version stays; arenaInstance and serverMods are the Arena session fields. The old
        // admissionFailure member is gone: the ISB refusal it carried belongs to the whitelist now
        // (the client still reads such a member when an older server sends one).
        Assert.Equal(
            ["arenaInstance", "serverMods", "version"],
            root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.False(root.GetProperty("arenaInstance").GetBoolean());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("version").GetString()));
        // policyHash belongs to the Arena instance: a normal server's ping has no such member at all
        // (not even a null), so it is unchanged for every client that has never heard of the member.
        Assert.False(root.TryGetProperty("policyHash", out _));
    }

    [Fact]
    public async Task PingListsEveryLoadedModWithGuidNameVersionAndFolder()
    {
        ArenaServerInstance.Initialize(SampleMods(), _noMarkerDirectory);

        using var ping = await Ping(Router());
        var mods = ping.RootElement.GetProperty("serverMods");

        Assert.Equal(JsonValueKind.Array, mods.ValueKind);
        Assert.Equal(2, mods.GetArrayLength());
        var sain = mods[1];
        Assert.Equal(
            ["dependencies", "folder", "guid", "name", "version"],
            sain.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("me.sol.sain", sain.GetProperty("guid").GetString());
        Assert.Equal("SAIN", sain.GetProperty("name").GetString());
        Assert.Equal("4.5.1", sain.GetProperty("version").GetString());
        Assert.True(Path.IsPathRooted(sain.GetProperty("folder").GetString()!));
        Assert.Equal(JsonValueKind.Array, sain.GetProperty("dependencies").ValueKind);
        Assert.Equal(0, sain.GetProperty("dependencies").GetArrayLength());
    }

    [Fact]
    public async Task PingCarriesTheModDependenciesAsAnArrayOfGuids()
    {
        ArenaServerInstance.Initialize(
        [
            Mod("com.wtt.contentbackport", "WTT-ContentBackport", "1.0.0", "./user/mods/WTT-ContentBackport",
                new Dictionary<string, SemanticVersioning.Range> { ["com.wtt.commonlib"] = new("~3.0.6") }),
        ], _noMarkerDirectory);

        using var ping = await Ping(Router());
        var dependencies = ping.RootElement.GetProperty("serverMods")[0].GetProperty("dependencies");

        Assert.Equal(["com.wtt.commonlib"], dependencies.EnumerateArray().Select(d => d.GetString()));
    }

    [Fact]
    public async Task PingOfTheArenaInstanceSaysSo()
    {
        ArenaServerInstance.Initialize(SampleMods(), ArenaTree());

        using var ping = await Ping(Router());

        Assert.True(ping.RootElement.GetProperty("arenaInstance").GetBoolean());
        Assert.Equal(2, ping.RootElement.GetProperty("serverMods").GetArrayLength());
    }

    [Fact]
    public async Task PingOfTheArenaInstanceAddsExactlyThePolicyHashOfItsMarker()
    {
        ArenaServerInstance.Initialize(SampleMods(), ArenaTree());

        using var ping = await Ping(Router());
        var root = ping.RootElement;

        Assert.Equal(
            ["arenaInstance", "policyHash", "serverMods", "version"],
            root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(JsonValueKind.String, root.GetProperty("policyHash").ValueKind);
        Assert.Equal(MarkerHash, root.GetProperty("policyHash").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"policyHash\":null}")]
    [InlineData("{\"policyHash\":7}")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task PingOfAnArenaInstanceWithoutAUsableHashCarriesAnExplicitNull(string markerText)
    {
        ArenaServerInstance.Initialize(SampleMods(), ArenaTree("null-hash", markerText));

        using var ping = await Ping(Router());
        var root = ping.RootElement;

        // The member is there (the Arena instance always reports it) and says "unknown".
        Assert.True(root.TryGetProperty("policyHash", out var hash));
        Assert.Equal(JsonValueKind.Null, hash.ValueKind);
        Assert.True(root.GetProperty("arenaInstance").GetBoolean());
    }

    // ------------------------------------------------------------------ refusal

    [Fact]
    public async Task ArenaInstanceRefusesAnUntaggedStartWithAnSptErrorBody()
    {
        ArenaServerInstance.Initialize(SampleMods(), ArenaTree());
        var logger = Recording();

        var (proceed, result) = RunPrefix(Start(identity: null));

        Assert.False(proceed);
        Assert.True(result.IsCompletedSuccessfully);
        using var body = JsonDocument.Parse(await result);
        // SPT's {err, errmsg, data} shape, the one the EFT client parses.
        Assert.Equal((int)ArenaServerInstance.OrdinaryStartRefusalCode, body.RootElement.GetProperty("err").GetInt32());
        string message = body.RootElement.GetProperty("errmsg").GetString()!;
        Assert.Contains("only Arena raids", message);
        Assert.Contains("RETURN TO NORMAL SESSION", message);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("data").ValueKind);
        var warning = Assert.Single(logger.Lines, l => l.Level == "Warning");
        Assert.Contains(Session.ToString(), warning.Text);
    }

    [Fact]
    public void TheRefusalCodeIsOneTheClientDoesNotRetry()
    {
        // EFT's ClientRequestErrorHandler.HandleError reports this code as
        // "Expected"; unmapped codes and HTTP 500 are retried after 20/40/60 s.
        Assert.Equal(BackendErrorCodes.UnknownScriptExecutionError, ArenaServerInstance.OrdinaryStartRefusalCode);
        Assert.Equal(1000, (int)ArenaServerInstance.OrdinaryStartRefusalCode);
    }

    [Theory]
    [InlineData("vanilla-server-id")]
    [InlineData("")]
    [InlineData("arenamode-ordinary-start:" + ProcessId + ":" + RequestId)]
    [InlineData("arenamode-ordinary-start:malformed")]
    public async Task ArenaInstanceRefusesEveryStartThatIsNotArenaTagged(string identity)
    {
        ArenaServerInstance.Initialize(SampleMods(), ArenaTree());
        Recording();

        var (proceed, result) = RunPrefix(Start(identity));

        Assert.False(proceed);
        using var body = JsonDocument.Parse(await result);
        Assert.Equal(1000, body.RootElement.GetProperty("err").GetInt32());
    }

    [Fact]
    public async Task ArenaInstanceRefusesAMissingRequestToo()
    {
        ArenaServerInstance.Initialize(SampleMods(), ArenaTree());
        Recording();

        var (proceed, result) = RunPrefix(null);

        Assert.False(proceed);
        using var body = JsonDocument.Parse(await result);
        Assert.Equal(1000, body.RootElement.GetProperty("err").GetInt32());
    }

    [Fact]
    public void ArenaTaggedStartsAreNotRefusedByTheInstanceCheck()
    {
        ArenaServerInstance.Initialize(SampleMods(), ArenaTree());
        var logger = Recording();

        var (proceed, result) = RunPrefix(Start(Protocol.BuildNativeStartIdentity(Token)));

        // With no protected-start service the Arena branch fails closed exactly as
        // it always did; the point is that it is that branch, not the refusal.
        Assert.False(proceed);
        Assert.True(result.IsFaulted);
        var error = Assert.Throws<InvalidOperationException>(() => result.GetAwaiter().GetResult());
        Assert.Contains("protected-start services are unavailable", error.Message);
        Assert.DoesNotContain(logger.Lines, l => l.Level == "Warning");
    }

    [Fact]
    public void ARefusalLeavesAnArmedSessionUntouched()
    {
        ArenaContext.Sessions = new ArenaSessionService(
            BotTierScalePatchTests.TestLogger.Create<ArenaSessionService>());
        ArenaContext.Sessions.Arm(Session, ArenaMapCatalog.FactoryDayId, "teamdeathmatch", armToken: Token);
        ArenaServerInstance.Initialize(SampleMods(), ArenaTree());
        Recording();

        var (proceed, _) = RunPrefix(Start("vanilla-server-id"));

        Assert.False(proceed);
        Assert.Equal(ArenaSessionState.Armed, ArenaContext.Sessions.GetStatus(Session).state);
        Assert.Equal(Token, ArenaContext.Sessions.GetArmToken(Session));
    }

    [Fact]
    public void ANormalServerStartsOrdinaryRaidsExactlyAsBefore()
    {
        ArenaServerInstance.Initialize(SampleMods(), _noMarkerDirectory);
        var logger = Recording();

        var (proceed, result) = RunPrefix(Start("vanilla-server-id"));

        // No Arena state anywhere: the prefix hands the call to vanilla SPT.
        Assert.True(proceed);
        Assert.Equal(default, result);
        Assert.Empty(logger.Lines);
    }

    [Fact]
    public void ANormalServerKeepsItsOrdinaryTaggedFailClosedBranch()
    {
        ArenaServerInstance.Initialize(SampleMods(), _noMarkerDirectory);
        var logger = Recording();

        var (proceed, result) = RunPrefix(Start(Protocol.BuildNativeOrdinaryStartIdentity(ProcessId, RequestId)));

        Assert.False(proceed);
        Assert.True(result.IsFaulted);
        var error = Assert.Throws<InvalidOperationException>(() => result.GetAwaiter().GetResult());
        Assert.Contains("lifecycle service is unavailable", error.Message);
        Assert.Empty(logger.Lines);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void RefusesStartOnlyForNonArenaStartsOnTheArenaInstance(bool arenaInstance, bool arenaTagged, bool refused)
    {
        ArenaServerInstance.Initialize([], arenaInstance ? ArenaTree() : _noMarkerDirectory);

        Assert.Equal(refused, ArenaServerInstance.RefusesStart(arenaTagged));
    }

    // ------------------------------------------------------------------ startup line

    [Fact]
    public void ArenaInstanceLogsOneInfoLineWithTheLoadedMods()
    {
        ArenaServerInstance.Initialize(SampleMods(), ArenaTree());
        var logger = Recording();

        ArenaServerInstance.LogStartup(logger.Logger);

        var line = Assert.Single(logger.Lines);
        Assert.Equal("Info", line.Level);
        Assert.Equal(
            "[ArenaMode] Arena session server: 2 mods loaded (ArenaMode, SAIN)",
            line.Text);
    }

    [Fact]
    public void AnArenaInstanceWithoutAPolicyHashWarnsAfterTheInfoLine()
    {
        ArenaServerInstance.Initialize(SampleMods(), ArenaTree("startup-no-hash", "{}"));
        var logger = Recording();

        ArenaServerInstance.LogStartup(logger.Logger);

        Assert.Equal(2, logger.Lines.Count);
        Assert.Equal("Info", logger.Lines[0].Level);
        Assert.StartsWith("[ArenaMode] Arena session server: 2 mods loaded", logger.Lines[0].Text);
        Assert.Equal("Warning", logger.Lines[1].Level);
        Assert.StartsWith("[ArenaMode] ", logger.Lines[1].Text);
        Assert.Contains("policyHash", logger.Lines[1].Text);
        Assert.Contains(ArenaSessionContract.MarkerFileName, logger.Lines[1].Text);
    }

    [Fact]
    public void ThePolicyHashWarningExistsOnlyForAnArenaInstanceThatLacksOne()
    {
        ArenaServerInstance.Initialize(SampleMods(), ArenaTree());
        Assert.Null(ArenaServerInstance.MissingPolicyHashWarning());

        ArenaServerInstance.Initialize(SampleMods(), ArenaTree("warn-no-hash", "{}"));
        Assert.NotNull(ArenaServerInstance.MissingPolicyHashWarning());

        // A normal server has no hash by design and says nothing about it.
        ArenaServerInstance.Initialize(SampleMods(), _noMarkerDirectory);
        Assert.Null(ArenaServerInstance.MissingPolicyHashWarning());
    }

    [Fact]
    public void TheStartupLineFallsBackToTheGuidForANamelessMod()
    {
        ArenaServerInstance.Initialize(
        [
            Mod("com.maschine.ArenaMode", "ArenaMode", "0.1.3", "./user/mods/ArenaMode"),
            Mod("x.nameless", "", "1.0.0", "./user/mods/Nameless"),
        ], ArenaTree());

        Assert.Equal(
            "Arena session server: 2 mods loaded (ArenaMode, x.nameless)",
            ArenaServerInstance.StartupLine());
    }

    [Fact]
    public void ANormalServerLogsNothingAtStartup()
    {
        ArenaServerInstance.Initialize(SampleMods(), _noMarkerDirectory);
        var logger = Recording();

        ArenaServerInstance.LogStartup(logger.Logger);

        Assert.Null(ArenaServerInstance.StartupLine());
        Assert.Empty(logger.Lines);
    }

    // ------------------------------------------------------------------ wiring

    [Fact]
    public void ThePatchManagerInitializesBeforeEnablingTheStartPatchAndLogsAfterwards()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root, "ArenaMode.Server", "PatchManager.cs"));

        int initialize = source.IndexOf("ArenaServerInstance.Initialize(loadedMods)", StringComparison.Ordinal);
        int enable = source.IndexOf("new ArenaStartLocalRaidPatch().Enable()", StringComparison.Ordinal);
        int log = source.IndexOf("ArenaServerInstance.LogStartup(logger)", StringComparison.Ordinal);

        Assert.True(initialize >= 0, "PatchManager must initialize ArenaServerInstance");
        Assert.True(enable > initialize, "the refusal reads the state, so it must exist before the patch is enabled");
        Assert.True(log > enable, "the startup line is written once the patches are in");
    }

    [Fact]
    public void TheStartPatchChecksTheInstanceBeforeAnyOtherBranch()
    {
        string source = File.ReadAllText(Path.Combine(
            RepoFiles.Root, "ArenaMode.Server", "Patches", "ArenaStartLocalRaidPatch.cs"));

        int refusal = source.IndexOf("ArenaServerInstance.RefusesStart(arenaTagged)", StringComparison.Ordinal);
        int ordinaryTagged = source.IndexOf("ordinaryRaids.GetOrStart", StringComparison.Ordinal);
        int arenaProtected = source.IndexOf("protectedStart.GetOrStart", StringComparison.Ordinal);
        int untagged = source.IndexOf("protectedStart.StartOrdinaryAsync", StringComparison.Ordinal);

        Assert.True(refusal >= 0);
        Assert.True(refusal < ordinaryTagged && refusal < arenaProtected && refusal < untagged);
    }

    /// <summary>Records the text of every Info/Warning/Error/Debug call.</summary>
    public class RecordingLogger : DispatchProxy
    {
        internal readonly List<(string Level, string Text)> Lines = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is { Name: "Info" or "Warning" or "Error" or "Debug" or "Critical" }
                && args is { Length: > 0 })
            {
                Lines.Add((targetMethod.Name, args[0]?.ToString() ?? string.Empty));
            }

            return targetMethod?.ReturnType == typeof(void) ? null
                : targetMethod?.ReturnType.IsValueType == true ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }
}
