using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArenaStart;

public static class RuntimeHost
{
    public const string Username = "Arena-Internal";
    private static readonly string[] ProcessNames = ["EscapeFromTarkov", "SPT.Server", "SPT.Launcher"];

    public static void RequireIdle()
    {
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = (string)entry.Key;
            if (key.StartsWith("BEPINEX_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("DOORSTOP_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("MONO_", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("DOTNET_STARTUP_HOOKS", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Loader override {key} is set. Clear it before using this isolated starter.");
        }
        foreach (var name in ProcessNames)
            if (HasRunningProcess(name))
                throw new InvalidOperationException($"Close {name} before changing start environments.");
        var listener = new TcpListener(IPAddress.Loopback, 6969) { ExclusiveAddressUse = true };
        try { listener.Start(); }
        catch (SocketException) { throw new InvalidOperationException("Port 6969 is occupied; no existing server will be reused."); }
        finally { listener.Stop(); }
    }

    public static async Task Run(string root, bool smoke, Action<string>? report = null, bool waitForClients = false,
        bool directGame = false)
    {
        RequireIdle();
        root = EnvironmentCopy.Root(root);
        using var control = BaseRebuild.Acquire(root);
        BaseRebuild.RequireNoPending(root);
        var manifest = BaseRebuild.Read<Manifest>(root, EnvironmentCopy.ManifestPath);
        var active = manifest.Schema == 2 ? BaseRebuild.Resolve(root, manifest.Source, manifest.Diagnostics) : root;
        await RunCore(active, smoke, report, waitForClients, directGame,
            anchorLease: active == root ? control.SafeFileHandle : null);
    }

    internal static async Task RunCore(string root, bool smoke, Action<string>? report = null, bool waitForClients = false,
        bool directGame = false, Microsoft.Win32.SafeHandles.SafeFileHandle? anchorLease = null,
        AssetLease? retainedAssets = null)
    {
        var messages = new RuntimeReporter(report ?? Console.WriteLine);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This prototype supports Windows only.");
        RequireIdle();
        root = EnvironmentCopy.Root(root);
        // Exclusive lease survives as a file after a crash; the OS releases its lock.
        using var lease = anchorLease == null ? new FileStream(EnvironmentCopy.Inside(root, ".arena-start/running"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None) : null;
        var runningHandle = anchorLease ?? lease!.SafeFileHandle;
        EnvironmentCopy.RequireSingleLink(runningHandle);
        ModUpdates.Recover(root, messages.Write);
        using var ownAssets = retainedAssets == null ? new AssetLease() : null;
        var assets = retainedAssets ?? ownAssets!;
        messages.Write("Verifying the private runtime and shared assets. This can take several minutes.");
        var manifest = EnvironmentCopy.Verify(root, messages.Write, runningHandle, assets);
        RequireIdle();
        manifest = ModUpdates.Apply(manifest, messages.Write);
        RequireIdle();
        if (directGame)
        {
            messages.Write("Checking the native SPT patch output. Game files stay unchanged.");
            DirectGame.ValidatePatch(root);
            RequireIdle();
        }
        NativeGameSettings.CopyFromSource(manifest, messages.Write);
        RequireIdle();
        var runtime = EnvironmentCopy.Inside(root, "SPT_Runtime");
        using var log = new StreamWriter(EnvironmentCopy.Inside(root, ".arena-start/server-start.log"), append: true) { AutoFlush = true };
        var info = new ProcessStartInfo(EnvironmentCopy.Inside(root, "SPT_Runtime/SPT.Server.exe"))
        {
            WorkingDirectory = runtime, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        // Keep inherited runtime overrides from redirecting the server out of its copy.
        foreach (var key in info.Environment.Keys.ToArray())
            if (key.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("COMPLUS_", StringComparison.OrdinalIgnoreCase))
                info.Environment.Remove(key);
        using var server = Process.Start(info) ?? throw new IOException("SPT server did not start.");
        Task stdout = Task.CompletedTask, stderr = Task.CompletedTask;
        Process? game = null;
        try
        {
            stdout = Pump(server.StandardOutput);
            stderr = Pump(server.StandardError);
            messages.Write($"Private Arena server PID {server.Id}. No existing server/profile is used.");
            using var http = await WaitForServer(root, server);
            await EnsureAccount(root, http, messages);
            EnvironmentCopy.ValidateProfileFiles(root);
            if (!directGame) WriteLauncherSettings(root);
            string? profileId = null;
            if (directGame)
            {
                messages.Write("Checking SPT versions and signing in to the private Arena account.");
                await DirectGame.ValidateVersion(root, http);
                profileId = await DirectGame.Login(root, http);
            }
            if (smoke)
            {
                messages.Write("ARENA_SERVER_SMOKE_PASS");
                return;
            }
            if (directGame)
            {
                DirectGame.RemovePrivateHwecho(root);
                if (AnyClients()) throw new InvalidOperationException("Another game or launcher was opened while Arena was preparing. Close it before retrying.");
                messages.Write("Starting the private game directly. No SPT Launcher window is needed.");
                game = Process.Start(DirectGame.StartInfo(root, profileId!))
                    ?? throw new IOException("The private game process could not be started.");
                messages.Write("Session running. Close EFT normally when finished; the private server will then stop.");
                var elapsed = Stopwatch.StartNew();
                await WaitForSessionEnd(() => !game.HasExited || AnyClients(),
                    () => server.HasExited ? server.ExitCode : null, messages);
                ValidateGameExit(game.ExitCode, elapsed.Elapsed);
                return;
            }
            var launcherInfo = new ProcessStartInfo(EnvironmentCopy.Inside(root, "SPT_Runtime/SPT.Launcher.exe"))
            {
                WorkingDirectory = runtime, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal
            };
            using var launcher = Process.Start(launcherInfo) ?? throw new IOException("SPT Launcher did not start.");
            messages.Write("Native SPT Launcher opened with the internal Arena account.");
            messages.Write("First launch: complete native character creation. The ordinary profile is not imported.");
            if (waitForClients)
            {
                messages.Write("Session running. Close the game AND SPT Launcher when finished; this window will then return to ready.");
                await WaitForSessionEnd(AnyClients, () => server.HasExited ? server.ExitCode : null, messages);
                return;
            }
            messages.Write("After playing, CLOSE GAME AND LAUNCHER, then press Enter here.");
            ConsoleCancelEventHandler cancel = (_, e) =>
            {
                e.Cancel = true;
                messages.Write("Close game and launcher first, then press Enter. Do not close this starter window while playing.");
            };
            Console.CancelKeyPress += cancel;
            try
            {
                while (true)
                {
                    if (Console.ReadLine() is null)
                    {
                        messages.Write("Input closed. Waiting for game and launcher before stopping the private server.");
                        await WaitUntilClientsClose(AnyClients);
                        break;
                    }
                    if (!AnyClients()) break;
                    messages.Write("Game or launcher is still running. Close both before stopping the private server.");
                }
            }
            finally { Console.CancelKeyPress -= cancel; }
        }
        finally
        {
            // Do not orphan redirected pipes on an error while native clients run.
            // Continue serving until the user has closed them; never kill the game.
            if (game is { HasExited: false } || AnyClients())
            {
                messages.Write("Waiting for game and launcher to close before stopping the private server.");
                await WaitUntilClientsClose(() => game is { HasExited: false } || AnyClients());
            }
            if (!server.HasExited)
            {
                // Own process only, after no client can still submit a raid/profile save.
                server.Kill(entireProcessTree: false);
                await server.WaitForExitAsync();
                await Task.WhenAll(stdout, stderr);
                messages.Write("Private Arena server stopped.");
            }
            else await Task.WhenAll(stdout, stderr);
            game?.Dispose();
        }

        async Task Pump(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
                lock (log) log.WriteLine(line);
        }
    }

    internal static async Task WaitUntilClientsClose(Func<bool> anyClients, Func<Task>? delay = null)
    {
        // No server-state condition: a failed server does not release live EFT's assets.
        while (anyClients()) await (delay?.Invoke() ?? Task.Delay(1000));
    }

    internal static void ValidateGameExit(int code, TimeSpan elapsed)
    {
        // Early native prevalidation failures can exit with 0. This is an early-exit
        // alarm, NOT proof of a loaded main menu for longer-lived processes.
        if (code != 0 || elapsed < TimeSpan.FromSeconds(20))
            throw new IOException($"The private game exited early or with an error ({code}). See the private BepInEx and game logs; no automatic repair was performed.");
    }

    internal static async Task WaitForSessionEnd(Func<bool> anyClients, Func<int?> serverExit,
        RuntimeReporter messages, Func<Task>? delay = null)
    {
        var reported = false;
        while (anyClients())
        {
            if (!reported && serverExit() is { } code)
            {
                messages.Write($"Private server exited unexpectedly ({code}). Close EFT and SPT Launcher; shared assets remain locked until then.");
                reported = true;
            }
            await (delay?.Invoke() ?? Task.Delay(1000));
        }
        if (serverExit() is { } exit)
            throw new IOException($"Private server exited unexpectedly ({exit}); see .arena-start/server-start.log. No automatic repair was performed.");
    }

    internal static bool HasRunningProcess(string name) => AnyRunning(Process.GetProcessesByName(name));

    internal static bool AnyRunning(Process[] processes)
    {
        // Windows can still enumerate a terminated process while a handle is
        // retained. Its presence alone is not a live client/server. Query errors
        // still propagate; an inaccessible process must not be treated as idle.
        try { return processes.Any(process => !process.HasExited); }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static bool AnyClients()
    {
        // During a session retain the original conservative enumeration check:
        // no per-process access query may release a live client's asset leases.
        foreach (var name in new[] { "EscapeFromTarkov", "SPT.Launcher" })
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var process in processes) process.Dispose();
            if (processes.Length != 0) return true;
        }
        return false;
    }

    private static async Task<HttpClient> WaitForServer(string root, Process server)
    {
        var certificatePath = EnvironmentCopy.Inside(root, "SPT_Runtime/user/certs/server.crt");
        var deadline = DateTime.UtcNow.AddMinutes(3);
        HttpClient? http = null;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                if (server.HasExited) throw new IOException($"Private server exited ({server.ExitCode}); see .arena-start/server-start.log.");
                if (File.Exists(certificatePath))
                {
                    EnvironmentCopy.AssertAncestors(certificatePath);
                    if (http is null)
                    {
                        // Trust only the certificate created in this private server's
                        // directory, only on this loopback URI. Never trust all certificates.
                        using var certificate = X509Certificate2.CreateFromPem(EnvironmentCopy.ReadPrivate(certificatePath));
                        var fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
                        var handler = new HttpClientHandler
                        {
                            AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
                            ServerCertificateCustomValidationCallback = (request, presented, _, _) =>
                                request.RequestUri?.Host == "127.0.0.1" && request.RequestUri.Port == 6969 &&
                                presented?.GetCertHashString(HashAlgorithmName.SHA256) == fingerprint
                        };
                        http = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:6969"), Timeout = TimeSpan.FromSeconds(3) };
                    }
                    try
                    {
                        var ping = await Request(http, "/launcher/v2/ping");
                        if (Value(ping, "response")?.GetValue<string>() == "Pong!")
                        {
                            if (server.HasExited) throw new IOException("Private server exited during startup.");
                            return http;
                        }
                    }
                    catch (HttpRequestException) { }
                    catch (TaskCanceledException) { }
                }
                await Task.Delay(500);
            }
            throw new TimeoutException("Private server did not become ready within three minutes.");
        }
        catch { http?.Dispose(); throw; }
    }

    internal static JsonNode? Value(JsonNode node, string name) =>
        node.AsObject().FirstOrDefault(x => x.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    internal static async Task<JsonNode> Request(HttpClient client, string path, object? payload = null)
    {
        using var request = new HttpRequestMessage(payload == null ? HttpMethod.Get : HttpMethod.Put, path);
        if (payload != null)
        {
            using var buffer = new MemoryStream();
            using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            {
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
                zlib.Write(bytes);
            }
            request.Content = new ByteArrayContent(buffer.ToArray());
        }
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var compressed = await response.Content.ReadAsByteArrayAsync();
        using var input = new MemoryStream(compressed);
        using var decompressor = new ZLibStream(input, CompressionMode.Decompress);
        return await JsonNode.ParseAsync(decompressor) ?? throw new InvalidDataException("Invalid SPT response.");
    }

    private static async Task EnsureAccount(string root, HttpClient http, RuntimeReporter messages)
    {
        var profilesResponse = await Request(http, "/launcher/v2/profiles");
        var profiles = Value(profilesResponse, "response")?.AsArray() ?? throw new InvalidDataException("No profile list.");
        var receipt = EnvironmentCopy.Inside(root, EnvironmentCopy.AccountPath);
        if (!File.Exists(receipt))
        {
            if (profiles.Count != 0) throw new InvalidDataException("Unowned profile appeared. Refusing adoption/import.");
            var types = Value(await Request(http, "/launcher/v2/types"), "response")?.AsObject();
            if (types?.ContainsKey("Standard") != true) throw new InvalidDataException("Native Standard profile template is unavailable.");
            var registered = await Request(http, "/launcher/v2/register", new { username = Username, edition = "Standard" });
            if (Value(registered, "response")?.GetValue<bool>() != true)
                throw new InvalidDataException("Native account creation failed.");
            profiles = Value(registered, "profiles")?.AsArray() ?? throw new InvalidDataException("No native account result.");
            if (profiles.Count != 1 || profiles[0]?["username"]?.GetValue<string>() != Username)
                throw new InvalidDataException("Unexpected native account result.");
            var id = profiles[0]!["profileId"]?.GetValue<string>() ?? "";
            if (id.Length != 24 || id.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid generated identity.");
            EnvironmentCopy.WriteNew(receipt, JsonSerializer.Serialize(new { username = Username, profileId = id }, EnvironmentCopy.Json));
            messages.Write("Fresh internal account created using SPT's native register route.");
        }
        else
        {
            var account = JsonNode.Parse(EnvironmentCopy.ReadPrivate(receipt))!;
            if (profiles.Count != 1 || profiles[0]?["profileId"]?.GetValue<string>() != account["profileId"]?.GetValue<string>() ||
                profiles[0]?["username"]?.GetValue<string>() != Username)
                throw new InvalidDataException("Server exposes an unexpected profile.");
        }
    }

    public static void WriteLauncherSettings(string root)
    {
        var account = JsonNode.Parse(EnvironmentCopy.ReadPrivate(EnvironmentCopy.Inside(root, EnvironmentCopy.AccountPath)))!;
        var path = EnvironmentCopy.Inside(root, "SPT_Runtime/user/Launcher/LauncherSettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var settings = new
        {
            Servers = Array.Empty<object>(), FirstRun = false, EnableGamePath = true, GamePath = root,
            CloseToTray = false, AutoConnectLastProfile = true, MinimizeOnLaunch = false, ClearCacheOnLaunch = false,
            PreferredProfile = new { ServerId = "1721162719", ProfileId = account["profileId"]!.GetValue<string>() },
            Language = "en"
        };
        // Only our copied environment, never an original launcher configuration.
        if (File.Exists(path)) _ = EnvironmentCopy.ReadPrivate(path);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, EnvironmentCopy.Json));
    }
}
