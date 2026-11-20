using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Rewards;
using ArenaMode.Server.Routers;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;
using Xunit;

namespace ArenaMode.Tests;

// The pipeline-aware save proof (ProfileWriteProof + the capture patches) against the real SPT FileUtil.
// The serial collection: the patches are process-wide and other tests share FileUtil.
[Collection(ArenaContextSerialCollection.Name)]
public sealed class ProfileWriteProofTests : IDisposable
{
    private const string Ledger = "com.maschine.ArenaMode.rewards";
    private readonly string _dir;
    private readonly List<Harmony> _harmonies = [];
    private int _next;

    public ProfileWriteProofTests()
    {
        ProfileWriteProof.ResetForTests();
        _dir = Path.Combine(Path.GetTempPath(), "ArenaMode.ProfileWriteProof.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        ProfileWriteProof.ProfilesDirectory = () => _dir;
        PapLike.Reset();
    }

    public void Dispose()
    {
        foreach (var harmony in _harmonies) harmony.UnpatchSelf();
        ArenaProfileWriteCapturePatches.Disable();
        ProfileWriteProof.ResetForTests();
        PapLike.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    // ---- fixtures

    private string NewId() => (0xabc000 + ++_next).ToString("x6").PadLeft(24, 'a');
    private string PathOf(string id) => Path.Combine(_dir, id + ".json");

    private static JsonObject ProfileNode(string id) => new()
    {
        ["info"] = new JsonObject { ["id"] = id, ["username"] = "tester" },
        [Ledger] = new JsonObject { ["schema"] = 1, ["payments"] = new JsonArray() },
        ["characters"] = new JsonObject
        {
            ["pmc"] = new JsonObject
            {
                ["Info"] = new JsonObject { ["Nickname"] = "pmc" },
                ["Hideout"] = new JsonObject
                {
                    ["Areas"] = new JsonArray(
                        new JsonObject { ["type"] = 1, ["level"] = 1 },
                        new JsonObject { ["type"] = 115, ["level"] = 1 }),
                },
                ["Quests"] = new JsonArray(
                    new JsonObject { ["qid"] = "quest-a", ["status"] = 2 },
                    new JsonObject { ["qid"] = "quest-115", ["status"] = 2 }),
            },
            ["scav"] = new JsonObject { ["Info"] = new JsonObject { ["Nickname"] = "scav" } },
        },
        ["inraid"] = new JsonObject { ["location"] = "none" },
    };

    private static string Profile(string id) => ProfileNode(id).ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static readonly FileUtil Files = new();

    private Task Write(string id, string text) => Files.WriteFileAsync(PathOf(id), text);

    private void Enable() => Assert.True(ArenaProfileWriteCapturePatches.TryEnable());

    private void PatchPap(Func<string, string>? transform = null)
    {
        if (transform is not null) PapLike.Transform = transform;
        var harmony = new Harmony("tests.pap-like." + Guid.NewGuid().ToString("N"));
        harmony.Patch(ArenaProfileWriteCapturePatches.StringTarget!,
            prefix: new HarmonyMethod(typeof(PapLike).GetMethod(nameof(PapLike.Prefix))!));
        _harmonies.Add(harmony);
    }

    private Harmony PatchStringPrefix(Type type, string name = "Prefix")
    {
        var harmony = new Harmony("tests.string-prefix." + Guid.NewGuid().ToString("N"));
        harmony.Patch(ArenaProfileWriteCapturePatches.StringTarget!, prefix: new HarmonyMethod(type.GetMethod(name)!));
        _harmonies.Add(harmony);
        return harmony;
    }

    private static void AssertRefused(string ram, string disk, ProfileProof proof)
    {
        var exception = Assert.Throws<RewardDataException>(() => ArenaRewardReturnListener.RequireSavedProfile(ram, disk, ram, proof));
        Assert.Equal("return-profile-save-not-confirmed", exception.Message);
    }

    // ---- 1, 2

    [Fact]
    public async Task NoTransformationIsExactlyAsStrictAsToday()
    {
        Enable();
        string id = NewId(), ram = Profile(id);
        await Write(id, ram);
        string disk = File.ReadAllText(PathOf(id));

        var proof = ProfileWriteProof.Resolve(id, disk);
        Assert.Equal(ProfileProofMode.Strict, proof.Mode);
        Assert.Equal("none", proof.Pipeline);
        Assert.Equal(disk, proof.Comparable);
        ArenaRewardReturnListener.RequireSavedProfile(ram, disk, ram, proof);

        // A changed RAM, a changed disk and a duplicate key are refused exactly as the strict rule refuses them.
        string changed = ram.Replace("\"pmc\"", "\"pmc2\"");
        AssertRefused(changed, disk, proof);
        AssertRefused(ram, changed, ProfileWriteProof.Resolve(id, changed));
        Assert.Throws<RewardDataException>(() => ArenaRewardReturnListener.RequireSavedProfile(ram, ram, changed, proof));
    }

    [Fact]
    public async Task PapLikeTransformingPrefixConfirmsThroughPipeline()
    {
        Enable();
        PatchPap();
        string id = NewId(), ram = Profile(id);
        await Write(id, ram);
        string disk = File.ReadAllText(PathOf(id));
        Assert.NotEqual(RewardIdentity.Hash(ram), RewardIdentity.Hash(disk));

        var proof = ProfileWriteProof.Resolve(id, disk);
        Assert.Equal(ProfileProofMode.Pipeline, proof.Mode);
        Assert.Equal("transformed", proof.Pipeline);
        Assert.Equal("true", proof.OutputDiskMatch);
        Assert.Equal(ram, proof.Input);
        Assert.Equal(disk, proof.Output);
        // The strict rule still refuses it: that is the reported bug.
        Assert.Throws<RewardDataException>(() => ArenaRewardReturnListener.RequireSavedProfile(ram, disk, ram));
        ArenaRewardReturnListener.RequireSavedProfile(ram, disk, ram, proof);
        // The diagnostics overload with a proof is the one SaveAndVerify uses.
        ArenaRewardReturnListener.VerifySavedProfile(id, ram, disk, () => ram, new SessionSaveDiagnostics(), id);
    }

    // ---- 3

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RegistrationOrderOfPapLikePrefixDoesNotMatter(bool papFirst)
    {
        if (papFirst) { PatchPap(); Enable(); }
        else { Enable(); PatchPap(); }
        string id = NewId(), ram = Profile(id);
        await Write(id, ram);
        string disk = File.ReadAllText(PathOf(id));
        ArenaRewardReturnListener.RequireSavedProfile(ram, disk, ram, ProfileWriteProof.Resolve(id, disk));

        var text = Harmony.GetPatchInfo(ArenaProfileWriteCapturePatches.StringTarget!)!;
        var ours = Assert.Single(text.Prefixes, p => p.owner == ArenaProfileWriteCapturePatches.StringWriteHarmonyId);
        Assert.Equal(Priority.First, ours.priority);
        Assert.All(text.Prefixes.Where(p => p != ours), p => Assert.True(p.priority < ours.priority));
        Assert.Equal(ours.PatchMethod, text.Prefixes.OrderByDescending(p => p.priority).First().PatchMethod);
        var bytes = Harmony.GetPatchInfo(ArenaProfileWriteCapturePatches.ByteTarget!)!;
        Assert.Equal(Priority.Last, Assert.Single(bytes.Prefixes, p => p.owner == ArenaProfileWriteCapturePatches.ByteWriteHarmonyId).priority);
    }

    // ---- 5, 6, 7, 8, 9

    [Fact]
    public async Task StaleMd5PlusExternalWriterIsRejected()
    {
        Enable();
        PatchPap();
        string id = NewId(), ram = Profile(id);
        await Write(id, ram);
        // SaveServer would skip the next save (same hash), and something else rewrites the file meanwhile.
        string external = ProfileNode(id).ToJsonString();
        File.WriteAllText(PathOf(id), external);

        var proof = ProfileWriteProof.Resolve(id, external);
        Assert.Equal(ProfileProofMode.Strict, proof.Mode);
        Assert.Equal("transformed", proof.Pipeline);
        Assert.Equal("false", proof.OutputDiskMatch);
        // The external writer wrote the very RAM text: today's strict rule accepts that, and still does.
        ArenaRewardReturnListener.RequireSavedProfile(ram, external, ram, proof);
        string tampered = external.Replace("\"tester\"", "\"someone\"");
        File.WriteAllText(PathOf(id), tampered);
        AssertRefused(ram, tampered, ProfileWriteProof.Resolve(id, tampered));
    }

    [Fact]
    public async Task FailedWriteThenSkippedSaveIsRejected()
    {
        Enable();
        PatchPap();
        string id = NewId(), ram = Profile(id);
        string old = ProfileNode(id).ToJsonString().Replace("\"tester\"", "\"old\"");
        File.WriteAllText(PathOf(id), old);
        // The temp file of the atomic write cannot be created: the original throws after the record was sealed.
        Directory.CreateDirectory(PathOf(id) + ".bak");
        await Assert.ThrowsAnyAsync<Exception>(() => Write(id, ram));

        string disk = File.ReadAllText(PathOf(id));
        Assert.Equal(old, disk);
        var proof = ProfileWriteProof.Resolve(id, disk);
        Assert.Equal(ProfileProofMode.Strict, proof.Mode);
        Assert.Equal("false", proof.OutputDiskMatch);
        AssertRefused(ram, disk, proof);
    }

    [Fact]
    public async Task RamChangedAfterWriteIsRejected()
    {
        Enable();
        PatchPap();
        string id = NewId(), ram = Profile(id);
        await Write(id, ram);
        string disk = File.ReadAllText(PathOf(id));
        var proof = ProfileWriteProof.Resolve(id, disk);

        string changedRam = ram.Replace("\"tester\"", "\"changed\"");
        AssertRefused(changedRam, disk, proof);
        // Stable at the first read, changed at the second.
        Assert.Throws<RewardDataException>(() => ArenaRewardReturnListener.RequireSavedProfile(ram, disk, changedRam, proof));
    }

    [Fact]
    public async Task ValueChangingTransformIsNotAccepted()
    {
        Enable();
        PatchPap(json =>
        {
            var root = JsonNode.Parse(PapLike.Strip(json))!;
            root["characters"]!["pmc"]!["Quests"]![0]!["status"] = 4; // a changed value, not a removal
            return root.ToJsonString();
        });
        string id = NewId(), ram = Profile(id);
        await Write(id, ram);
        string disk = File.ReadAllText(PathOf(id));

        var proof = ProfileWriteProof.Resolve(id, disk);
        Assert.Equal(ProfileProofMode.Strict, proof.Mode);
        Assert.Equal("unsafe", proof.Pipeline);
        AssertRefused(ram, disk, proof);
    }

    [Theory]
    [InlineData("ledger")]
    [InlineData("info")]
    [InlineData("pmcInfo")]
    [InlineData("scavInfo")]
    public async Task DroppingProtectedSubtreeIsUnsafe(string dropped)
    {
        Enable();
        PatchPap(json =>
        {
            var root = JsonNode.Parse(PapLike.Strip(json))!.AsObject();
            switch (dropped)
            {
                case "ledger": root.Remove(Ledger); break;
                case "info": root.Remove("info"); break;
                case "pmcInfo": root["characters"]!["pmc"]!.AsObject().Remove("Info"); break;
                default: root["characters"]!["scav"]!.AsObject().Remove("Info"); break;
            }
            return root.ToJsonString();
        });
        string id = NewId(), ram = Profile(id);
        await Write(id, ram);
        string disk = File.ReadAllText(PathOf(id));

        var proof = ProfileWriteProof.Resolve(id, disk);
        Assert.Equal("unsafe", proof.Pipeline);
        AssertRefused(ram, disk, proof);
    }

    // ---- 10, 11, 12, 13

    [Fact]
    public async Task DirectByteWriterInvalidatesRecord()
    {
        Enable();
        PatchPap();
        string id = NewId(), ram = Profile(id);
        await Write(id, ram);
        Assert.Equal("transformed", ProfileWriteProof.Resolve(id, File.ReadAllText(PathOf(id))).Pipeline);

        byte[] direct = Encoding.UTF8.GetBytes(ProfileNode(id).ToJsonString());
        await Files.WriteFileAsync(PathOf(id), direct);
        string disk = File.ReadAllText(PathOf(id));
        var proof = ProfileWriteProof.Resolve(id, disk);
        Assert.Equal("opaque", proof.Pipeline);
        Assert.Equal(ProfileProofMode.Strict, proof.Mode);
        AssertRefused(ram.Replace("tester", "other"), disk, proof);
    }

    [Fact]
    public async Task SkippedStringWriteTurnsRecordOpaqueAndDoesNotLeakSlot()
    {
        Enable();
        string id = NewId(), ram = Profile(id);
        await Write(id, ram); // a good record first
        Assert.Equal("none", ProfileWriteProof.Resolve(id, File.ReadAllText(PathOf(id))).Pipeline);

        var skipper = PatchStringPrefix(typeof(SkipStringWrite));
        await Write(id, ram);
        Assert.Equal("opaque", ProfileWriteProof.Resolve(id, File.ReadAllText(PathOf(id))).Pipeline);
        skipper.UnpatchSelf();

        // A leaked slot would poison the next write of any profile.
        string other = NewId(), otherRam = Profile(other);
        await Write(other, otherRam);
        Assert.Equal("none", ProfileWriteProof.Resolve(other, File.ReadAllText(PathOf(other))).Pipeline);
        await Write(id, ram);
        Assert.Equal("none", ProfileWriteProof.Resolve(id, File.ReadAllText(PathOf(id))).Pipeline);
    }

    [Fact]
    public async Task ThrowingPrefixDoesNotLeakSlot()
    {
        Enable();
        var thrower = PatchStringPrefix(typeof(ThrowingStringWrite));
        string id = NewId(), ram = Profile(id);
        await Assert.ThrowsAnyAsync<Exception>(() => Write(id, ram));
        thrower.UnpatchSelf();

        // Begin ran and the original never did: the finalizer must have cleared the slot.
        Assert.Equal("opaque", ProfileWriteProof.Resolve(id, ram).Pipeline);
        string other = NewId(), otherRam = Profile(other);
        await Write(other, otherRam);
        Assert.Equal("none", ProfileWriteProof.Resolve(other, File.ReadAllText(PathOf(other))).Pipeline);
    }

    [Fact]
    public void NestedProfileWriteIsPoisoned()
    {
        ProfileWriteProof.MarkReady(true);
        string id = NewId(), path = PathOf(id), text = Profile(id);
        byte[] bytes = Encoding.UTF8.GetBytes(text);

        ProfileWriteProof.Begin(path, text);
        ProfileWriteProof.Begin(path, text); // a profile write inside a profile write
        ProfileWriteProof.Seal(path, bytes);
        ProfileWriteProof.End();
        ProfileWriteProof.End();
        Assert.Equal("opaque", ProfileWriteProof.Resolve(id, text).Pipeline);

        // Depth reached zero: the slot is clean again.
        ProfileWriteProof.Begin(path, text);
        ProfileWriteProof.Seal(path, bytes);
        ProfileWriteProof.End();
        Assert.Equal("none", ProfileWriteProof.Resolve(id, text).Pipeline);
    }

    // ---- 14, 15

    [Fact]
    public async Task HookNotReadyFailsClosed()
    {
        Enable();
        PatchPap();
        string id = NewId(), ram = Profile(id);
        await Write(id, ram);
        string disk = File.ReadAllText(PathOf(id));
        Assert.Equal("transformed", ProfileWriteProof.Resolve(id, disk).Pipeline);

        ProfileWriteProof.MarkReady(false);
        var proof = ProfileWriteProof.Resolve(id, disk);
        Assert.Equal("unavailable", proof.Pipeline);
        Assert.Equal(ProfileProofMode.Strict, proof.Mode);
        AssertRefused(ram, disk, proof);
        Assert.False(ArenaProfileWriteCapturePatches.TryEnable(null, null));
        Assert.False(ProfileWriteProof.Ready);
    }

    [Fact]
    public void CaptureNeverBreaksWritePath()
    {
        ProfileWriteProof.MarkReady(true);
        string id = NewId(), path = PathOf(id);

        // Invalid paths and null arguments are ignored, never thrown.
        ProfileWriteProof.Begin(null!, "{}");
        ProfileWriteProof.Seal(null!, null!);
        ProfileWriteProof.Begin("\0:?*", "{}");
        ProfileWriteProof.Seal("\0:?*", []);
        ProfileWriteProof.Begin(path, null!);
        ProfileWriteProof.Seal(path, []);
        ProfileWriteProof.End();
        ProfileWriteProof.End();
        ProfileWriteProof.End(); // more Ends than Begins
        Assert.Equal("opaque", ProfileWriteProof.Resolve(id, "{}").Pipeline);

        // A byte[] over the profile limit is not retained.
        ProfileWriteProof.Begin(path, "{}");
        ProfileWriteProof.Seal(path, new byte[ProfileWriteProof.MaxProfileBytes + 1]);
        ProfileWriteProof.End();
        Assert.Equal("opaque", ProfileWriteProof.Resolve(id, "{}").Pipeline);
        Assert.Equal(0, ProfileWriteProof.RetainedBytesForTests);

        // Paths of other directories and other names are not profiles.
        Assert.False(ProfileWriteProof.IsProfilePath(Path.Combine(_dir, "sub", id + ".json"), out _));
        Assert.False(ProfileWriteProof.IsProfilePath(Path.Combine(_dir, id + ".json.bak"), out _));
        Assert.False(ProfileWriteProof.IsProfilePath(Path.Combine(_dir, "notahexprofileidxxxxxxx.json"), out _));
        Assert.True(ProfileWriteProof.IsProfilePath(Path.Combine(_dir, id.ToUpperInvariant() + ".json"), out string lowered));
        Assert.Equal(id, lowered);
    }

    // ---- 16

    [Fact]
    public void PatchesHaveExpectedPrioritiesAndAreIndependentOfFileMutationPatchesReady()
    {
        Enable();
        Assert.True(ProfileWriteProof.Ready);
        var text = Harmony.GetPatchInfo(ArenaProfileWriteCapturePatches.StringTarget!)!;
        Assert.Equal(Priority.First, Assert.Single(text.Prefixes, p => p.owner == "ArenaProfileWriteCaptureStringWriteInput").priority);
        Assert.Equal(Priority.Last, Assert.Single(text.Finalizers, p => p.owner == "ArenaProfileWriteCaptureStringWriteInput").priority);
        var bytes = Harmony.GetPatchInfo(ArenaProfileWriteCapturePatches.ByteTarget!)!;
        Assert.Equal(Priority.Last, Assert.Single(bytes.Prefixes, p => p.owner == "ArenaProfileWriteCaptureByteWriteOutput").priority);

        // A failed enable leaves nothing behind and the proof not ready.
        ArenaProfileWriteCapturePatches.Disable();
        Assert.False(ArenaProfileWriteCapturePatches.TryEnable(ArenaProfileWriteCapturePatches.StringTarget, null));
        Assert.False(ProfileWriteProof.Ready);
        Assert.DoesNotContain(Harmony.GetPatchInfo(ArenaProfileWriteCapturePatches.StringTarget!)?.Prefixes ?? [],
            p => p.owner == "ArenaProfileWriteCaptureStringWriteInput");

        // PatchManager enables it in its own try/catch, outside the PitFireTeam rollback group.
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root, "ArenaMode.Server", "PatchManager.cs"));
        int enable = source.IndexOf("ArenaProfileWriteCapturePatches.TryEnable()", StringComparison.Ordinal);
        int groupStart = source.IndexOf("AbstractPatch[] pitFireTeamFilePatches", StringComparison.Ordinal);
        int groupEnd = source.IndexOf("PitFireTeamDynamicPersistencePatches.TryEnable(", StringComparison.Ordinal);
        Assert.True(enable >= 0 && groupStart >= 0 && groupEnd > groupStart);
        Assert.True(enable < groupStart || enable > groupEnd, "the capture must not be part of the PitFireTeam rollback group");
        Assert.DoesNotContain("FileMutationPatchesReady", source.Substring(Math.Max(0, enable - 200), 400));
    }

    // ---- 17

    [Fact]
    public void ForeignSameProfilePostfixDoesNotAffectConfirms()
    {
        var harmony = new Harmony("tests.same-profile-postfix." + Guid.NewGuid().ToString("N"));
        _harmonies.Add(harmony);
        var same = typeof(RewardRuntimeFiles).GetMethod(nameof(RewardRuntimeFiles.SameProfile),
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        harmony.Patch(same, postfix: new HarmonyMethod(typeof(ForcedTrue).GetMethod(nameof(ForcedTrue.Postfix))!));
        try
        {
            // Self-validating: the foreign patch really forces the patchable wrapper.
            Assert.True(CallSameProfile("{}", "{\"a\":1}"));
            var strict = ProfileProof.Strict("{\"a\":1}", "none");
            Assert.False(ProfileWriteProof.Confirms("{}", "{\"a\":1}", strict));
            Assert.Throws<RewardDataException>(() => ArenaRewardReturnListener.RequireSavedProfile("{}", "{\"a\":1}", "{}", strict));
            Assert.Throws<RewardDataException>(() => ArenaRewardReturnListener.RequireSavedProfile("{}", "{\"a\":1}", "{}"));
        }
        finally { harmony.UnpatchSelf(); }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool CallSameProfile(string a, string b) => RewardRuntimeFiles.SameProfile(a, b);

    // ---- 18, 19

    [Fact]
    public async Task RetainedBytesBudgetEvictsToStrict()
    {
        Enable();
        PatchPap();
        string a = NewId(), b = NewId();
        await Write(a, Profile(a));
        long cost = ProfileWriteProof.RetainedBytesForTests;
        Assert.True(cost > 0);
        ProfileWriteProof.MaxRetainedBytes = cost + 16;

        await Write(a, Profile(a)); // replaces its own record: the old cost is released first
        Assert.Equal(cost, ProfileWriteProof.RetainedBytesForTests);
        Assert.Equal("transformed", ProfileWriteProof.Resolve(a, File.ReadAllText(PathOf(a))).Pipeline);

        await Write(b, Profile(b)); // over the budget
        string disk = File.ReadAllText(PathOf(b));
        var proof = ProfileWriteProof.Resolve(b, disk);
        Assert.Equal("opaque", proof.Pipeline);
        Assert.Equal(ProfileProofMode.Strict, proof.Mode);
        Assert.Equal(cost, ProfileWriteProof.RetainedBytesForTests);
        AssertRefused(Profile(b), disk, proof);

        // An unchanged write releases the cost of the transformed record it replaces.
        PapLike.Transform = json => json;
        await Write(a, Profile(a));
        Assert.Equal(0, ProfileWriteProof.RetainedBytesForTests);
    }

    [Fact]
    public async Task EqualWriteRetainsNothing()
    {
        Enable();
        string id = NewId();
        await Write(id, Profile(id));
        Assert.Equal(0, ProfileWriteProof.RetainedBytesForTests);
        Assert.False(ProfileWriteProof.HasTransformedRecordForTests(id));
        Assert.Equal("none", ProfileWriteProof.Resolve(id, File.ReadAllText(PathOf(id))).Pipeline);

        // Non-ASCII, a surrogate pair and a lone surrogate (encoded as U+FFFD, like Encoding.UTF8) are equal too.
        string odd = Profile(id).Replace("\"tester\"", "\"täster 😀 中\"");
        await Write(id, odd);
        Assert.Equal("none", ProfileWriteProof.Resolve(id, File.ReadAllText(PathOf(id))).Pipeline);
        Assert.True(ProfileWriteProof.Utf8Equals("a\ud800b", Encoding.UTF8.GetBytes("a\ud800b")));
        Assert.True(ProfileWriteProof.Utf8Equals(new string('\ud83d', 1) + new string('\ude00', 1), Encoding.UTF8.GetBytes("😀")));
        Assert.False(ProfileWriteProof.Utf8Equals("abc", Encoding.UTF8.GetBytes("abd")));
        Assert.False(ProfileWriteProof.Utf8Equals("abc", Encoding.UTF8.GetBytes("abcd")));
        // A text longer than one chunk with a surrogate pair at the boundary.
        string long8k = new string('x', 8191) + "😀" + new string('y', 9000);
        Assert.True(ProfileWriteProof.Utf8Equals(long8k, Encoding.UTF8.GetBytes(long8k)));
    }

    // ---- 20

    [Fact]
    public void SaveAndVerifyUsesResolve()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root, "ArenaMode.Server", "Routers", "ArenaRewardReturnListener.cs"));
        int saveAndVerify = source.IndexOf("private async Task SaveAndVerify(", StringComparison.Ordinal);
        int strict = source.IndexOf("internal static void RequireSavedProfile(", StringComparison.Ordinal);
        int verify = source.IndexOf("internal static void VerifySavedProfile(", StringComparison.Ordinal);
        Assert.True(saveAndVerify >= 0 && strict > saveAndVerify && verify > strict);
        string body = source[saveAndVerify..strict];
        Assert.Contains("VerifySavedProfile(", body);
        Assert.DoesNotContain("RequireSavedProfile(", body);
        string verifyBody = source[verify..];
        verifyBody = verifyBody[..verifyBody.IndexOf("internal static async Task SaveWithoutAborting", StringComparison.Ordinal)];
        Assert.Contains("ProfileWriteProof.Resolve(id, actualDisk)", verifyBody);
        Assert.Contains("RequireSavedProfile(expected, actualDisk, readAfter(), diagnostics, id, selectedProfileId, proof)", verifyBody);
    }

    // ---- limits and isolation

    private void Capture(string id, string input, string output)
    {
        string path = PathOf(id);
        ProfileWriteProof.Begin(path, input);
        ProfileWriteProof.Seal(path, Encoding.UTF8.GetBytes(output));
        ProfileWriteProof.End();
    }

    [Fact]
    public async Task RetainedCostCountsTheCachedDecodedOutput()
    {
        Enable();
        PatchPap();
        string id = NewId(), ram = Profile(id);
        await Write(id, ram);
        string disk = File.ReadAllText(PathOf(id));
        long output = Encoding.UTF8.GetByteCount(disk);
        // Input text (2 bytes per char), output bytes, and the decoded output text Resolve caches (2 bytes per char at most).
        Assert.Equal(2L * ram.Length + 3L * output, ProfileWriteProof.RetainedBytesForTests);
        Assert.Equal("transformed", ProfileWriteProof.Resolve(id, disk).Pipeline);
        Assert.True(2L * disk.Length <= 2L * output);
    }

    [Fact]
    public async Task ExhaustedVisitBudgetFailsClosed()
    {
        Enable();
        PatchPap();
        ProfileWriteProof.VisitBudget = 3;
        string id = NewId(), ram = Profile(id);
        await Write(id, ram);
        string disk = File.ReadAllText(PathOf(id));
        var proof = ProfileWriteProof.Resolve(id, disk);
        Assert.Equal(ProfileProofMode.Strict, proof.Mode);
        Assert.Equal("unsafe", proof.Pipeline);
        AssertRefused(ram, disk, proof);

        // The same kind of write inside the default budget is accepted: the budget is what refused it.
        ProfileWriteProof.VisitBudget = 20_000_000;
        string other = NewId(), otherRam = Profile(other);
        await Write(other, otherRam);
        Assert.Equal(ProfileProofMode.Pipeline, ProfileWriteProof.Resolve(other, File.ReadAllText(PathOf(other))).Mode);
    }

    [Fact]
    public void RecordTableStopsAtTheIdCap()
    {
        ProfileWriteProof.MarkReady(true);
        string Id(int n) => n.ToString("x24");
        for (int n = 1; n <= 1024; n++) Capture(Id(n), "{\"a\":1,\"b\":2}", "{\"a\":1}");
        Assert.True(ProfileWriteProof.HasTransformedRecordForTests(Id(1024)));

        // The 1025th id is not recorded, so it resolves as opaque (strict), never as a stale neighbour.
        Capture(Id(1025), "{\"a\":1,\"b\":2}", "{\"a\":1}");
        Assert.False(ProfileWriteProof.HasTransformedRecordForTests(Id(1025)));
        var proof = ProfileWriteProof.Resolve(Id(1025), "{\"a\":1}");
        Assert.Equal(ProfileProofMode.Strict, proof.Mode);
        Assert.Equal("opaque", proof.Pipeline);
        // An id that already has a record is still updated at the cap.
        Capture(Id(7), "{\"a\":1,\"b\":2,\"c\":3}", "{\"a\":1,\"b\":2}");
        Assert.True(ProfileWriteProof.HasTransformedRecordForTests(Id(7)));
        long small = 2L * "{\"a\":1,\"b\":2}".Length + 3L * "{\"a\":1}".Length;
        long updated = 2L * "{\"a\":1,\"b\":2,\"c\":3}".Length + 3L * "{\"a\":1,\"b\":2}".Length;
        Assert.Equal(1023L * small + updated, ProfileWriteProof.RetainedBytesForTests);
    }

    [Fact]
    public void WritesOnTwoThreadsKeepTheirOwnInputAndOutput()
    {
        ProfileWriteProof.MarkReady(true);
        string a = NewId(), b = NewId();
        string inputA = Profile(a), inputB = Profile(b).Replace("\"tester\"", "\"other\"");
        string outputA = PapLike.Strip(inputA), outputB = PapLike.Strip(inputB);
        using var barrier = new Barrier(2);
        void Run(string id, string input, string output)
        {
            string path = PathOf(id);
            ProfileWriteProof.Begin(path, input);
            barrier.SignalAndWait(); // both threads have begun before either seals
            ProfileWriteProof.Seal(path, Encoding.UTF8.GetBytes(output));
            barrier.SignalAndWait();
            ProfileWriteProof.End();
        }
        var first = Task.Factory.StartNew(() => Run(a, inputA, outputA), TaskCreationOptions.LongRunning);
        var second = Task.Factory.StartNew(() => Run(b, inputB, outputB), TaskCreationOptions.LongRunning);
        Task.WaitAll(first, second);

        var proofA = ProfileWriteProof.Resolve(a, outputA);
        var proofB = ProfileWriteProof.Resolve(b, outputB);
        Assert.Equal(ProfileProofMode.Pipeline, proofA.Mode);
        Assert.Equal(inputA, proofA.Comparable);
        Assert.Equal(ProfileProofMode.Pipeline, proofB.Mode);
        Assert.Equal(inputB, proofB.Comparable);
    }

    [Fact]
    public async Task UnparsableDiskTextLeavesTheOutputDiskMatchUnknown()
    {
        Enable();
        PatchPap();
        string id = NewId();
        await Write(id, Profile(id));
        var proof = ProfileWriteProof.Resolve(id, "{\"a\":1,\"a\":2}"); // a duplicate key cannot be compared
        Assert.Equal(ProfileProofMode.Strict, proof.Mode);
        Assert.Equal("transformed", proof.Pipeline);
        Assert.Equal("unknown", proof.OutputDiskMatch);
    }

    // ---- test patches

    internal static class PapLike
    {
        internal static Func<string, string> Transform = Strip;
        internal static void Reset() => Transform = Strip;

        // What Pack a Punch does: take its own data out of the text SPT is about to write.
        internal static string Strip(string json)
        {
            var root = JsonNode.Parse(json)!;
            var pmc = root["characters"]!["pmc"]!;
            var areas = pmc["Hideout"]!["Areas"]!.AsArray();
            for (int i = areas.Count - 1; i >= 0; i--)
                if (areas[i]!["type"]!.GetValue<int>() == 115) areas.RemoveAt(i);
            var quests = pmc["Quests"]!.AsArray();
            for (int i = quests.Count - 1; i >= 0; i--)
                if (quests[i]!["qid"]!.GetValue<string>() == "quest-115") quests.RemoveAt(i);
            return root.ToJsonString();
        }

        public static void Prefix(string __0, ref string __1)
        {
            if (ProfileWriteProof.IsProfilePath(__0, out _)) __1 = Transform(__1);
        }
    }

    internal static class SkipStringWrite
    {
        public static bool Prefix(ref Task __result)
        {
            __result = Task.CompletedTask;
            return false;
        }
    }

    internal static class ThrowingStringWrite
    {
        public static void Prefix() => throw new InvalidOperationException("synthetic prefix failure");
    }

    internal static class ForcedTrue
    {
        public static void Postfix(ref bool __result) => __result = true;
    }
}

// The real SaveServer over a synthetic profile directory, with a PaP-like prefix in SPT's write path.
[Collection(RewardNativeLoadCollection.Name)]
public sealed class ProfileWriteProofNativeTests
{
    private static string Serialize(SPTarkov.Server.Core.Models.Eft.Profile.SptProfile profile)
        => new JsonUtil([new SPTarkov.Server.Core.Utils.Json.SptJsonConverterRegistrator()]).Serialize(profile, true)!;

    // Removes one array element of an extension field: a subtractive edit of data SPT does not model.
    private static string StripForeign(string json)
    {
        var root = JsonNode.Parse(json)!;
        root["foreignFixture"]!["untouched"]!.AsArray().RemoveAt(1);
        return root.ToJsonString();
    }

    private static async Task Run(Func<SPTarkov.Server.Core.Servers.SaveServer, MongoId, string, string, Task> body)
    {
        using var fixture = new RewardNativeLoadTests.NativeFixture(fresh: true);
        var previous = Environment.CurrentDirectory;
        var harmony = new Harmony("tests.native-pap-like." + Guid.NewGuid().ToString("N"));
        ProfileWriteProof.ResetForTests();
        try
        {
            Directory.SetCurrentDirectory(fixture.Root);
            var (saves, _) = fixture.CreateNativeServer();
            Assert.True(ArenaProfileWriteCapturePatches.TryEnable());
            ProfileWriteProofTests.PapLike.Transform = StripForeign;
            harmony.Patch(ArenaProfileWriteCapturePatches.StringTarget!,
                prefix: new HarmonyMethod(typeof(ProfileWriteProofTests.PapLike).GetMethod(nameof(ProfileWriteProofTests.PapLike.Prefix))!));
            var id = new MongoId(fixture.Binding.ProfileId);
            await saves.LoadProfileAsync(id);
            await body(saves, id, fixture.Binding.ProfileId, fixture.ProfilePath);
        }
        finally
        {
            harmony.UnpatchSelf();
            ArenaProfileWriteCapturePatches.Disable();
            ProfileWriteProofTests.PapLike.Reset();
            ProfileWriteProof.ResetForTests();
            Directory.SetCurrentDirectory(previous);
        }
    }

    // 4
    [Fact]
    public Task Md5SkippedSaveConfirmsThroughLatestRecord() => Run(async (saves, id, text, path) =>
    {
        await saves.SaveProfileAsync(id);
        string disk = File.ReadAllText(path);
        Assert.DoesNotContain("two", disk); // the PaP-like prefix did strip
        var past = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, past);

        await saves.SaveProfileAsync(id); // unchanged RAM: SaveServer skips the write by its cached hash
        Assert.Equal(past, File.GetLastWriteTimeUtc(path));
        string expected = Serialize(saves.GetProfile(id));
        ArenaRewardReturnListener.VerifySavedProfile(text, expected, File.ReadAllText(path),
            () => Serialize(saves.GetProfile(id)), new SessionSaveDiagnostics(), text);
    });

    // 21
    [Fact]
    public Task RealSaveServerChainConfirmsPapLikeProfile() => Run(async (saves, id, text, path) =>
    {
        await saves.SaveProfileAsync(id);
        string disk = File.ReadAllText(path);
        string expected = Serialize(saves.GetProfile(id));
        Assert.Throws<RewardDataException>(() => ArenaRewardReturnListener.RequireSavedProfile(expected, disk, expected));

        var diagnostics = new SessionSaveDiagnostics();
        ArenaRewardReturnListener.VerifySavedProfile(text, expected, disk, () => Serialize(saves.GetProfile(id)), diagnostics, text);

        // Without the hook the same chain is refused, with the new diagnostic words.
        ProfileWriteProof.MarkReady(false);
        var refused = Assert.Throws<RewardDataException>(() => ArenaRewardReturnListener.VerifySavedProfile(
            text, expected, disk, () => Serialize(saves.GetProfile(id)), diagnostics, text));
        Assert.Equal("return-profile-save-not-confirmed", refused.Message);
        var lines = new List<string>();
        diagnostics.WarnRefusal(true, new RewardReturnReply(false, "refused", false, true, "preflight-save", "return-profile-save-not-confirmed"),
            true, lines.Add);
        Assert.Contains("pipeline=unavailable;", Assert.Single(lines));
    });
}
