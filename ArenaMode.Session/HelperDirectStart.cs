// Linked into the desktop test suite; no Unity, launcher or UI dependency.
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ArenaMode.Shared.Session;

namespace ArenaMode.SessionHelper
{
    internal sealed class HelperStartProfile
    {
        internal string Id, Name, Side;
        internal int Level;
        public override string ToString() { return Name + "  ·  " + Side + "  ·  Lv. " + Level; }
    }

    /// <summary>A fresh local preparation, followed by the existing schema-3 ownership transfer.</summary>
    internal sealed class HelperDirectStart
    {
        private readonly string _stateRoot;
        private readonly HelperLog _log;
        private readonly HelperRunner _runner;
        internal readonly string GameRoot, Runtime, HelperExe;
        internal Action<string> Progress = _ => { };

        internal HelperDirectStart(string helperExe, string stateRoot, HelperLog log, HelperRunner runner)
        {
            HelperExe = Path.GetFullPath(helperExe);
            GameRoot = FindGameRoot(HelperExe);
            Runtime = Path.Combine(GameRoot, "SPT_Runtime");
            _stateRoot = stateRoot; _log = log; _runner = runner;
        }

        internal static string FindGameRoot(string helperExe)
        {
            if (!HelperPathSafety.IsDriveAbsolute(helperExe)) throw new HelperException("Start the installed ArenaMode helper.");
            string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(helperExe), "..", "..", ".."));
            string installed = Path.Combine(root, "BepInEx", "plugins", ArenaSessionContract.PluginFolderName, ArenaSessionContract.HelperFileName);
            if (!HelperPathSafety.PathsEqual(helperExe, installed)) throw new HelperException("Start the helper in BepInEx/plugins/maschine-ArenaMode.");
            HelperRewardControlTransfer.RequirePlainPath(installed, true);
            foreach (string path in new[] { Path.Combine(root, "EscapeFromTarkov.exe"),
                Path.Combine(root, "SPT_Runtime", HelperLayout.ServerExeName),
                Path.Combine(root, "BepInEx", "patchers", ArenaSessionContract.PluginFolderName, ArenaSessionContract.GateFileName) })
            {
                HelperRewardControlTransfer.RequirePlainPath(path, true);
                if (!File.Exists(path)) throw new HelperException("The SPT/ArenaMode installation is incomplete. Install ArenaMode in the game folder.");
            }
            return root;
        }

        internal List<HelperStartProfile> Profiles()
        {
            HelperRunner.RequireNoRunningGame(Path.Combine(GameRoot, "EscapeFromTarkov.exe"));
            return ReadProfiles(Path.Combine(Runtime, "user", "profiles"));
        }

        internal static List<HelperStartProfile> ReadProfiles(string directory)
        {
            HelperRewardControlTransfer.RequirePlainPath(directory, true);
            var profiles = new List<HelperStartProfile>();
            if (!Directory.Exists(directory)) return profiles;
            foreach (string path in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                string id = Path.GetFileNameWithoutExtension(path);
                if (!HelperRequest.IsValidProfileId(id)) continue;
                try
                {
                    object data; string error;
                    if (!ArenaMiniJson.TryParse(ReadText(path, 32 * 1024 * 1024), out data, out error)) continue;
                    var root = ArenaMiniJson.AsObject(data);
                    var info = Child(root, "info");
                    object wipe;
                    if (info == null || info.TryGetValue("wipe", out wipe) && wipe != null && (!(wipe is bool) || (bool)wipe)) continue;
                    object invalid;
                    if (info.TryGetValue("invalidOrUnloadableProfile", out invalid) && invalid != null
                        && (!(invalid is bool) || (bool)invalid)) continue;
                    var pmc = Child(Child(root, "characters"), "pmc");
                    var character = Child(pmc, "Info");
                    string profileId = ArenaMiniJson.GetString(info, "id");
                    string name = ArenaMiniJson.GetString(character, "Nickname");
                    string side = ArenaMiniJson.GetString(character, "Side");
                    int level = ArenaMiniJson.GetInt(character, "Level", 0);
                    if (!string.Equals(profileId, id, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(name)
                        || (side != "Usec" && side != "Bear") || level < 1) continue;
                    profiles.Add(new HelperStartProfile { Id = id.ToLowerInvariant(), Name = name, Side = side.ToUpperInvariant(), Level = level });
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is HelperException || ex is DecoderFallbackException) { }
            }
            profiles.Sort((a, b) => { int order = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
                return order == 0 ? StringComparer.Ordinal.Compare(a.Id, b.Id) : order; });
            return profiles;
        }

        private string PreferencePath { get { return Path.Combine(_stateRoot, "launch", Hash(Encoding.UTF8.GetBytes(GameRoot.ToUpperInvariant())) + ".json"); } }
        internal string RememberedProfile()
        {
            try
            {
                object data; string error;
                if (!ArenaMiniJson.TryParse(ReadText(PreferencePath, 4096), out data, out error)) return null;
                string id = ArenaMiniJson.GetString(data, "profileId");
                return HelperRequest.IsValidProfileId(id) ? id : null;
            }
            catch (Exception) { return null; }
        }

        private void Remember(string profile)
        {
            try { new HelperSandbox(_stateRoot, null).WriteAllText(PreferencePath,
                ArenaMiniJson.Write(new Dictionary<string, object> { { "profileId", profile } }, false)); }
            catch (Exception) { _log.Warn("The selected Arena profile could not be remembered."); }
        }

        internal bool Run(string profileId)
        {
            try
            {
                if (!HelperRequest.IsValidProfileId(profileId) || !Profiles().Exists(p => p.Id == profileId))
                    throw new HelperException("Choose an initialized SPT profile. Create new profiles in the SPT Launcher first.");
                HelperHttpEndpoint endpoint = ReadEndpoint(Runtime);
                ArenaWhitelistPolicy policy = ReadPolicy(GameRoot);
                // Fika is a platform unit and would never be removed by the policy.
                foreach (string path in Directory.GetFiles(Path.Combine(GameRoot, "BepInEx", "plugins"), "Fika*.dll", SearchOption.AllDirectories))
                    if (string.Equals(Path.GetFileName(path), "Fika.Core.dll", StringComparison.OrdinalIgnoreCase))
                        throw new HelperException("ArenaMode is unavailable with Fika.");
                Progress("Preparing SPT…");
                HelperRunner.RequireNoRunningGame(Path.Combine(GameRoot, "EscapeFromTarkov.exe"));
                // EXIT may have left the watchdog waiting for this helper's mutex.
                // Retire that verified private writer before normal startup recovers it.
                StopResult stopped = _runner.StopArenaServer(Runtime);
                if (stopped == StopResult.OtherInstallation || stopped == StopResult.Unverified || stopped == StopResult.StillRunning)
                    throw new HelperException("The previous Arena server is still running or could not be verified. Check helper.log.");
                using (Process server = GetNormalServer(endpoint))
                {
                    DateTime started;
                    if (!HelperProcesses.TryGetStartTimeUtc(server, out started)) throw new HelperException("The SPT server could not be identified.");
                    Action verify = () => RequireServer(server, started, endpoint);
                    verify();
                    string certPath = Path.Combine(Runtime, "user", "certs", "server.crt");
                    HelperRewardControlTransfer.RequirePlainPath(certPath, true);
                    string pin;
                    using (var cert = new X509Certificate2(certPath)) pin = Hash(cert.RawData);
                    var origin = new Uri("https://" + (endpoint.ServerHost == "::1" ? "[::1]" : endpoint.ServerHost) + ":" + endpoint.ServerPort);
                    var ping = Send(origin, pin, profileId, "/arenamode/ping", null, verify);
                    List<HelperServerMod> mods = AllowedMods(ping, policy, Runtime);
                    string sessionId = Guid.NewGuid().ToString("N");
                    HelperRunner.RequireNoRunningGame(Path.Combine(GameRoot, "EscapeFromTarkov.exe"));
                    var reply = Send(origin, pin, profileId, "/arenamode/session/prepare",
                        new Dictionary<string, object> { { "SessionId", sessionId } }, verify);
                    object accepted;
                    if (!reply.TryGetValue("Accepted", out accepted) || !(accepted is bool) || !(bool)accepted)
                        throw new HelperException("SPT could not prepare Arena. Finish any active raid and check the server log.");
                    // Policy changes during preparation must not strand the player after the restart.
                    if (ReadPolicy(GameRoot).Hash != policy.Hash) throw new HelperException("The Arena mod list changed. Start Arena again.");
                    var request = BuildRequest(GameRoot, profileId, origin, sessionId, policy.Hash, mods);
                    verify();
                    Remember(profileId);
                    Progress("Starting Arena…");
                    return _runner.EnterDirect(request);
                }
            }
            catch (Exception ex)
            {
                _log.Error("direct start failed: " + ex.Message);
                _runner.FailureNotice(ex is HelperException ? ex.Message : "Arena could not start. Check helper.log and the SPT server log.");
                return false;
            }
        }

        private Process GetNormalServer(HelperHttpEndpoint endpoint)
        {
            HelperRunner.RequireNoRunningGame(Path.Combine(GameRoot, "EscapeFromTarkov.exe"));
            string exe = Path.Combine(Runtime, HelperLayout.ServerExeName);
            Process server = FindNormalServer(exe);
            try
            {
                if (server == null)
                {
                    if (!HelperProcesses.IsPortFree(endpoint.ServerHost, endpoint.ServerPort))
                        throw new HelperException("The SPT port is in use by another process. Close it before starting Arena.");
                    // Do not hold the startup lock here: normal startup needs it to recover an old session.
                    server = _runner.ProcessStarter(exe, string.Empty, Runtime);
                    if (server == null) throw new HelperException("The SPT server did not start.");
                    _log.Info("direct start: preparing the normal SPT server");
                }
                _runner.WaitForServer(endpoint.ServerHost, endpoint.ServerPort, server, "the normal server", false);
                return server;
            }
            catch { if (server != null) server.Dispose(); throw; } // Never kill the normal writer.
        }

        internal static Process FindNormalServer(string executable)
        {
            // FindByImage deliberately skips inaccessible candidates. Here absence is
            // authority to start a writer, so unknown identity must instead refuse.
            Process selected = null;
            Process[] candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));
            try
            {
                foreach (Process candidate in candidates)
                {
                    if (candidate.HasExited) continue;
                    string image = HelperProcesses.GetImagePath(candidate);
                    if (image == null || !HelperProcesses.SameFileSystemObject(image, executable))
                        throw new HelperException("Another SPT server is running or cannot be identified. Close it before starting Arena.");
                    if (selected != null) throw new HelperException("More than one SPT server is running for this installation.");
                    selected = candidate;
                }
                return selected;
            }
            catch { selected = null; throw; }
            finally { foreach (Process candidate in candidates) if (candidate != selected) candidate.Dispose(); }
        }

        private void RequireServer(Process server, DateTime started, HelperHttpEndpoint endpoint)
        {
            DateTime actual;
            if (server.HasExited || !HelperProcesses.TryGetStartTimeUtc(server, out actual) || actual != started
                || !HelperProcesses.SameFileSystemObject(HelperProcesses.GetImagePath(server), Path.Combine(Runtime, HelperLayout.ServerExeName)))
                throw new HelperException("The original SPT server is no longer running.");
            var ids = _runner.ListenerLookup(endpoint.ServerPort, IPAddress.Parse(endpoint.ServerHost));
            if (ids == null || ids.Count != 1 || ids[0] != server.Id)
                throw new HelperException("The SPT server port could not be verified.");
        }

        internal static HelperHttpEndpoint ReadEndpoint(string runtime)
        {
            string text = ReadText(Path.Combine(runtime, "SPT_Data", "configs", "http.json"), 256 * 1024);
            HelperHttpEndpoint endpoint; string error; HelperJsonDocument doc;
            if (!HelperJsonText.TryReadEndpoint(text, out endpoint, out error) || !HelperJsonDocument.TryScan(text, out doc, out error))
                throw new HelperException("The SPT server address could not be read.");
            string ip = null;
            foreach (var member in doc.Members)
                if (member.Depth == 1 && member.Key == "ip") { object value;
                    if (ArenaMiniJson.TryParse(text.Substring(member.ValueStart, member.ValueEnd - member.ValueStart), out value, out error)) ip = value as string; }
            IPAddress address;
            if (!IPAddress.TryParse(ip, out address) || !(address.Equals(IPAddress.Loopback) || address.Equals(IPAddress.IPv6Loopback))
                || endpoint.ServerPort != endpoint.BackendPort)
                throw new HelperException("Direct Arena start requires a local SPT server using 127.0.0.1 or ::1.");
            endpoint.ServerHost = address.ToString();
            return endpoint;
        }

        internal static ArenaWhitelistPolicy ReadPolicy(string root)
        {
            string defaults, user, detail;
            if (ArenaPolicyFiles.TryRead(Path.Combine(root, "BepInEx", "plugins", ArenaSessionContract.PluginFolderName,
                ArenaSessionContract.DefaultPolicyFileName), out defaults, out detail) != ArenaPolicyFileState.Read)
                throw new HelperException("The Arena mod list is missing or unreadable. Reinstall ArenaMode.");
            ArenaPolicyFiles.TryRead(Path.Combine(root, "BepInEx", "config", ArenaSessionContract.UserPolicyFileName), out user, out detail);
            ArenaWhitelistPolicy policy; List<string> notes;
            if (!ArenaWhitelistPolicy.TryLoad(defaults, user, out policy, out notes)) throw new HelperException("The Arena mod list is invalid.");
            return policy;
        }

        internal static List<HelperServerMod> AllowedMods(Dictionary<string, object> ping, ArenaWhitelistPolicy policy, string runtime)
        {
            object arena;
            if (!ping.TryGetValue("arenaInstance", out arena) || !(arena is bool) || (bool)arena)
                throw new HelperException("The normal SPT server is not ready for Arena.");
            var source = ArenaMiniJson.GetArray(ping, "serverMods");
            if (source == null || source.Count == 0) throw new HelperException("Update the ArenaMode server mod before starting Arena.");
            var units = new List<ArenaSessionUnit>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object item in source)
            {
                string guid = ArenaMiniJson.GetString(item, "guid"), folder = ArenaMiniJson.GetString(item, "folder");
                if (string.IsNullOrWhiteSpace(guid) || !ids.Add(guid) || !HelperPathSafety.IsDriveAbsolute(folder)
                    || !HelperPathSafety.PathsEqual(Path.GetDirectoryName(folder), Path.Combine(runtime, "user", "mods")))
                    throw new HelperException("SPT reported an invalid server mod list.");
                if (ArenaWhitelistPolicy.IsFikaGuid(guid)) throw new HelperException("ArenaMode is unavailable with Fika.");
                var dependencies = new List<string>();
                var raw = ArenaMiniJson.GetArray(item, "dependencies");
                if (raw == null) throw new HelperException("Update the ArenaMode server mod before starting Arena.");
                foreach (var dep in raw) { if (!(dep is string) || string.IsNullOrWhiteSpace((string)dep)) throw new HelperException("Invalid server mod dependency."); dependencies.Add((string)dep); }
                units.Add(new ArenaSessionUnit(guid, ArenaMiniJson.GetString(item, "name"), ArenaMiniJson.GetString(item, "version"), folder, dependencies));
            }
            if (!ids.Contains(ArenaSessionContract.ArenaPluginGuid)) throw new HelperException("The ArenaMode server mod is not loaded.");
            var allowed = new List<HelperServerMod>();
            foreach (var unit in ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Server, units).Allowed)
                allowed.Add(new HelperServerMod { Guid = unit.Guid, Folder = unit.File });
            return allowed;
        }

        internal static HelperRequest BuildRequest(string root, string profile, Uri origin, string session, string hash, List<HelperServerMod> mods)
        {
            var entries = new List<object>();
            foreach (var mod in mods) entries.Add(new Dictionary<string, object> { { "guid", mod.Guid }, { "folder", mod.Folder } });
            var args = new List<string> { "-force-gfx-jobs", "native", "-token=" + profile,
                "-config={'BackendUrl':'" + origin.GetLeftPart(UriPartial.Authority) + "','Version':'live','MatchingVersion':'live'}" };
            var json = ArenaMiniJson.Write(new Dictionary<string, object> {
                { "schema", ArenaSessionContract.OfflineRequestSchema }, { "direction", "enter" }, { "gamePid", 0 },
                { "gameExe", Path.Combine(root, "EscapeFromTarkov.exe") }, { "gameDir", root }, { "sptRuntimeDir", Path.Combine(root, "SPT_Runtime") },
                { "profileId", profile }, { "offlineSessionId", session }, { "policyHash", hash }, { "gameArgs", args }, { "serverMods", entries }
            }, false);
            HelperRequest request; string error;
            if (!HelperRequest.TryParseDirectStart(json, out request, out error)) throw new HelperException("The Arena start could not be prepared: " + error);
            return request;
        }

        private static Dictionary<string, object> Send(Uri origin, string pin, string profile, string route, Dictionary<string, object> data, Action verify)
        {
            verify();
            var request = (HttpWebRequest)WebRequest.Create(new Uri(origin, route));
            request.Method = data == null ? "GET" : "POST"; request.ContentType = "application/json";
            request.Proxy = null; request.AllowAutoRedirect = false; request.KeepAlive = false;
            request.Timeout = 30000; request.ReadWriteTimeout = 30000;
            request.Headers["requestcompressed"] = "0"; request.Headers["responsecompressed"] = "0";
            request.Headers[HttpRequestHeader.Cookie] = "PHPSESSID=" + profile;
            request.ServerCertificateValidationCallback = (_, cert, chain, errors) => cert != null && Hash(cert.GetRawCertData()) == pin;
            if (data != null) { byte[] bytes = Encoding.UTF8.GetBytes(ArenaMiniJson.Write(data, false)); request.ContentLength = bytes.Length;
                using (var output = request.GetRequestStream()) output.Write(bytes, 0, bytes.Length); }
            using (var response = request.GetResponse())
            using (var input = response.GetResponseStream())
            {
                string text = ReadBounded(input, 512 * 1024); object result; string error;
                verify();
                if (!ArenaMiniJson.TryParse(text, out result, out error) || ArenaMiniJson.AsObject(result) == null)
                    throw new HelperException("SPT returned an invalid Arena response.");
                return ArenaMiniJson.AsObject(result);
            }
        }

        private static Dictionary<string, object> Child(Dictionary<string, object> data, string name)
        { object value; return data != null && data.TryGetValue(name, out value) ? ArenaMiniJson.AsObject(value) : null; }

        internal static string ReadText(string path, int limit)
        {
            HelperRewardControlTransfer.RequirePlainPath(path, true);
            // The picker is only a hint. A live normal server must remain free to
            // atomically replace its profile; the server grants/validates entry later.
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) return ReadBounded(input, limit);
        }

        private static string ReadBounded(Stream stream, int limit)
        {
            using (var buffer = new MemoryStream())
            {
                byte[] chunk = new byte[8192]; int n;
                while ((n = stream.Read(chunk, 0, chunk.Length)) > 0)
                { if (buffer.Length + n > limit) throw new HelperException("An SPT response or file is too large."); buffer.Write(chunk, 0, n); }
                using (var reader = new StreamReader(new MemoryStream(buffer.ToArray()), new UTF8Encoding(false, true), true)) return reader.ReadToEnd();
            }
        }

        internal static string Hash(byte[] bytes)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}
