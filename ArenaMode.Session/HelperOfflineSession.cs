// Linked into offline checks. Keep compatible with net472/C# 7.3.
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ArenaMode.Shared.Session;

namespace ArenaMode.SessionHelper
{
    /// <summary>Exclusive offline handoff. Transports opaque authority; never writes either profile.</summary>
    internal static class HelperOfflineSession
    {
        internal const string LeaseFile = "arena-offline-session.json";
        internal const string Route = "/arenamode/session/enter-stop";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr window, string text, string caption, uint flags);

        internal sealed class Grant
        {
            internal string SessionId, ProfileId, Token, Source, Pin;
            internal Uri Origin;
            internal int ServerPid;
            internal DateTime ServerStarted;
            internal List<string> Arguments;
        }

        internal sealed class Lease
        {
            internal string SessionId, ProfileId, Source, Private, MarkerCreated, Operation, Hash;
            internal int Port, NormalPid, HelperPid;
            internal DateTime NormalStarted, HelperStarted;
            internal List<string> Arguments;
            internal byte[] Bytes;
        }

        internal sealed class Entry : IDisposable
        {
            internal Grant Grant;
            internal Lease Lease;
            internal string Private, MarkerCreated, Operation;
            internal int Port, HelperPid;
            internal DateTime HelperStarted;
            internal FileStream Startup;
            internal bool ReadyStopped;
            internal bool CanResumeNormal;

            internal static Entry Open(HelperRequest request, string tree, HelperHttpEndpoint normal, DateTime created, int port)
            {
                Grant grant = ReadGrant(request, normal, DateTime.UtcNow);
                var entry = new Entry { Grant = grant, Private = tree, MarkerCreated = MarkerTime(created), Port = port,
                    Operation = Guid.NewGuid().ToString("N") };
                using (Process helper = Process.GetCurrentProcess())
                {
                    entry.HelperPid = helper.Id;
                    if (!HelperProcesses.TryGetStartTimeUtc(helper, out entry.HelperStarted)) throw Refused();
                }
                entry.Startup = AcquireStartup(grant.Source);
                return entry;
            }

            internal void StopNormal(HelperHttpEndpoint normal, Action<string> log)
            {
                using (Process server = HelperProcesses.TryOpen(Grant.ServerPid))
                {
                    RequireProcess(server, Grant.ServerPid, Grant.ServerStarted, Path.Combine(Grant.Source, HelperLayout.ServerExeName));
                    RequireListener(server, normal.ServerHost, normal.ServerPort);
                    log("Saving and stopping normal SPT before the Arena profile is copied.");
                    CanResumeNormal = CompleteStop(() => Send(this), () => RequireReady(), milliseconds => server.WaitForExit(milliseconds),
                        () => MessageBox(IntPtr.Zero,
                            "Arena is waiting for SPT to finish saving.\n\nKeep the server open. Retry, or cancel and check helper.log.",
                            "ArenaMode - entry paused", 0x50035) == 4, log);
                    if (CanResumeNormal)
                        throw new HelperException("Arena entry was declined; the normal server confirmed it can continue.");
                    ReadyStopped = true;
                }
            }

            internal void RequireReady()
            {
                var lease = ReadOriginal(Grant.Source, Grant.SessionId);
                RequireEntryBinding(lease, Grant, Private, MarkerCreated, Port, Operation, HelperPid, HelperStarted);
                RequireActive(lease);
                Lease = lease;
            }

            internal void RequireNormalAvailable(HelperHttpEndpoint normal)
            {
                if (!CanResumeNormal || ReadyStopped || Lease != null) throw Refused();
                using (Process server = HelperProcesses.TryOpen(Grant.ServerPid))
                {
                    RequireProcess(server, Grant.ServerPid, Grant.ServerStarted, Path.Combine(Grant.Source, HelperLayout.ServerExeName));
                    RequireListener(server, normal.ServerHost, normal.ServerPort);
                }
                // A refused transition must not have published or activated a private session.
                if (Exists(ActivePath(Grant.Source))
                    || Exists(Path.Combine(Grant.Source, "user", "arena-rewards", "offline-sessions", Grant.SessionId, "lease.json"))) throw Refused();
            }

            internal void RequireBuild(HelperRequest request, string tree, int port, DateTime created)
            {
                if (Startup == null || !ReadyStopped || request.Schema != ArenaSessionContract.OfflineRequestSchema
                    || request.OfflineSessionId != Grant.SessionId || request.ProfileId != Grant.ProfileId
                    || !HelperPathSafety.PathsEqual(request.SptRuntimeDir, Grant.Source)
                    || !HelperPathSafety.PathsEqual(tree, Private) || MarkerTime(created) != MarkerCreated || port != Port)
                    throw Refused();
                RequireReady();
                RequireGone(Grant.ServerPid, Grant.ServerStarted, Path.Combine(Grant.Source, HelperLayout.ServerExeName));
            }

            internal void CopyLease(HelperMarker marker)
            {
                RequireReady();
                RequireMarker(Lease, marker);
                string path = Path.Combine(Private, LeaseFile);
                HelperRewardControlTransfer.RequirePlainPath(path, false);
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { stream.Write(Lease.Bytes, 0, Lease.Bytes.Length); stream.Flush(true); }
                if (!Same(Lease.Bytes, Read(path))) throw Refused();
            }

            internal void ConfirmActivation(Process child)
            {
                RequireReady();
                DateTime started;
                if (child == null || !HelperProcesses.TryGetStartTimeUtc(child, out started)) throw Refused();
                RequireProcess(child, child.Id, started, Path.Combine(Private, HelperLayout.ServerExeName));
                ValidateActivation(Read(Path.Combine(SessionDirectory(Lease), "activation.json")), Lease, child.Id, started);
                var active = Reference(Read(ActivePath(Lease.Source)));
                if (!active.ContainsKey("activated") || !Bool(active, "activated")) throw Refused();
                if (!Same(Lease.Bytes, Read(Path.Combine(Private, LeaseFile)))) throw Refused();
            }

            public void Dispose()
            { if (Startup != null) { Startup.Dispose(); Startup = null; } }
        }

        // Exit after an HTTP loss is insufficient: final native readback must have
        // published matching immutable Ready evidence. A held process is never killed.
        // True means an explicit safe refusal, not permission to build/copy. False means Ready + exit.
        internal static bool CompleteStop(Func<Dictionary<string, object>> send, Action requireReady,
            Func<int, bool> awaitExit, Func<bool> retry, Action<string> log)
        {
            while (true)
            {
                Dictionary<string, object> reply = null;
                try
                {
                    reply = send();
                    if (reply != null) { Bool(reply, "accepted"); Bool(reply, "held"); }
                }
                catch (Exception ex)
                {
                    reply = null;
                    log("Arena entry reply could not be confirmed (" + TransportFailure(ex) + ").");
                }
                if (reply != null && !Bool(reply, "accepted") && !Bool(reply, "held"))
                {
                    log("Arena entry declined: " + DescribeReply(reply));
                    if (AllowsNormalResume(reply)) return true;
                    throw new HelperException("Normal SPT refused Arena entry; no profile was copied.");
                }
                try
                {
                    requireReady();
                    if (awaitExit(reply != null && Bool(reply, "accepted") ? 60000 : 3000)) return false;
                }
                catch (Exception) { }
                log("Arena entry paused; preserving the original server and saved handoff evidence.");
                if (!retry()) throw new HelperException("Arena entry paused. The original server was not force-closed and no profile was copied.");
            }
        }

        internal static bool AllowsNormalResume(Dictionary<string, object> reply)
        {
            object accepted, held, resume, reason;
            return reply != null && reply.TryGetValue("accepted", out accepted) && accepted is bool && !(bool)accepted
                && reply.TryGetValue("held", out held) && held is bool && !(bool)held
                && reply.TryGetValue("canResumeNormal", out resume) && resume is bool && (bool)resume
                && reply.TryGetValue("reason", out reason) && reason is string && (string)reason == "return-not-ready";
        }

        internal static string DescribeReply(Dictionary<string, object> reply)
        {
            string stage = ArenaMiniJson.GetString(reply, "stage");
            switch (stage)
            {
                case "http-drain": case "update-drain": case "idle": case "preflight-save":
                case "stop-updates": case "final-save": break;
                default: stage = "unknown-stage"; break;
            }
            string detail = ArenaMiniJson.GetString(reply, "detail");
            switch (detail)
            {
                case "OperationCanceledException": case "TaskCanceledException": case "TimeoutException": detail = "timeout"; break;
                case "IOException": case "UnauthorizedAccessException": detail = "file-access"; break;
                case "return-session-not-idle": detail = "session-busy"; break;
                case "return-profile-save-not-confirmed": detail = "save-not-confirmed"; break;
                default: detail = "see-server-log"; break;
            }
            int requests = ArenaMiniJson.GetInt(reply, "activeRequests", -1);
            int updates = ArenaMiniJson.GetInt(reply, "activeUpdates", -1);
            return stage + "/" + detail + "; active requests=" + Count(requests) + "; active updates=" + Count(updates)
                + "; normal resume=" + AllowsNormalResume(reply);
        }

        private static string Count(int value) => value >= 0 && value <= 1000000 ? value.ToString(CultureInfo.InvariantCulture) : "unknown";
        private static string TransportFailure(Exception ex) => ex is WebException web ? web.Status.ToString()
            : ex is IOException ? "file-or-network-io" : "invalid-or-unavailable-response";

        internal static Grant ReadGrant(HelperRequest request, HelperHttpEndpoint normal, DateTime now)
        {
            if (request.Schema != ArenaSessionContract.OfflineRequestSchema || !request.IsEnter) throw Refused();
            var grant = ParseGrant(Read(Path.Combine(request.SptRuntimeDir, "user", "arena-rewards", request.ProfileId, "entry.json")),
                request.SptRuntimeDir, request.ProfileId, request.OfflineSessionId, now);
            HelperRewardControlTransfer.RequirePlainPath(grant.Source, true);
            if (Exists(Path.Combine(grant.Source, ArenaSessionContract.MarkerFileName)) || grant.Origin.Port != normal.ServerPort)
                throw Refused();
            return grant;
        }

        internal static Grant ParseGrant(byte[] bytes, string source, string profile, string session, DateTime now)
        {
            var data = Parse(bytes, 13);
            if (Int(data, "schema") != 1 || Text(data, "purpose") != "arena-entry"
                || !Hex(profile, 24) || !Hex(session, 32) || Text(data, "profileId") != profile || Text(data, "sessionId") != session
                || !HelperPathSafety.IsDriveAbsolute(source) || !HelperPathSafety.PathsEqual(Text(data, "runtimeRoot"), source)) throw Refused();
            string token = Text(data, "token"), pin = Text(data, "certificateSha256"), originText = Text(data, "originUrl");
            Secret(token); if (!Hex(pin, 64)) throw Refused();
            Uri origin;
            if (!Uri.TryCreate(originText, UriKind.Absolute, out origin) || origin.Scheme != "https" || origin.Port < 1
                || !(originText == "https://127.0.0.1:" + origin.Port.ToString(CultureInfo.InvariantCulture)
                    || originText == "https://[::1]:" + origin.Port.ToString(CultureInfo.InvariantCulture))) throw Refused();
            DateTime issued = Utc(data, "issuedUtc"), expires = Utc(data, "expiresUtc"), started = Utc(data, "serverStartedUtc");
            if (issued > now.ToUniversalTime().AddMinutes(1) || expires <= now.ToUniversalTime() || expires <= issued
                || expires > issued.AddHours(12) || started > issued || Int(data, "serverPid") <= 0) throw Refused();
            return new Grant { SessionId = session, ProfileId = profile, Source = source, Token = token, Pin = pin, Origin = origin,
                ServerPid = Int(data, "serverPid"), ServerStarted = started, Arguments = Arguments(data, "serverArguments") };
        }

        internal static Lease ParseLease(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 32768) throw Refused();
            var data = Parse(bytes, 17);
            string source = Text(data, "runtimeRoot"), target = Text(data, "privateRoot");
            if (Int(data, "schema") != 1 || !Hex(Text(data, "sessionId"), 32) || !Hex(Text(data, "profileId"), 24)
                || !Hex(Text(data, "entryOperationId"), 32) || !HelperPathSafety.IsDriveAbsolute(source)
                || !HelperPathSafety.IsDriveAbsolute(target) || HelperPathSafety.IsSameOrUnder(source, target)
                || HelperPathSafety.IsSameOrUnder(target, source) || Int(data, "port") < 1 || Int(data, "port") > 65535
                || Int(data, "normalServerPid") <= 0 || Int(data, "entryHelperPid") <= 0) throw Refused();
            Utc(data, "markerCreatedUtc"); Bool(data, "enabled"); Secret(Text(data, "controlKey"));
            object revision; decimal parsed;
            if (!data.TryGetValue("consentRevision", out revision)
                || !decimal.TryParse(Convert.ToString(revision, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                || parsed < 0 || parsed > long.MaxValue) throw Refused();
            // Authority remains opaque to this helper. Server core validates its signature,
            // enrollment binding and progression; helper never accepts an amount from it.
            if (!data.ContainsKey("authority") || data["authority"] != null && !(data["authority"] is Dictionary<string, object>)) throw Refused();
            return new Lease { SessionId = Text(data, "sessionId"), ProfileId = Text(data, "profileId"), Source = source, Private = target,
                MarkerCreated = Text(data, "markerCreatedUtc"), Port = Int(data, "port"), Operation = Text(data, "entryOperationId"),
                NormalPid = Int(data, "normalServerPid"), NormalStarted = Utc(data, "normalServerStartedUtc"),
                HelperPid = Int(data, "entryHelperPid"), HelperStarted = Utc(data, "entryHelperStartedUtc"),
                Arguments = Arguments(data, "normalServerArguments"), Bytes = bytes, Hash = Hash(bytes) };
        }

        internal static void RequireEntryBinding(Lease lease, Grant grant, string tree, string markerTime, int port,
            string operation, int helperPid, DateTime helperStarted)
        {
            if (lease.SessionId != grant.SessionId || lease.ProfileId != grant.ProfileId
                || !HelperPathSafety.PathsEqual(lease.Source, grant.Source) || !HelperPathSafety.PathsEqual(lease.Private, tree)
                || lease.MarkerCreated != markerTime || lease.Port != port || lease.Operation != operation
                || lease.HelperPid != helperPid || lease.HelperStarted != helperStarted || lease.NormalPid != grant.ServerPid
                || lease.NormalStarted != grant.ServerStarted || !SameArguments(lease.Arguments, grant.Arguments)) throw Refused();
        }

        internal static Lease ReadOriginal(string source, string session)
        {
            if (!Hex(session, 32)) throw Refused();
            var lease = ParseLease(Read(Path.Combine(source, "user", "arena-rewards", "offline-sessions", session, "lease.json")));
            if (lease.SessionId != session || !HelperPathSafety.PathsEqual(lease.Source, source)) throw Refused();
            return lease;
        }

        internal static Lease ReadPrivate(string tree, HelperRequest request = null)
        {
            var lease = ParseLease(Read(Path.Combine(tree, LeaseFile)));
            if (!HelperPathSafety.PathsEqual(lease.Private, tree) || !Same(lease.Bytes, ReadOriginal(lease.Source, lease.SessionId).Bytes)) throw Refused();
            HelperMarker marker; string error;
            if (!HelperMarker.TryParse(Utf8.GetString(Read(Path.Combine(tree, ArenaSessionContract.MarkerFileName))), out marker, out error)) throw Refused();
            RequireMarker(lease, marker);
            if (request != null && (!HelperPathSafety.PathsEqual(request.SptRuntimeDir, lease.Source)
                || request.ProfileId != lease.ProfileId || request.OfflineSessionId != null && request.OfflineSessionId != lease.SessionId)) throw Refused();
            return lease;
        }

        internal static bool HasOfflineTree(string tree)
        {
            // Detection is read-only and must not impose the offline no-links
            // policy on legacy sessions (whose canonical image checks support
            // junction/SUBST spellings). Once evidence exists, ReadPrivate and
            // every authority read still require ordinary, unlinked paths.
            FileAttributes attributes;
            if (HelperFileSystem.TryGetAttributes(Path.Combine(tree, LeaseFile), out attributes)) return true;
            string path = Path.Combine(tree, ArenaSessionContract.MarkerFileName);
            if (!HelperFileSystem.TryGetAttributes(path, out attributes)) return false;
            object value; string error;
            if (!ArenaMiniJson.TryParse(Utf8.GetString(Read(path, 32768, false)), out value, out error)) return false;
            var data = ArenaMiniJson.AsObject(value);
            if (data == null) return false;
            object id; return data.TryGetValue("offlineSessionId", out id) && id != null;
        }

        internal static FileStream AcquireStartup(string source)
        {
            string directory = Path.Combine(source, "user", "arena-rewards");
            HelperRewardControlTransfer.RequirePlainPath(directory, true);
            string path = Path.Combine(directory, ".startup.lock");
            var clock = Stopwatch.StartNew();
            while (true)
            {
                HelperRewardControlTransfer.RequirePlainPath(path, false);
                try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { if (clock.ElapsedMilliseconds >= 125000) throw new HelperException("Normal SPT startup coordination is busy; the Arena session was retained."); }
                Thread.Sleep(25);
            }
        }

        internal static void RequireActive(Lease lease)
        {
            var reference = Reference(Read(ActivePath(lease.Source)));
            if (Int(reference, "schema") != 1 || Text(reference, "sessionId") != lease.SessionId
                || Text(reference, "leaseHash") != lease.Hash || Exists(Path.Combine(SessionDirectory(lease), "closed.json"))) throw Refused();
        }

        internal static void ValidateActivation(byte[] bytes, Lease lease, int pid, DateTime started)
        {
            var activation = Parse(bytes, 5);
            if (Int(activation, "schema") != 1 || Text(activation, "sessionId") != lease.SessionId
                || Text(activation, "leaseHash") != lease.Hash || Int(activation, "privatePid") != pid
                || Utc(activation, "privateStartedUtc") != started) throw Refused();
        }

        internal static bool RequireClosed(Lease lease)
        {
            byte[] closureBytes = Read(Path.Combine(SessionDirectory(lease), "closed.json"), 524288);
            var closure = Parse(closureBytes, 6);
            if (Int(closure, "schema") != 1 || Text(closure, "sessionId") != lease.SessionId || Text(closure, "leaseHash") != lease.Hash) throw Refused();
            var completion = Parse(Read(Path.Combine(SessionDirectory(lease), "completion.json")), 3);
            if (Int(completion, "schema") != 1 || Text(completion, "sessionId") != lease.SessionId
                || Text(completion, "closureHash") != Hash(closureBytes)) throw Refused();
            // A newer session may already have a Ready pointer when the old tree is rebuilt.
            if (Exists(ActivePath(lease.Source)))
            {
                var active = Reference(Read(ActivePath(lease.Source)));
                if (Text(active, "sessionId") == lease.SessionId) throw Refused();
            }
            return Bool(closure, "activated");
        }

        internal static void RequireClosedBeforeRebuild(string runtimeRoot)
        {
            string tree = HelperServerTree.GetTreePath(runtimeRoot);
            if (!HasOfflineTree(tree)) return;
            var lease = ParseLease(Read(Path.Combine(tree, LeaseFile)));
            if (!HelperPathSafety.PathsEqual(lease.Private, tree) || !Same(lease.Bytes, ReadOriginal(lease.Source, lease.SessionId).Bytes)) throw Refused();
            bool activated = RequireClosed(lease);
            // A stopped, never-activated failed Build may not yet have a marker.
            if (activated || Exists(Path.Combine(tree, ArenaSessionContract.MarkerFileName))) ReadPrivate(tree);
            HelperRewardTransfer.RequireDrainedBeforeRebuild(runtimeRoot);
        }

        internal static void RequirePrivateAbsent(Lease lease)
        {
            string expected = Path.Combine(lease.Private, HelperLayout.ServerExeName);
            var candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(expected));
            try
            {
                foreach (Process candidate in candidates)
                {
                    if (candidate.HasExited) continue;
                    string image = HelperProcesses.GetImagePath(candidate);
                    if (image == null || HelperProcesses.SameFileSystemObject(image, expected)) throw Refused();
                }
            }
            finally { foreach (Process candidate in candidates) candidate.Dispose(); }
        }

        internal static void StartNormal(Lease lease, HelperHttpEndpoint normal,
            Func<string, string, string, Process> start, Action<string, int, Process, string, bool> wait, Action<string> log)
        {
            // Caller has already released .startup.lock, so ordinary initialization can
            // close the lease and import the final signed state before loading profiles.
            RequirePrivateAbsent(lease);
            RequireGone(lease.NormalPid, lease.NormalStarted, Path.Combine(lease.Source, HelperLayout.ServerExeName));
            string exe = Path.Combine(lease.Source, HelperLayout.ServerExeName);
            Process replacement = null;
            try
            {
                IPAddress reached;
                if (HelperProcesses.CanConnect(normal.ServerHost, normal.ServerPort, 1000, out reached))
                {
                    var ids = HelperProcesses.GetListenerProcessIds(normal.ServerPort, reached);
                    if (ids == null || ids.Count != 1 || !IPAddress.IsLoopback(reached)) throw Refused();
                    replacement = HelperProcesses.TryOpen(ids[0]);
                    DateTime started;
                    if (replacement == null || !HelperProcesses.TryGetStartTimeUtc(replacement, out started)) throw Refused();
                    RequireProcess(replacement, ids[0], started, exe);
                }
                else
                {
                    if (!HelperProcesses.IsPortFree(normal.ServerHost, normal.ServerPort)) throw Refused();
                    log("Restoring normal SPT and applying the completed Arena session.");
                    replacement = start(exe, HelperCommandLine.BuildCommandLine(lease.Arguments), lease.Source);
                    if (replacement == null) throw Refused();
                }
                wait(normal.ServerHost, normal.ServerPort, replacement, "the normal server after Arena", false);
                // Listening with a failed mod initialization must never be mistaken for
                // successful import. Native startup must retire this session first.
                RequireClosed(lease);
                if (Exists(ActivePath(lease.Source))) throw Refused();
            }
            finally { if (replacement != null) replacement.Dispose(); }
        }

        private static Dictionary<string, object> Send(Entry entry)
        {
            var request = (HttpWebRequest)WebRequest.Create(new Uri(entry.Grant.Origin, Route));
            request.Method = "POST"; request.ContentType = "application/json";
            request.Proxy = null; request.AllowAutoRedirect = false; request.KeepAlive = false;
            request.Timeout = 90000; request.ReadWriteTimeout = 90000;
            request.Headers[HttpRequestHeader.Cookie] = "PHPSESSID=" + entry.Grant.ProfileId;
            request.ServerCertificateValidationCallback = (_, cert, chain, errors) => cert != null && Hash(cert.GetRawCertData()) == entry.Grant.Pin;
            byte[] body = Utf8.GetBytes(ArenaMiniJson.Write(new Dictionary<string, object> {
                { "schema", 1 }, { "sessionId", entry.Grant.SessionId }, { "token", entry.Grant.Token }, { "operationId", entry.Operation },
                { "helperPid", entry.HelperPid }, { "helperStartedUtc", entry.HelperStarted.ToString("o", CultureInfo.InvariantCulture) },
                { "privateRoot", entry.Private }, { "markerCreatedUtc", entry.MarkerCreated }, { "port", entry.Port }
            }, false));
            request.ContentLength = body.Length;
            using (var stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
            using (var response = request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var buffer = new MemoryStream())
            {
                byte[] chunk = new byte[1024]; int count;
                while ((count = stream.Read(chunk, 0, chunk.Length)) > 0)
                { if (buffer.Length + count > 4096) throw Refused(); buffer.Write(chunk, 0, count); }
                var reply = Parse(buffer.ToArray(), -1); Bool(reply, "accepted"); Bool(reply, "held");
                return reply;
            }
        }

        private static void RequireMarker(Lease lease, HelperMarker marker)
        {
            if (marker == null || marker.Schema != 1 || marker.OfflineSessionId != lease.SessionId || marker.ProfileId != lease.ProfileId
                || marker.CreatedUtc != lease.MarkerCreated || marker.Port != lease.Port
                || !HelperPathSafety.PathsEqual(marker.SourceRuntime, lease.Source)
                || !HelperPathSafety.PathsEqual(marker.ServerExe, Path.Combine(lease.Private, HelperLayout.ServerExeName))) throw Refused();
        }
        private static void RequireProcess(Process process, int pid, DateTime started, string image)
        {
            DateTime actual;
            if (process == null || process.Id != pid || process.HasExited || !HelperProcesses.TryGetStartTimeUtc(process, out actual)
                || actual != started || !HelperProcesses.SameFileSystemObject(HelperProcesses.GetImagePath(process), image)) throw Refused();
        }
        private static void RequireGone(int pid, DateTime started, string image)
        {
            using (Process process = HelperProcesses.TryOpen(pid))
            {
                if (process == null || process.HasExited) return;
                DateTime actual;
                if (!HelperProcesses.TryGetStartTimeUtc(process, out actual)) throw Refused();
                if (actual == started) throw Refused(); // PID reuse is harmless only with a different start time.
            }
        }
        private static void RequireListener(Process process, string host, int port)
        {
            IPAddress reached;
            if (!HelperProcesses.CanConnect(host, port, 1000, out reached) || !IPAddress.IsLoopback(reached)) throw Refused();
            var owners = HelperProcesses.GetListenerProcessIds(port, reached);
            if (owners == null || owners.Count != 1 || owners[0] != process.Id) throw Refused();
        }
        internal static string MarkerTime(DateTime time) => time.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        internal static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        internal static string ActivePath(string source) => Path.Combine(source, "user", "arena-rewards", "offline-active.json");
        private static string SessionDirectory(Lease lease) => Path.Combine(lease.Source, "user", "arena-rewards", "offline-sessions", lease.SessionId);
        private static bool Exists(string path) { HelperRewardControlTransfer.RequirePlainPath(path, false); FileAttributes attributes; return HelperFileSystem.TryGetAttributes(path, out attributes); }
        private static byte[] Read(string path, int maximum = 32768, bool requirePlain = true)
        {
            if (requirePlain) HelperRewardControlTransfer.RequirePlainPath(path, true);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length <= 0 || stream.Length > maximum) throw Refused();
                byte[] bytes = new byte[(int)stream.Length]; int offset = 0, count;
                while (offset < bytes.Length && (count = stream.Read(bytes, offset, bytes.Length - offset)) > 0) offset += count;
                if (offset != bytes.Length || stream.ReadByte() != -1) throw Refused(); return bytes;
            }
        }
        private static Dictionary<string, object> Parse(byte[] bytes, int count)
        {
            object value; string error;
            if (bytes == null || bytes.Length == 0 || !ArenaMiniJson.TryParse(Utf8.GetString(bytes), out value, out error)) throw Refused();
            var data = ArenaMiniJson.AsObject(value);
            if (data == null || count >= 0 && data.Count != count) throw Refused(); return data;
        }
        private static Dictionary<string, object> Reference(byte[] bytes)
        {
            var data = Parse(bytes, -1);
            if (data.Count != 3 && data.Count != 4 || Int(data, "schema") != 1
                || !Hex(Text(data, "sessionId"), 32) || !Hex(Text(data, "leaseHash"), 64)) throw Refused();
            if (data.Count == 4) Bool(data, "activated");
            return data;
        }
        private static string Text(Dictionary<string, object> data, string key) { string text = ArenaMiniJson.GetString(data, key); if (text == null) throw Refused(); return text; }
        private static int Int(Dictionary<string, object> data, string key) { int value = ArenaMiniJson.GetInt(data, key, int.MinValue); if (value == int.MinValue) throw Refused(); return value; }
        private static bool Bool(Dictionary<string, object> data, string key) { object value; if (!data.TryGetValue(key, out value) || !(value is bool)) throw Refused(); return (bool)value; }
        private static void Secret(string value) { byte[] bytes = Convert.FromBase64String(value); if (bytes.Length != 32 || Convert.ToBase64String(bytes) != value) throw Refused(); }
        private static DateTime Utc(Dictionary<string, object> data, string key)
        {
            string text = Text(data, key); DateTimeOffset parsed;
            if (!(text.EndsWith("Z", StringComparison.Ordinal) || text.EndsWith("+00:00", StringComparison.Ordinal))
                || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) throw Refused();
            return parsed.UtcDateTime;
        }
        private static List<string> Arguments(Dictionary<string, object> data, string key)
        {
            var input = ArenaMiniJson.GetArray(data, key); var output = new List<string>(); int length = 0;
            if (input == null || input.Count > 32) throw Refused();
            foreach (object value in input)
            {
                string arg = value as string;
                if (arg == null || arg.IndexOf('\0') >= 0 || (length += arg.Length) > 4096) throw Refused(); output.Add(arg);
            }
            return output;
        }
        private static bool SameArguments(List<string> left, List<string> right)
        { if (left.Count != right.Count) return false; for (int i = 0; i < left.Count; i++) if (left[i] != right[i]) return false; return true; }
        private static bool Same(byte[] left, byte[] right)
        { if (left.Length != right.Length) return false; for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false; return true; }
        private static bool Hex(string text, int length)
        { if (text == null || text.Length != length) return false; foreach (char c in text) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false; return true; }
        private static HelperException Refused() => new HelperException("Offline Arena session proof is missing or changed; the session and profiles were retained for safe recovery.");
    }
}
