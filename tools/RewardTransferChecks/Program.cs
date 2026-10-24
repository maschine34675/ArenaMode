using ArenaMode.SessionHelper;
using ArenaMode.Shared.Session;
using System.Text;

static class Program
{
    private static int assertions;
    private static readonly List<string> failures = new();

    static int Main()
    {
        Check("off-keeps-existing-helper-path", f =>
        {
            File.Delete(f.Handoff);
            Equal(false, HelperRewardTransfer.CopyHandoff(f.Normal, Fixture.Profile, f.Tree));
            Equal(false, HelperRewardTransfer.CopyHandoff(f.Normal, Fixture.Profile.ToUpperInvariant(), f.Tree));
            Equal(false, File.Exists(f.Origin));
            HelperRewardTransfer.RequireDrainedBeforeRebuild(f.Session);
        });
        Check("disabled-does-not-delegate", f =>
        {
            f.WriteHandoff(false);
            Equal(false, HelperRewardTransfer.CopyHandoff(f.Normal, Fixture.Profile, f.Tree));
            Equal(false, File.Exists(f.Origin));
        });
        Check("bound-handoff-is-exact-and-only-metadata", f =>
        {
            Equal(true, f.Delegate());
            Equal(File.ReadAllText(f.Handoff), File.ReadAllText(f.Origin));
            Equal(false, Directory.Exists(Path.Combine(f.Tree, "user", "arena-rewards")));
        });
        foreach (var kind in new[] { "schema", "binding", "key", "extra", "duplicate", "wrong-root" })
            Check("invalid-handoff-" + kind, f =>
            {
                string json = File.ReadAllText(f.Handoff);
                json = kind switch
                {
                    "schema" => json.Replace("\"schema\": 1", "\"schema\": 2"),
                    "binding" => json.Replace(Fixture.Profile, "bbbbbbbbbbbbbbbbbbbbbbbb"),
                    "key" => json.Replace(Convert.ToBase64String(new byte[32]), "bad-key"),
                    "extra" => json.TrimEnd().TrimEnd('}') + ",\"unknown\":true}",
                    "duplicate" => json.Replace("\"schema\": 1", "\"schema\": 1, \"schema\":1"),
                    _ => json.Replace("normal", "different-origin")
                };
                File.WriteAllText(f.Handoff, json);
                Refuses(() => f.Delegate());
                Equal(false, File.Exists(f.Origin));
            });
        Check("return-never-copies-profile-progress", f =>
        {
            f.Delegate();
            string receipt = f.AddReceipt("{\"opaque\":\"signed bytes\"}");
            string normalProfile = Path.Combine(f.Normal, "user", "profiles", Fixture.Profile + ".json");
            string privateProfile = Path.Combine(f.Tree, "user", "profiles", Fixture.Profile + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(normalProfile));
            Directory.CreateDirectory(Path.GetDirectoryName(privateProfile));
            File.WriteAllText(normalProfile, "normal progress after private entry");
            File.WriteAllText(privateProfile, "private profile must never come back");
            Equal(1, HelperRewardTransfer.ReturnPending(f.Session, f.Normal));
            Equal("{\"opaque\":\"signed bytes\"}", File.ReadAllText(f.Destination(receipt)));
            Equal(false, File.Exists(receipt));
            Equal("normal progress after private entry", File.ReadAllText(normalProfile));
            Equal("private profile must never come back", File.ReadAllText(privateProfile));
            Equal(0, HelperRewardTransfer.ReturnPending(f.Session, f.Normal));
            HelperRewardTransfer.RequireDrainedBeforeRebuild(f.Session);
        });
        Check("crash-after-publish-retries-identically", f =>
        {
            f.Delegate(); string receipt = f.AddReceipt("original receipt");
            Refuses(() => HelperRewardTransfer.ReturnPending(f.Session, f.Normal, afterPublish: _ => throw new IOException("simulated crash")));
            Equal(true, File.Exists(receipt));
            Equal("original receipt", File.ReadAllText(f.Destination(receipt)));
            Equal(1, HelperRewardTransfer.ReturnPending(f.Session, f.Normal));
            Equal(false, File.Exists(receipt));
        });
        Check("collision-preserves-both-copies", f =>
        {
            f.Delegate(); string receipt = f.AddReceipt("original receipt");
            Directory.CreateDirectory(f.Inbox); File.WriteAllText(f.Destination(receipt), "different receipt");
            Refuses(() => HelperRewardTransfer.ReturnPending(f.Session, f.Normal));
            Equal("original receipt", File.ReadAllText(receipt));
            Equal("different receipt", File.ReadAllText(f.Destination(receipt)));
            Refuses(() => HelperRewardTransfer.RequireDrainedBeforeRebuild(f.Session));
        });
        Check("late-source-change-is-not-erased", f =>
        {
            f.Delegate(); string receipt = f.AddReceipt("initial");
            Refuses(() => HelperRewardTransfer.ReturnPending(f.Session, f.Normal, afterPublish: _ => File.WriteAllText(receipt, "changed")));
            Equal("changed", File.ReadAllText(receipt));
            Equal("initial", File.ReadAllText(f.Destination(receipt)));
        });
        Check("disable-keeps-earned-transfer-but-no-new-delegation", f =>
        {
            f.Delegate(); f.AddReceipt("earned before disable"); f.WriteHandoff(false);
            Equal(1, HelperRewardTransfer.ReturnPending(f.Session, f.Normal));
            File.Delete(f.Origin); Equal(false, f.Delegate());
        });
        foreach (string field in new[] { "installationId", "epoch", "delegationId", "key" })
            Check("changed-generation-retains-private-result-" + field, f =>
            {
                f.Delegate(); string receipt = f.AddReceipt("old generation");
                string handoff = File.ReadAllText(f.Handoff);
                string oldValue = field switch { "installationId" => new string('1', 32), "epoch" => new string('2', 32), "delegationId" => new string('3', 32), _ => Convert.ToBase64String(new byte[32]) };
                string newValue = field == "key" ? Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()) : new string('4', 32);
                File.WriteAllText(f.Handoff, handoff.Replace(oldValue, newValue));
                Refuses(() => HelperRewardTransfer.ReturnPending(f.Session, f.Normal));
                Equal("old generation", File.ReadAllText(receipt));
                Equal(false, Directory.Exists(f.Inbox));
            });
        foreach (string kind in new[] { "missing-marker", "invalid-marker", "normal-exe-marker", "different-caller", "missing-origin", "missing-normal-authority", "private-destination" })
            Check("origin-guard-" + kind, f =>
            {
                f.Delegate(); string receipt = f.AddReceipt("keep");
                string caller = f.Normal;
                switch (kind)
                {
                    case "missing-marker": File.Delete(f.MarkerPath); break;
                    case "invalid-marker": File.WriteAllText(f.MarkerPath, "{}"); break;
                    case "normal-exe-marker": f.Marker.ServerExe = Path.Combine(f.Normal, HelperLayout.ServerExeName); f.SaveMarker(); break;
                    case "different-caller": caller = f.Root; break;
                    case "missing-origin": File.Delete(f.Origin); break;
                    case "missing-normal-authority": File.Delete(f.Handoff); break;
                    case "private-destination": File.WriteAllText(Path.Combine(f.Normal, ArenaSessionContract.MarkerFileName), "{}"); break;
                }
                Refuses(() => HelperRewardTransfer.ReturnPending(f.Session, caller));
                Equal("keep", File.ReadAllText(receipt));
                Equal(false, Directory.Exists(f.Inbox));
            });
        foreach (string kind in new[] { "empty", "too-large", "non-guid", "directory", "unknown-temp" })
            Check("receipt-guard-" + kind, f =>
            {
                f.Delegate(); string receipt = f.AddReceipt("keep");
                if (kind == "empty") File.WriteAllText(receipt, "");
                if (kind == "too-large") using (var s = File.OpenWrite(receipt)) s.SetLength(HelperRewardTransfer.MaxReceiptBytes + 1L);
                if (kind == "non-guid") File.Move(receipt, Path.Combine(f.Outbox, "bad.json"));
                if (kind == "directory") { File.Delete(receipt); Directory.CreateDirectory(receipt); }
                if (kind == "unknown-temp") File.WriteAllText(Path.Combine(f.Outbox, "unknown.tmp"), "incomplete");
                Refuses(() => HelperRewardTransfer.ReturnPending(f.Session, f.Normal));
                Equal(true, Directory.EnumerateFileSystemEntries(f.Outbox).Any());
            });
        Check("only-exact-unpublished-temp-can-be-discarded", f =>
        {
            Directory.CreateDirectory(f.Outbox);
            File.WriteAllText(Path.Combine(f.Outbox, ".arena-reward-" + Guid.NewGuid().ToString("N") + ".tmp"), "partial");
            Equal(false, HelperRewardTransfer.HasPublishedResults(f.Session));
            HelperRewardTransfer.RequireDrainedBeforeRebuild(f.Session);
        });
        Check("receipt-count-bound-before-transfer", f =>
        {
            f.Delegate();
            for (int i = 0; i <= HelperRewardTransfer.MaxReceipts; i++) f.AddReceipt("bounded");
            Refuses(() => HelperRewardTransfer.ReturnPending(f.Session, f.Normal));
            Equal(HelperRewardTransfer.MaxReceipts + 1, Directory.GetFiles(f.Outbox).Length);
            Equal(false, Directory.Exists(f.Inbox));
        });
        Check("locked-source-is-retained", f =>
        {
            f.Delegate(); string receipt = f.AddReceipt("keep");
            using (File.Open(receipt, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Refuses(() => HelperRewardTransfer.ReturnPending(f.Session, f.Normal));
            Equal("keep", File.ReadAllText(receipt));
            Equal(1, HelperRewardTransfer.ReturnPending(f.Session, f.Normal));
        });
        Check("blocked-inbox-is-not-replaced", f =>
        {
            f.Delegate(); string receipt = f.AddReceipt("keep"); File.WriteAllText(f.Inbox, "occupied");
            Refuses(() => HelperRewardTransfer.ReturnPending(f.Session, f.Normal));
            Equal("occupied", File.ReadAllText(f.Inbox)); Equal("keep", File.ReadAllText(receipt));
        });
        foreach (string kind in new[] { "inbox", "outbox", "origin", "marker", "handoff", "receipt", "destination" })
            Check("link-refused-" + kind, f =>
            {
                f.Delegate(); string receipt = f.AddReceipt("keep");
                string outside = Path.Combine(f.Root, "outside"); Directory.CreateDirectory(outside);
                string sentinel = Path.Combine(outside, "sentinel"); File.WriteAllText(sentinel, "unchanged");
                string link = kind switch { "inbox" => f.Inbox, "outbox" => f.Outbox, "origin" => f.Origin, "marker" => f.MarkerPath, "handoff" => f.Handoff, "receipt" => receipt, _ => f.Destination(receipt) };
                if (kind == "outbox") { File.Delete(receipt); Directory.Delete(f.Outbox); }
                else if (File.Exists(link)) File.Delete(link);
                Directory.CreateDirectory(Path.GetDirectoryName(link));
                if (kind is "inbox" or "outbox") Directory.CreateSymbolicLink(link, outside);
                else File.CreateSymbolicLink(link, sentinel);
                try
                {
                    Refuses(() => HelperRewardTransfer.ReturnPending(f.Session, f.Normal));
                    Equal("unchanged", File.ReadAllText(sentinel));
                    if (kind == "handoff") Refuses(() => f.Delegate());
                }
                finally
                {
                    if (kind is "inbox" or "outbox") Directory.Delete(link); else File.Delete(link);
                }
            });
        Check("runner-stop-transfers-stopped-private-session", f =>
        {
            f.Delegate(); string receipt = f.AddReceipt("stop transfer");
            var runner = f.Runner(); Equal(StopResult.NothingToStop, runner.StopArenaServer(f.Normal));
            Equal(false, File.Exists(receipt)); Equal("stop transfer", File.ReadAllText(f.Destination(receipt)));
        });
        Check("watchdog-does-not-touch-newer-session", f =>
        {
            f.Delegate(); string receipt = f.AddReceipt("new session");
            var runner = f.Runner(); runner.Watch = new HelperWatchFile { MarkerCreatedUtc = "2001-01-01T00:00:00Z", SptRuntimeDir = f.Normal };
            Equal(StopResult.NothingToStop, runner.StopAfterGameExit());
            Equal(true, File.Exists(receipt)); Equal(false, Directory.Exists(f.Inbox));
        });
        Check("rebuild-refuses-before-deleting-old-marker-or-results", f =>
        {
            f.Delegate(); string receipt = f.AddReceipt("old session");
            File.WriteAllText(Path.Combine(f.Normal, HelperLayout.ServerExeName), "test executable placeholder");
            string marker = File.ReadAllText(f.MarkerPath);
            Refuses(() => HelperServerTree.Build(new HelperRequest { Direction = "enter", ProfileId = Fixture.Profile, SptRuntimeDir = f.Normal }, 6968, f.Session, "test", DateTime.UtcNow, null));
            Equal(marker, File.ReadAllText(f.MarkerPath)); Equal("old session", File.ReadAllText(receipt));
        });
        foreach (string failure in failures) Console.Error.WriteLine(failure);
        Console.WriteLine($"REWARD_TRANSFER_CHECKS {(failures.Count == 0 ? "PASS" : "FAIL")} assertions={assertions} failures={failures.Count}");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Check(string name, Action<Fixture> test)
    {
        try { using var fixture = new Fixture(); test(fixture); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures.Add("FAIL " + name + ": " + ex); }
    }
    private static void Equal<T>(T expected, T actual)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
    }
    private static void Refuses(Action action)
    {
        assertions++;
        try { action(); } catch (Exception ex) when (ex is HelperException or IOException or UnauthorizedAccessException) { return; }
        throw new Exception("Unsafe operation was accepted");
    }
    private sealed class Fixture : IDisposable
    {
        public const string Profile = "aaaaaaaaaaaaaaaaaaaaaaaa";
        public readonly string Root = Path.Combine(Path.GetTempPath(), "ArenaRewardTransfer-" + Guid.NewGuid().ToString("N"));
        public string Normal => Path.Combine(Root, "normal");
        public string Session => Path.Combine(Root, "session");
        public string Tree => HelperServerTree.GetTreePath(Session);
        public string Handoff => Path.Combine(Normal, "user", "arena-rewards", Profile, "handoff.json");
        public string Origin => Path.Combine(Tree, HelperRewardTransfer.OriginFileName);
        public string Outbox => Path.Combine(Tree, "user", HelperRewardTransfer.OutboxFolderName);
        public string Inbox => Path.Combine(Path.GetDirectoryName(Handoff), "inbox");
        public string MarkerPath => Path.Combine(Tree, ArenaSessionContract.MarkerFileName);
        public HelperMarker Marker { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Tree); Directory.CreateDirectory(Path.GetDirectoryName(Handoff));
            WriteHandoff(true);
            Marker = HelperMarker.Create(DateTime.UtcNow, Profile, 6968, Normal, "policy", "test", Path.Combine(Tree, HelperLayout.ServerExeName));
            File.WriteAllText(Marker.ServerExe, "test executable placeholder"); SaveMarker();
        }
        public void SaveMarker() => File.WriteAllText(MarkerPath, Marker.ToJson());
        public void WriteHandoff(bool enabled) => File.WriteAllText(Handoff, ArenaMiniJson.Write(new Dictionary<string, object>
        {
            ["schema"] = 1, ["enabled"] = enabled, ["runtimeRoot"] = Normal,
            ["binding"] = new Dictionary<string, object> { ["installationId"] = new string('1', 32), ["profileId"] = Profile, ["epoch"] = new string('2', 32) },
            ["delegationId"] = new string('3', 32), ["key"] = Convert.ToBase64String(new byte[32])
        }, true), new UTF8Encoding(false));
        public bool Delegate() => HelperRewardTransfer.CopyHandoff(Normal, Profile, Tree);
        public string AddReceipt(string content)
        {
            Directory.CreateDirectory(Outbox); string file = Path.Combine(Outbox, Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(file, content, new UTF8Encoding(false)); return file;
        }
        public string Destination(string receipt) => Path.Combine(Inbox, Path.GetFileName(receipt));
        public HelperRunner Runner() => new HelperRunner(Session, new HelperLog(Path.Combine(Session, "helper.log")), "test");
        public void Dispose()
        {
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(Root).StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(Root).StartsWith("ArenaRewardTransfer-", StringComparison.Ordinal))
                throw new Exception("Fixture cleanup path is not owned");
            Directory.Delete(Root, true);
        }
    }
}
