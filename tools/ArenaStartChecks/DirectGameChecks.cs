using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ArenaStart;

internal static class DirectGameChecks
{
    internal static async Task<int> Run(string scratch, string root, string[] args)
    {
        var count = 0;
        void Check(bool condition, string reason) { count++; if (!condition) throw new Exception(reason); }
        void Reject(Action action, string reason)
        {
            try { action(); }
            catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
            { count++; return; }
            throw new Exception("Accepted negative direct-start control: " + reason);
        }
        const string id = "0123456789abcdef01234567";
        JsonNode Mini(string identity = id, string name = RuntimeHost.Username, bool invalid = false) =>
            new JsonObject { ["profileId"] = identity, ["username"] = name, ["invalidOrUnloadableProfile"] = invalid, ["wipe"] = true };
        DirectGame.ValidateLoginProfile(Mini(), id);
        Check(true, "fresh native account with wipe=true accepted");
        Reject(() => DirectGame.ValidateLoginProfile(Mini("aaaaaaaaaaaaaaaaaaaaaaaa"), id), "foreign profile");
        Reject(() => DirectGame.ValidateLoginProfile(Mini(name: "ordinary"), id), "foreign username");
        Reject(() => DirectGame.ValidateLoginProfile(Mini(invalid: true), id), "unloadable profile");
        Reject(() => DirectGame.ValidateLoginProfile(null, id), "missing login detail");
        var missingValidity = Mini().AsObject();
        missingValidity.Remove("invalidOrUnloadableProfile");
        DirectGame.ValidateLoginProfile(missingValidity, id);
        Check(true, "healthy native profile omits nullable error flag");
        missingValidity["invalidOrUnloadableProfile"] = null;
        DirectGame.ValidateLoginProfile(missingValidity, id);
        Check(true, "native nullable error flag");
        missingValidity["invalidOrUnloadableProfile"] = "false";
        Reject(() => DirectGame.ValidateLoginProfile(missingValidity, id), "malformed error flag");

        using (var http = new HttpClient(new LoginHandler(false, Mini(), Check)) { BaseAddress = new Uri(DirectGame.Backend) })
        {
            try { await DirectGame.Login(root, http); throw new Exception("Rejected login started profile selection"); }
            catch (InvalidDataException) { count++; }
        }
        var healthyNative = Mini().AsObject();
        healthyNative.Remove("invalidOrUnloadableProfile");
        using (var http = new HttpClient(new LoginHandler(true, healthyNative, Check)) { BaseAddress = new Uri(DirectGame.Backend) })
            Check(await DirectGame.Login(root, http) == id, "compressed native login did not return owned identity");
        using (var http = new HttpClient(new LoginHandler(true, Mini("aaaaaaaaaaaaaaaaaaaaaaaa"), Check)) { BaseAddress = new Uri(DirectGame.Backend) })
        {
            try { await DirectGame.Login(root, http); throw new Exception("Foreign server profile adopted"); }
            catch (InvalidDataException) { count++; }
        }
        DirectGame.ValidateVersions("4.1.6", new Version(4, 1, 6));
        DirectGame.ValidateVersions("4.1.6 - test build", new Version(4, 1, 6));
        Check(true, "matching release/development version accepted");
        foreach (var version in new[] { null, "", "4.1", "4.2.6", "4.1.7", "4.1.6.0", "not-a-version" })
            Reject(() => DirectGame.ValidateVersions(version, new Version(4, 1, 6)), "server/core version mismatch");
        Reject(() => DirectGame.ValidateVersions("4.1.6", new Version(1, 0, 0)), "development DLL bypass");

        var spaced = Path.Combine(scratch, "game root with spaces");
        Directory.CreateDirectory(spaced);
        var start = DirectGame.StartInfo(spaced, id);
        Check(!start.UseShellExecute && start.WorkingDirectory == spaced &&
            start.FileName == Path.Combine(spaced, "EscapeFromTarkov.exe") && start.ArgumentList.Count == 4 &&
            start.Arguments == "", "direct start can use shell or an unrelated working directory");
        Check(start.ArgumentList[0] == "-force-gfx-jobs" && start.ArgumentList[1] == "native" &&
            start.ArgumentList[2] == "-token=" + id, "native argument contract lost");
        var config = JsonNode.Parse(start.ArgumentList[3]["-config=".Length..])!;
        Check(config["BackendUrl"]!.GetValue<string>() == DirectGame.Backend &&
            config["Version"]!.GetValue<string>() == "live" && config["MatchingVersion"]!.GetValue<string>() == "live",
            "backend or matching config escaped private loopback");
        Reject(() => DirectGame.StartInfo(spaced, id + " --escape"), "argument injection through identity");
        try
        {
            using var process = Process.Start(start);
            throw new Exception("Absent game unexpectedly launched");
        }
        catch (System.ComponentModel.Win32Exception) { count++; }
        Reject(() => RuntimeHost.ValidateGameExit(0, TimeSpan.FromSeconds(2)), "early native exit 0 treated as ready");
        Reject(() => RuntimeHost.ValidateGameExit(1, TimeSpan.FromMinutes(5)), "late failed game exit treated as success");
        RuntimeHost.ValidateGameExit(0, TimeSpan.FromMinutes(5));
        Check(true, "ordinary later close accepted without claiming menu readiness");

        var hwecho = EnvironmentCopy.Inside(root, "EscapeFromTarkov_Data/Plugins/x86_64/hwecho.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(hwecho)!);
        var unrelated = Path.Combine(scratch, "preserved-hwecho.dll");
        File.WriteAllText(unrelated, "protected original");
        Check(NativeLinks.CreateHardLink(hwecho, unrelated, IntPtr.Zero), "hwecho hardlink negative fixture");
        Reject(() => DirectGame.RemovePrivateHwecho(root), "linked cleanup target");
        Check(File.ReadAllText(unrelated) == "protected original" && File.Exists(hwecho), "linked cleanup changed source");
        File.Delete(hwecho);
        File.WriteAllText(hwecho, "private native cleanup fixture");
        DirectGame.RemovePrivateHwecho(root);
        Check(!File.Exists(hwecho) && File.Exists(unrelated), "exact private cleanup");

        if (args.Length == 2 && args[0] == "--native-patch")
        {
            // Installed game content is used only in this disposable test directory,
            // never checked in as fixtures and never modified at the supplied source.
            var native = EnvironmentCopy.Root(args[1]);
            var copied = Path.Combine(scratch, "native-patch-check");
            Directory.CreateDirectory(Path.Combine(copied, ".arena-start"));
            foreach (var relative in new[] { DirectGame.AssemblyPath, DirectGame.AssemblyPath + ".spt-bak", DirectGame.DeltaPath })
            {
                var source = EnvironmentCopy.Inside(native, relative);
                var target = EnvironmentCopy.Inside(copied, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
            }
            var installed = EnvironmentCopy.Inside(copied, DirectGame.AssemblyPath);
            var expected = File.ReadAllBytes(installed);
            DirectGame.ValidatePatch(copied);
            Check(File.ReadAllBytes(installed).SequenceEqual(expected), "native patch proof modified installed assembly");
            using (var writer = new FileStream(installed, FileMode.Open, FileAccess.Write)) writer.WriteByte((byte)(expected[0] ^ 1));
            Reject(() => DirectGame.ValidatePatch(copied), "same-size mutated game DLL");
            File.WriteAllBytes(installed, expected);
            Check(!Directory.EnumerateFiles(Path.Combine(copied, ".arena-start")).Any(), "patch scratch output leaked on success/rejection");
            var savedDelta = EnvironmentCopy.Inside(copied, DirectGame.DeltaPath);
            File.Delete(savedDelta);
            Reject(() => DirectGame.ValidatePatch(copied), "missing native delta");
            Console.WriteLine("NATIVE_PATCH_OUTPUT_CHECKS_PASS");
        }
        else if (args.Length != 0) throw new ArgumentException("Expected --native-patch <installed private runtime root>.");
        return count;
    }

    private sealed class LoginHandler(bool accepted, JsonNode profile, Action<bool, string> check) : HttpMessageHandler
    {
        private int requests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests++;
            check(request.Method == HttpMethod.Put && request.RequestUri!.GetLeftPart(UriPartial.Authority) == DirectGame.Backend,
                "login request must be private PUT");
            using var input = new MemoryStream(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            using var unzip = new ZLibStream(input, CompressionMode.Decompress);
            var payload = (await JsonNode.ParseAsync(unzip, cancellationToken: cancellationToken))!.AsObject();
            check(payload.Count == 1 && payload["username"]!.GetValue<string>() == RuntimeHost.Username,
                "unexpected login payload/profile transfer");
            check(request.RequestUri!.AbsolutePath == (requests == 1 ? "/launcher/v2/login" : "/launcher/v2/profile") &&
                (requests == 1 || accepted), "login/detail route order violated");
            var response = new JsonObject { ["response"] = requests == 1 ? JsonValue.Create(accepted) : profile.DeepClone() };
            using var bytes = new MemoryStream();
            using (var zip = new ZLibStream(bytes, CompressionLevel.Optimal, leaveOpen: true))
                zip.Write(Encoding.UTF8.GetBytes(response.ToJsonString()));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes.ToArray()) };
        }
    }
}
