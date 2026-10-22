using System.Text;
using ArenaMode.Shared.Session;

namespace ArenaMode.Tests;

/// <summary>
/// The one reader of the two policy files (docs/ARENA-SESSION-20261021.md sections 3, 5 and 7): the client and the
/// gate read the shipped default list and the user's file through <see cref="ArenaPolicyFiles"/>, so they apply the same
/// size limit and the same decoding to the same bytes and end up with the same policy hash. A limit that only one side
/// enforced would make the Arena session refuse on the policy hash for ever.
/// </summary>
public sealed class ArenaPolicyFilesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "arena-policy-files-tests", Guid.NewGuid().ToString("N"));

    public ArenaPolicyFilesTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string Write(string name, byte[] content)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>A valid user policy file that is padded with white space to the given size in bytes.</summary>
    private static byte[] UserPolicyOfSize(int bytes)
    {
        const string json = """{ "schema": 1, "deny": [ "me.sol.sain" ] }""";
        return Encoding.UTF8.GetBytes(json + new string(' ', bytes - json.Length));
    }

    [Fact]
    public void AMissingFileIsAbsentAndNotAnError()
    {
        var state = ArenaPolicyFiles.TryRead(Path.Combine(_folder, "nothing.json"), out string? text, out string? detail);

        Assert.Equal(ArenaPolicyFileState.Absent, state);
        Assert.Null(text);
        Assert.Null(detail);
        Assert.Equal("the file does not exist", ArenaPolicyFiles.Describe(state, detail!));
    }

    [Fact]
    public void AFolderWhereTheFileBelongsIsAbsentToo()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "dir.json"));

        Assert.Equal(ArenaPolicyFileState.Absent, ArenaPolicyFiles.TryRead(Path.Combine(_folder, "dir.json"), out string? text, out _));
        Assert.Null(text);
    }

    [Fact]
    public void AFileUpToTheLimitIsReadInFull()
    {
        string path = Write("exact.json", UserPolicyOfSize(ArenaPolicyFiles.MaxBytes));

        var state = ArenaPolicyFiles.TryRead(path, out string? text, out string? detail);

        Assert.Equal(ArenaPolicyFileState.Read, state);
        Assert.Null(detail);
        Assert.Equal(ArenaPolicyFiles.MaxBytes, text!.Length);
        Assert.StartsWith("{ \"schema\": 1", text);
    }

    [Fact]
    public void AFileOneByteOverTheLimitIsNotAPolicyFileAndNoneOfItIsUsed()
    {
        string path = Write("over.json", UserPolicyOfSize(ArenaPolicyFiles.MaxBytes + 1));

        var state = ArenaPolicyFiles.TryRead(path, out string? text, out string? detail);

        Assert.Equal(ArenaPolicyFileState.TooLarge, state);
        Assert.Null(text);
        Assert.Equal("larger than 256 KB", detail);
        Assert.Equal("larger than 256 KB", ArenaPolicyFiles.Describe(state, detail));
        Assert.Equal(256 * 1024, ArenaPolicyFiles.MaxBytes);
    }

    [Fact]
    public void ABomIsHonouredAndTheTextIsUtf8()
    {
        byte[] bom = { 0xEF, 0xBB, 0xBF };
        const string content = "{ \"name\": \"Gr\u00fc\u00dfe\" }";
        string path = Write("bom.json", bom.Concat(Encoding.UTF8.GetBytes(content)).ToArray());

        Assert.Equal(ArenaPolicyFileState.Read, ArenaPolicyFiles.TryRead(path, out string? text, out _));
        Assert.Equal(content, text);
    }

    [SkippableFact]
    public void AFileAnEditorHoldsOpenForWritingIsUnreadableAndNotReadHalfWritten()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "the sharing mode of File.ReadAllText is enforced by Windows");
        string path = Write("locked.json", UserPolicyOfSize(100));

        ArenaPolicyFileState state;
        string? text;
        string? detail;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            state = ArenaPolicyFiles.TryRead(path, out text, out detail);
        }

        Assert.Equal(ArenaPolicyFileState.Unreadable, state);
        Assert.Null(text);
        Assert.False(string.IsNullOrWhiteSpace(detail));
        Assert.Equal(detail, ArenaPolicyFiles.Describe(state, detail!));
    }

    [Theory]
    [InlineData("ArenaMode.Client/Session/ArenaWhitelistAdmission.cs")]
    [InlineData("ArenaMode.Gate/ArenaGateRuntime.cs")]
    public void TheClientAndTheGateReadThePolicyFilesOnlyThroughTheSharedReader(string relativePath)
    {
        // A private reader on one side (its own size limit or none, its own decoding) is how the two sides come to apply
        // different policies to the same files, and the Arena session then refuses on the policy hash for ever.
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root, relativePath));

        Assert.Contains("ArenaPolicyFiles.TryRead(", source);
        Assert.DoesNotContain("File.ReadAllText(", source);
        Assert.DoesNotContain("File.ReadAllBytes(", source);
        Assert.DoesNotContain("new StreamReader(", source);
    }

    [Fact]
    public void ThePolicyBuiltFromAFileOverTheLimitIsTheOneBuiltWithoutTheFile()
    {
        // What the client and the gate both do with the user's file: an oversized one is ignored as a whole, not applied.
        // The deny in it must not show in the policy, and the hash is the one of the default list alone, which is what
        // the request, the server tree and the gate's own report agree on.
        const string defaultJson = """{ "schema": 1, "mods": [ { "guid": "me.sol.sain", "name": "SAIN", "kind": "mod" } ] }""";
        string path = Write("user.json", UserPolicyOfSize(ArenaPolicyFiles.MaxBytes + 1));
        Assert.Equal(ArenaPolicyFileState.TooLarge, ArenaPolicyFiles.TryRead(path, out string? userJson, out _));

        Assert.True(ArenaWhitelistPolicy.TryLoad(defaultJson, userJson!, out var ignored, out _));
        Assert.True(ArenaWhitelistPolicy.TryLoad(defaultJson, null!, out var withoutFile, out _));
        Assert.Equal(withoutFile!.Hash, ignored!.Hash);
        Assert.True(ignored.IsAllowedPlugin("me.sol.sain"));

        // The same file within the limit is applied and is another policy.
        string within = Write("within.json", UserPolicyOfSize(ArenaPolicyFiles.MaxBytes));
        Assert.Equal(ArenaPolicyFileState.Read, ArenaPolicyFiles.TryRead(within, out string? applied, out _));
        Assert.True(ArenaWhitelistPolicy.TryLoad(defaultJson, applied!, out var denied, out _));
        Assert.NotEqual(withoutFile.Hash, denied!.Hash);
        Assert.False(denied.IsAllowedPlugin("me.sol.sain"));
    }
}
