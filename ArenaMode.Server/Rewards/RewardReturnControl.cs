using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SPTarkov.Server.Core.Servers;

namespace ArenaMode.Server.Rewards;

// A separate capability: possession of the consent token never authorizes shutdown.
internal sealed record RewardReturnGrant(int Schema, string ControlId, string ProfileId, string Token,
    string RuntimeRoot, string OriginUrl, string CertificateSha256, DateTimeOffset IssuedUtc, DateTimeOffset ExpiresUtc,
    int ServerPid, DateTimeOffset ServerStartedUtc, string[] ServerArguments);
internal sealed record RewardReturnRequest(int Schema, string ControlId, string Token, string OperationId,
    int HelperPid, DateTimeOffset HelperStartedUtc);
internal sealed record RewardReturnReply(bool Accepted, string Reason, bool Held = false,
    bool CanResumeNormal = false, string? Stage = null, string? Detail = null,
    int ActiveRequests = 0, int ActiveUpdates = 0);
internal sealed record RewardReturnProcess(int Pid, DateTimeOffset StartedUtc, string[] Arguments);

/// <summary>Process-local authority for one idle, local server return. Never pays or replaces a profile.</summary>
internal sealed class RewardReturnControl
{
    internal const string Route = "/arenamode/rewards/private-return";
    private readonly string _root;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<RewardReturnProcess?> _process;
    private readonly Func<RewardReturnRequest, bool> _helper;
    private readonly Dictionary<string, RewardReturnGrant> _grants = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private (string Profile, RewardReturnRequest Request)? _operation;

    internal RewardReturnControl(string root, Func<DateTimeOffset> now,
        Func<RewardReturnProcess?>? process = null, Func<RewardReturnRequest, bool>? helper = null)
    {
        _root = RewardStartupFiles.Canonical(root);
        _now = now;
        _process = process ?? (() => CurrentProcess(_root));
        _helper = helper ?? (request => MatchesHelper(_root, request));
    }

    internal bool Issue(RewardControlGrant consent)
    {
        lock (_sync)
        {
            if (_operation is not null || !RewardStartupFiles.MarkerAbsent(_root)) return false;
            RewardConsentControl.ValidateGrant(consent, _now());
            if (!RewardRuntimeFiles.SameRoot(consent.RuntimeRoot, _root)) return false;
            var process = _process();
            if (process is null || !SupportedArguments(process.Arguments)) return false;
            string config = Path.Combine(_root, "SPT_Data", "configs", "http.json");
            RewardStartupFiles.RequireLocalPath(_root, config);
            using var doc = JsonDocument.Parse(File.ReadAllText(config), new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!IPAddress.TryParse(doc.RootElement.GetProperty("ip").GetString(), out var ip) || !IPAddress.IsLoopback(ip)) return false;
            int port = doc.RootElement.GetProperty("port").GetInt32();
            if (new Uri(consent.OriginUrl).Port != port) return false;
            if (_grants.TryGetValue(consent.ProfileId, out var existing) && existing.ControlId == consent.ControlId
                && existing.ExpiresUtc > _now()) return true;
            var grant = new RewardReturnGrant(1, consent.ControlId, consent.ProfileId,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), _root, consent.OriginUrl,
                consent.CertificateSha256, consent.IssuedUtc, consent.ExpiresUtc,
                process.Pid, process.StartedUtc, process.Arguments.ToArray());
            var directory = RewardRuntimeFiles.DirectoryFor(_root, consent.ProfileId);
            RewardRuntimeFiles.EnsureDirectory(_root, directory);
            RewardRuntimeFiles.Write(_root, Path.Combine(directory, "return.json"), grant);
            _grants[consent.ProfileId] = grant;
            return true;
        }
    }

    internal bool HasGrant(string profileId)
    {
        lock (_sync)
        {
            try
            {
                if (!RewardStartupFiles.MarkerAbsent(_root) || !_grants.TryGetValue(profileId, out var grant)
                    || grant.ExpiresUtc <= _now() || grant.IssuedUtc > _now()) return false;
                var process = _process();
                return process is not null && process.Pid == grant.ServerPid && process.StartedUtc == grant.ServerStartedUtc
                    && process.Arguments.SequenceEqual(grant.ServerArguments);
            }
            catch { return false; }
        }
    }

    // Presentation only. The normal server's live status must ALSO confirm its process-local
    // grant; this file alone never authorizes a restart, capture, enrollment or payment.
    internal static bool PrivateGrantAvailable(string root, string profileId, DateTimeOffset? at = null)
    {
        try
        {
            string markerPath = Path.Combine(root, "arena-session.json");
            string grantPath = Path.Combine(root, "arena-rewards-return.json");
            RewardStartupFiles.RequireLocalPath(root, markerPath);
            RewardStartupFiles.RequireLocalPath(root, grantPath);
            var marker = RewardStartupFiles.Read<JsonElement>(markerPath, 16384);
            var grant = RewardStartupFiles.Read<RewardReturnGrant>(grantPath, 16384);
            var now = at ?? DateTimeOffset.UtcNow;
            if (marker.GetProperty("schema").GetInt32() != 1
                || marker.GetProperty("profileId").GetString() != profileId
                || marker.GetProperty("rewardControlId").GetString() != grant.ControlId
                || !RewardRuntimeFiles.SameRoot(marker.GetProperty("sourceRuntime").GetString()!, grant.RuntimeRoot)
                || !RewardRuntimeFiles.SameRoot(marker.GetProperty("serverExe").GetString()!, Path.Combine(root, "SPT.Server.exe"))
                || marker.GetProperty("port").GetInt32() is < 1 or > 65535
                || marker.GetProperty("port").GetInt32() == new Uri(grant.OriginUrl).Port
                || RewardRuntimeFiles.SameRoot(root, grant.RuntimeRoot)
                || grant.Schema != 1 || grant.ProfileId != profileId || !RewardIdentity.IsGuid(grant.ControlId)
                || !RewardIdentity.IsHex(profileId, 24) || grant.ServerPid <= 0
                || grant.ServerStartedUtc > grant.IssuedUtc || !SupportedArguments(grant.ServerArguments))
                return false;
            RewardConsentControl.ValidateGrant(new(1, grant.ControlId, grant.ProfileId, grant.Token, grant.RuntimeRoot,
                grant.OriginUrl, grant.CertificateSha256, grant.IssuedUtc, grant.ExpiresUtc), now);
            return true;
        }
        catch { return false; }
    }

    internal static bool SupportedArguments(string[]? arguments) => arguments is { Length: <= 32 }
        && arguments.All(argument => argument is not null && !argument.Contains('\0'))
        && arguments.Sum(argument => argument.Length) <= 4096;

    // Reserve only after all authentication checks. The exact same request can retry a held save;
    // a different helper/operation cannot take over a partially quiesced process.
    internal bool Authorize(string profileId, RewardReturnRequest request)
    {
        lock (_sync)
        {
            bool continuing = _operation is { } active && active.Profile == profileId && active.Request == request;
            if (!RewardStartupFiles.MarkerAbsent(_root) || request.Schema != 1
                || !RewardIdentity.IsGuid(request.OperationId) || request.HelperPid <= 0
                || !_grants.TryGetValue(profileId, out var grant) || grant.ControlId != request.ControlId
                || !continuing && (grant.ExpiresUtc <= _now() || grant.IssuedUtc > _now())
                || !SecretEquals(grant.Token, request.Token)) return false;
            var process = _process();
            if (process is null || process.Pid != grant.ServerPid || process.StartedUtc != grant.ServerStartedUtc
                || !process.Arguments.SequenceEqual(grant.ServerArguments) || !_helper(request)) return false;
            if (_operation is { } prior && (prior.Profile != profileId || prior.Request != request)) return false;
            _operation = (profileId, request);
            return true;
        }
    }

    internal void ReleaseBeforeQuiescence(string profileId, RewardReturnRequest request)
    {
        lock (_sync)
            if (_operation is { } current && current.Profile == profileId && current.Request == request)
                _operation = null;
    }

    private static bool SecretEquals(string expected, string supplied)
    {
        try
        {
            byte[] bytes = Convert.FromBase64String(supplied);
            return bytes.Length == 32 && CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(expected), bytes);
        }
        catch (Exception e) when (e is FormatException or ArgumentNullException) { return false; }
    }

    internal static RewardReturnProcess? CurrentProcess(string root)
    {
        if (!OperatingSystem.IsWindows() || !RewardReturnQuiescence.Enabled
            || typeof(SaveServer).Assembly.GetName().Version != new Version(4, 1, 6, 0)
            || Assembly.GetEntryAssembly()?.GetName().Name != "SPT.Server"
            || !RewardRuntimeFiles.SameRoot(root, AppContext.BaseDirectory)
            || !RewardRuntimeFiles.SameRoot(root, Directory.GetCurrentDirectory())
            || !ArenaRewardStartup.CurrentImageOwnsRoot(root, Environment.ProcessPath)
            || !string.Equals(Path.GetFileName(Environment.ProcessPath), "SPT.Server.exe", StringComparison.OrdinalIgnoreCase)) return null;
        using var current = Process.GetCurrentProcess();
        return new(current.Id, current.StartTime.ToUniversalTime(), Environment.GetCommandLineArgs().Skip(1).ToArray());
    }

    internal static bool MatchesHelper(string root, RewardReturnRequest request)
    {
        try
        {
            if (request.HelperPid <= 0 || request.HelperPid == Environment.ProcessId) return false;
            string installation = Path.GetDirectoryName(root)!;
            string expected = Path.Combine(installation, "BepInEx", "plugins", "maschine-ArenaMode", "maschine-ArenaMode.Session.exe");
            RewardStartupFiles.RequireLocalPath(installation, expected);
            using var helper = Process.GetProcessById(request.HelperPid);
            return !helper.HasExited && helper.StartTime.ToUniversalTime() == request.HelperStartedUtc.UtcDateTime
                && string.Equals(helper.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    internal static bool NoOtherGameOrServerProcesses()
    {
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch { return false; }
        try
        {
            foreach (var process in processes)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    string name = process.ProcessName;
                    if (name.Equals("EscapeFromTarkov", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("SPT.Server", StringComparison.OrdinalIgnoreCase))
                        if (!process.HasExited) return false;
                }
                catch (InvalidOperationException) { if (!process.HasExited) return false; }
                catch { return false; }
            }
            return true;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
