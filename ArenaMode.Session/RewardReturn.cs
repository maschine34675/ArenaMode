// Also linked into offline checks. Keep compatible with net472/C# 7.3.
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using ArenaMode.Shared.Session;

namespace ArenaMode.SessionHelper
{
    /// <summary>The normal server grants one local, authenticated graceful return. No profile writes or process kills.</summary>
    internal static class HelperRewardReturn
    {
        internal const string FileName = "arena-rewards-return.json";
        internal const string Route = "/arenamode/rewards/private-return";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr window, string text, string caption, uint flags);

        internal sealed class Grant
        {
            internal string ControlId, ProfileId, Token, RuntimeRoot, CertificateSha256;
            internal Uri Origin;
            internal int ServerPid;
            internal DateTime ServerStartedUtc;
            internal List<string> ServerArguments;
        }

        internal static bool TryCopy(string source, string profileId, string controlId, string tree,
            HelperMarker marker, DateTime now, Action<string> log)
        {
            string temporary = null;
            try
            {
                string target = Path.Combine(tree, FileName);
                HelperRewardControlTransfer.RequirePlainPath(target, false);
                if (File.Exists(target)) File.Delete(target);
                string path = Path.Combine(source, "user", "arena-rewards", profileId, "return.json");
                if (!File.Exists(path)) return false; // Old/remote servers retain the previous return behavior.
                byte[] bytes = HelperRewardControlTransfer.ReadBounded(path);
                Parse(bytes, source, profileId, controlId, now);
                if (marker == null || marker.RewardControlId != controlId || marker.ProfileId != profileId
                    || !HelperPathSafety.PathsEqual(marker.SourceRuntime, source)
                    || !HelperPathSafety.PathsEqual(marker.ServerExe, Path.Combine(tree, HelperLayout.ServerExeName)))
                    throw new InvalidDataException();
                HelperRewardControlTransfer.RequireMarker(Path.Combine(tree, ArenaSessionContract.MarkerFileName), marker.ToJson());
                temporary = Path.Combine(tree, ".arena-return-" + Guid.NewGuid().ToString("N") + ".tmp");
                HelperRewardControlTransfer.RequirePlainPath(temporary, false);
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
                HelperRewardControlTransfer.RequireMarker(Path.Combine(tree, ArenaSessionContract.MarkerFileName), marker.ToJson());
                File.Move(temporary, target);
                return true;
            }
            catch (Exception)
            {
                if (log != null) log("Automatic reward settlement on return is unavailable; the normal session remains usable.");
                return false;
            }
            finally
            {
                if (temporary != null)
                    try { HelperRewardControlTransfer.RequirePlainPath(temporary, false); if (File.Exists(temporary)) File.Delete(temporary); }
                    catch (Exception) { }
            }
        }

        internal static Grant Parse(byte[] bytes, string source, string profileId, string controlId, DateTime now)
        {
            object raw; string error;
            if (bytes == null || bytes.Length == 0 || bytes.Length > HelperRewardControlTransfer.MaximumBytes
                || !ArenaMiniJson.TryParse(Utf8.GetString(bytes), out raw, out error)) throw new InvalidDataException();
            var data = ArenaMiniJson.AsObject(raw);
            if (data == null || data.Count != 12 || ArenaMiniJson.GetInt(data, "schema", -1) != 1
                || !Hex(profileId, 24) || !Hex(controlId, 32)
                || ArenaMiniJson.GetString(data, "profileId") != profileId || ArenaMiniJson.GetString(data, "controlId") != controlId
                || !HelperPathSafety.PathsEqual(ArenaMiniJson.GetString(data, "runtimeRoot"), source)
                || !HelperPathSafety.IsDriveAbsolute(source)) throw new InvalidDataException();
            string token = ArenaMiniJson.GetString(data, "token");
            byte[] secret = Convert.FromBase64String(token ?? "");
            string pin = ArenaMiniJson.GetString(data, "certificateSha256");
            if (secret.Length != 32 || Convert.ToBase64String(secret) != token || !Hex(pin, 64)) throw new InvalidDataException();
            Uri origin;
            string originText = ArenaMiniJson.GetString(data, "originUrl");
            if (!Uri.TryCreate(originText, UriKind.Absolute, out origin) || origin.Scheme != "https"
                || origin.Port < 1 || origin.Port > 65535
                || !(originText == "https://127.0.0.1:" + origin.Port.ToString(CultureInfo.InvariantCulture)
                    || originText == "https://[::1]:" + origin.Port.ToString(CultureInfo.InvariantCulture))) throw new InvalidDataException();
            DateTime issued = Utc(data, "issuedUtc"), expires = Utc(data, "expiresUtc"), started = Utc(data, "serverStartedUtc");
            if (issued > now.ToUniversalTime().AddMinutes(1) || expires <= now.ToUniversalTime() || expires <= issued
                || expires > issued.AddHours(12) || started > issued) throw new InvalidDataException();
            int pid = ArenaMiniJson.GetInt(data, "serverPid", -1);
            object argumentValue;
            var arguments = data.TryGetValue("serverArguments", out argumentValue) ? argumentValue as List<object> : null;
            if (pid <= 0 || arguments == null || arguments.Count > 32) throw new InvalidDataException();
            var copied = new List<string>();
            int length = 0;
            foreach (object value in arguments)
            {
                var argument = value as string;
                if (argument == null || argument.IndexOf('\0') >= 0 || (length += argument.Length) > 4096) throw new InvalidDataException();
                copied.Add(argument);
            }
            return new Grant { ControlId = controlId, ProfileId = profileId, Token = token, RuntimeRoot = source,
                Origin = origin, CertificateSha256 = pin, ServerPid = pid, ServerStartedUtc = started, ServerArguments = copied };
        }

        // Returns true only if it restarted and waited for the normal server. A safe refusal returns false;
        // after an ambiguous/held shutdown, throw instead of launching a game against a suspended server.
        internal static bool TryRestart(string runtimeRoot, HelperRequest request, HelperHttpEndpoint normal,
            Func<string, string, string, Process> start, Action<string, int, Process, string, bool> wait, Action<string> log)
        {
            string tree = HelperServerTree.GetTreePath(runtimeRoot);
            string path = Path.Combine(tree, FileName);
            if (!File.Exists(path)) return false;
            Grant grant;
            try
            {
                HelperMarker marker; string error;
                if (!HelperMarker.TryParse(Utf8.GetString(HelperRewardControlTransfer.ReadBounded(Path.Combine(tree, ArenaSessionContract.MarkerFileName))), out marker, out error)
                    || marker == null || !marker.RunsFromTree || marker.ProfileId != request.ProfileId
                    || !HelperPathSafety.PathsEqual(marker.SourceRuntime, request.SptRuntimeDir)
                    || !HelperPathSafety.PathsEqual(marker.ServerExe, Path.Combine(tree, HelperLayout.ServerExeName))) throw new InvalidDataException();
                grant = Parse(HelperRewardControlTransfer.ReadBounded(path), request.SptRuntimeDir, request.ProfileId, marker.RewardControlId, DateTime.UtcNow);
                HelperRewardControlTransfer.RequirePlainPath(grant.RuntimeRoot, true);
                HelperRewardControlTransfer.RequirePlainPath(Path.Combine(grant.RuntimeRoot, ArenaSessionContract.MarkerFileName), false);
                if (File.Exists(Path.Combine(grant.RuntimeRoot, ArenaSessionContract.MarkerFileName))
                    || Directory.Exists(Path.Combine(grant.RuntimeRoot, ArenaSessionContract.MarkerFileName))
                    || grant.Origin.Port != normal.ServerPort) throw new InvalidDataException();
            }
            catch (Exception)
            {
                log("Automatic reward settlement could not verify this session; rewards remain queued for a normal server start.");
                return false;
            }

            Process server;
            try { server = Process.GetProcessById(grant.ServerPid); }
            catch (ArgumentException) { return false; }
            using (server)
            {
                string exe = Path.Combine(grant.RuntimeRoot, HelperLayout.ServerExeName);
                DateTime started;
                if (HelperProcesses.HasExited(server) || !HelperProcesses.TryGetStartTimeUtc(server, out started) || started != grant.ServerStartedUtc
                    || !HelperProcesses.SameFileSystemObject(HelperProcesses.GetImagePath(server), exe)) return false;
                IPAddress reached;
                if (!HelperProcesses.CanConnect(normal.ServerHost, normal.ServerPort, 1000, out reached) || !IPAddress.IsLoopback(reached)) return false;
                var owners = HelperProcesses.GetListenerProcessIds(normal.ServerPort, reached);
                if (owners == null || owners.Count != 1 || owners[0] != server.Id) return false;

                string operation = Guid.NewGuid().ToString("N");
                while (true)
                {
                    string failure = null;
                    try
                    {
                        var reply = Send(grant, operation);
                        if (ArenaMiniJson.GetBool(reply, "accepted", false)) break;
                        if (ArenaMiniJson.GetBool(reply, "held", true))
                            failure = "The normal SPT server could not safely finish saving. Your profile is still held in memory and your Arena rewards remain queued. Keep the server open.";
                        else
                        {
                            if (ArenaMiniJson.GetString(reply, "reason") != "no-pending-rewards")
                            {
                                log("The normal server deferred automatic reward settlement; returning without changing the queued rewards.");
                                MessageBox(IntPtr.Zero, "Arena could not safely restart the normal SPT server. Your rewards are saved and remain queued for its next start. The game will now return to the normal session.",
                                    "ArenaMode - rewards queued", 0x30);
                            }
                            return false;
                        }
                    }
                    catch (Exception)
                    {
                        // A lost response during Kestrel shutdown is safe only after this exact process exits.
                        if (server.WaitForExit(3000)) break;
                        failure = "The normal SPT server has not confirmed a safe return. Keep it open; your Arena rewards remain queued.";
                    }
                    log("Return paused; preserving the normal server and queued rewards.");
                    // This exceptional path must be visible after EFT closed. Retry keeps the same
                    // helper/process-bound operation; Cancel never force-closes the server.
                    if (MessageBox(IntPtr.Zero, failure + "\n\nRetry to check again, or Cancel and review the SPT server log. No profile will be replaced to force the return.",
                        "ArenaMode - return paused", 0x35) != 4) // MB_RETRYCANCEL | MB_ICONWARNING, IDRETRY
                        throw new HelperException("Return paused by the player; the normal server and queued rewards were retained.");
                }
                CompleteRestart(() => server.WaitForExit(60000),
                    () => HelperProcesses.IsPortFree(normal.ServerHost, normal.ServerPort),
                    () => start(exe, HelperCommandLine.BuildCommandLine(grant.ServerArguments), grant.RuntimeRoot),
                    // The reward path requires actual listener ownership; unlike legacy recovery,
                    // a previously free port alone is not enough if the TCP owner lookup fails.
                    replacement => wait(normal.ServerHost, normal.ServerPort, replacement, "the normal server after reward settlement", false),
                    () => MessageBox(IntPtr.Zero, "The normal SPT server is not ready yet. Retry to continue waiting or retry its start. Cancel leaves any running server open; you can start the game normally once it is ready.\n\nArena rewards remain protected against duplicate payment.",
                        "ArenaMode - return paused", 0x35) == 4, log);
                return true;
            }
        }

        internal static void CompleteRestart(Func<bool> awaitOriginalExit, Func<bool> portFree,
            Func<Process> start, Action<Process> awaitReady, Func<bool> retry, Action<string> log)
        {
            Process replacement = null;
            try
            {
                while (true)
                {
                    try
                    {
                        if (!awaitOriginalExit()) throw new HelperException("The normal server is still stopping. It was not force-closed.");
                        if (replacement == null || HelperProcesses.HasExited(replacement))
                        {
                            if (!portFree()) throw new HelperException("Another process is using the normal server port.");
                            if (replacement != null) replacement.Dispose();
                            replacement = null;
                            log("Applying Arena rewards: restarting the normal server before returning to the game.");
                            replacement = start();
                            if (replacement == null) throw new HelperException("The normal server could not be restarted.");
                        }
                        // A readiness timeout retains the actual process; retry never duplicates it.
                        awaitReady(replacement);
                        return;
                    }
                    catch (Exception)
                    {
                        log("Return paused while waiting for the normal server; no process was force-closed.");
                        if (!retry()) throw new HelperException("Return paused by the player while waiting for the normal server.");
                    }
                }
            }
            finally { if (replacement != null) replacement.Dispose(); }
        }

        private static Dictionary<string, object> Send(Grant grant, string operation)
        {
            var request = (HttpWebRequest)WebRequest.Create(new Uri(grant.Origin, Route));
            request.Method = "POST"; request.ContentType = "application/json";
            request.Proxy = null; request.AllowAutoRedirect = false; request.KeepAlive = false;
            request.Timeout = 90000; request.ReadWriteTimeout = 90000;
            request.Headers[HttpRequestHeader.Cookie] = "PHPSESSID=" + grant.ProfileId;
            request.ServerCertificateValidationCallback = (_, cert, chain, errors) =>
            {
                if (cert == null) return false;
                using (var sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(cert.GetRawCertData())).Replace("-", "").ToLowerInvariant() == grant.CertificateSha256;
            };
            using (Process helper = Process.GetCurrentProcess())
            {
                DateTime started;
                if (!HelperProcesses.TryGetStartTimeUtc(helper, out started)) throw new InvalidDataException();
                byte[] body = Utf8.GetBytes(ArenaMiniJson.Write(new Dictionary<string, object> {
                    { "schema", 1 }, { "controlId", grant.ControlId }, { "token", grant.Token }, { "operationId", operation },
                    { "helperPid", helper.Id }, { "helperStartedUtc", started.ToString("o", CultureInfo.InvariantCulture) }
                }, false));
                request.ContentLength = body.Length;
                using (var stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
            }
            using (var response = request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var buffer = new MemoryStream())
            {
                byte[] chunk = new byte[1024]; int count;
                while ((count = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + count > 4096) throw new InvalidDataException();
                    buffer.Write(chunk, 0, count);
                }
                object raw; string error;
                if (!ArenaMiniJson.TryParse(Utf8.GetString(buffer.ToArray()), out raw, out error)) throw new InvalidDataException();
                var reply = ArenaMiniJson.AsObject(raw);
                if (reply == null || !reply.ContainsKey("accepted") || !reply.ContainsKey("held")) throw new InvalidDataException();
                return reply;
            }
        }

        private static bool Hex(string text, int length)
        {
            if (text == null || text.Length != length) return false;
            foreach (char c in text) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            return true;
        }

        private static DateTime Utc(Dictionary<string, object> data, string key)
        {
            string value = ArenaMiniJson.GetString(data, key);
            DateTimeOffset parsed;
            if (value == null || !(value.EndsWith("Z", StringComparison.Ordinal) || value.EndsWith("+00:00", StringComparison.Ordinal))
                || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) throw new InvalidDataException();
            return parsed.UtcDateTime;
        }
    }
}
