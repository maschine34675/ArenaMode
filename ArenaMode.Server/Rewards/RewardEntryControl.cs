using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ArenaMode.Shared.Session;

namespace ArenaMode.Server.Rewards;

// Only the authenticated installed helper receives this capability from disk.
// Consent and result-signing keys cannot authorize a server shutdown.
internal sealed record RewardEntryGrant(int Schema, string Purpose, string SessionId, string ProfileId, string Token,
    string RuntimeRoot, string OriginUrl, string CertificateSha256, DateTimeOffset IssuedUtc, DateTimeOffset ExpiresUtc,
    int ServerPid, DateTimeOffset ServerStartedUtc, string[] ServerArguments);
internal sealed record RewardEntryRequest(int Schema, string SessionId, string Token, string OperationId,
    int HelperPid, DateTimeOffset HelperStartedUtc, string PrivateRoot, string MarkerCreatedUtc, int Port);

/// <summary>Entry and return share one native stop sequence and therefore one operation owner.</summary>
internal sealed class RewardShutdownReservation
{
    private readonly object _sync = new();
    private (bool Entry, string Profile, object Request)? _owner;
    internal bool Reserve(bool entry, string profile, object request)
    {
        lock (_sync)
        {
            var candidate = (entry, profile, request);
            if (_owner is { } owner && !owner.Equals(candidate)) return false;
            _owner = candidate;
            return true;
        }
    }
    internal void Release(bool entry, string profile, object request)
    {
        lock (_sync)
            if (_owner is { } owner && owner.Equals((entry, profile, request))) _owner = null;
    }
}

internal sealed class RewardEntryControl
{
    internal const string Route = "/arenamode/session/enter-stop";
    internal const string Purpose = "arena-entry";
    private static readonly Lazy<RewardEntryControl> Instance = new(() => new(Directory.GetCurrentDirectory()));
    internal static RewardEntryControl Current => Instance.Value;
    private readonly string _root;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<RewardReturnProcess?> _process;
    private readonly Func<RewardEntryRequest, bool> _helper;
    private readonly Func<string> _privateRoot;
    private readonly Dictionary<string, RewardEntryGrant> _grants = new(StringComparer.Ordinal);
    private readonly HashSet<string> _issued = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private (string Profile, RewardEntryRequest Request)? _operation;

    internal RewardEntryControl(string root, Func<DateTimeOffset>? now = null,
        Func<RewardReturnProcess?>? process = null, Func<RewardEntryRequest, bool>? helper = null,
        Func<string>? privateRoot = null)
    {
        _root = RewardStartupFiles.Canonical(root);
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _process = process ?? (() => RewardReturnControl.CurrentProcess(_root));
        _helper = helper ?? (request => RewardReturnControl.MatchesHelper(_root,
            new(1, request.SessionId, request.Token, request.OperationId, request.HelperPid, request.HelperStartedUtc)));
        _privateRoot = privateRoot ?? (() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ArenaSessionContract.RuntimeParentFolderName, ArenaSessionContract.RuntimeRootFolderName,
            ArenaSessionContract.ServerTreeFolderName));
    }

    internal bool Issue(string profileId, string sessionId)
    {
        lock (_sync)
        {
            if (_operation is not null || !RewardStartupFiles.MarkerAbsent(_root)
                || !RewardIdentity.IsHex(profileId, 24) || !RewardIdentity.IsGuid(sessionId)) return false;
            if (_grants.TryGetValue(profileId, out var existing) && existing.SessionId == sessionId
                && existing.ExpiresUtc > _now()) return true;
            if (_issued.Contains(sessionId) || _issued.Count >= 4096) return false;
            var process = _process();
            if (process is null || !RewardReturnControl.SupportedArguments(process.Arguments)) return false;
            string config = Path.Combine(_root, "SPT_Data", "configs", "http.json");
            RewardStartupFiles.RequireLocalPath(_root, config);
            using var doc = JsonDocument.Parse(File.ReadAllText(config), new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!IPAddress.TryParse(doc.RootElement.GetProperty("ip").GetString(), out var ip) || !IPAddress.IsLoopback(ip)) return false;
            int port = doc.RootElement.GetProperty("port").GetInt32();
            if (port is < 1 or > 65535) return false;
            string certPath = Path.Combine(_root, "user", "certs", "server.crt");
            RewardStartupFiles.RequireLocalPath(_root, certPath);
            using var cert = X509CertificateLoader.LoadCertificateFromFile(certPath);
            DateTimeOffset issued = _now();
            var grant = new RewardEntryGrant(1, Purpose, sessionId, profileId,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), _root,
                "https://" + (ip.Equals(IPAddress.IPv6Loopback) ? "[::1]" : "127.0.0.1") + ":" + port,
                Convert.ToHexStringLower(SHA256.HashData(cert.RawData)), issued, issued.AddMinutes(5),
                process.Pid, process.StartedUtc, process.Arguments.ToArray());
            var directory = RewardRuntimeFiles.DirectoryFor(_root, profileId);
            RewardRuntimeFiles.EnsureDirectory(_root, directory);
            RewardRuntimeFiles.Write(_root, Path.Combine(directory, "entry.json"), grant);
            _grants[profileId] = grant;
            _issued.Add(sessionId);
            return true;
        }
    }

    internal bool Authorize(string profileId, RewardEntryRequest request, out RewardEntryGrant? grant)
        => Authorize(profileId, request, out grant, out _);

    internal bool Authorize(string profileId, RewardEntryRequest request, out RewardEntryGrant? grant, out string? refusal)
    {
        lock (_sync)
        {
            grant = null;
            refusal = null;
            bool continuing = _operation is { } active && active.Profile == profileId && active.Request == request;
            if (!RewardStartupFiles.MarkerAbsent(_root)) return Refuse("source-marker-present", out refusal);
            if (request.Schema != 1) return Refuse("request-schema", out refusal);
            if (!RewardIdentity.IsGuid(request.SessionId)) return Refuse("session-format", out refusal);
            if (!RewardIdentity.IsGuid(request.OperationId)) return Refuse("operation-format", out refusal);
            if (request.HelperPid <= 0) return Refuse("helper-pid-format", out refusal);
            if (request.Port is < ArenaSessionContract.ArenaPort or > ArenaSessionContract.FallbackPortLast)
                return Refuse("private-port", out refusal);
            if (!DateTimeOffset.TryParseExact(request.MarkerCreatedUtc, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var markerTime)) return Refuse("marker-time-format", out refusal);
            if (!_grants.TryGetValue(profileId, out var candidate)) return Refuse("grant-not-in-memory", out refusal);
            if (candidate.SessionId != request.SessionId) return Refuse("grant-session-mismatch", out refusal);
            if (!continuing)
            {
                if (candidate.ExpiresUtc <= _now()) return Refuse("grant-expired", out refusal);
                if (candidate.IssuedUtc > _now()) return Refuse("grant-issued-in-future", out refusal);
                if (markerTime < candidate.IssuedUtc.AddMinutes(-1)) return Refuse("marker-time-before-grant", out refusal);
                if (markerTime > _now().AddMinutes(1)) return Refuse("marker-time-in-future", out refusal);
            }
            if (!SecretEquals(candidate.Token, request.Token)) return Refuse("grant-secret-mismatch", out refusal);
            try
            {
                if (!Path.IsPathFullyQualified(request.PrivateRoot)) return Refuse("private-root-relative", out refusal);
                if (!RewardRuntimeFiles.SameRoot(request.PrivateRoot, _privateRoot())) return Refuse("private-root-mismatch", out refusal);
            }
            catch { return Refuse("private-root-unavailable", out refusal); }
            var process = _process();
            if (process is null) return Refuse("server-identity-unavailable", out refusal);
            if (process.Pid != candidate.ServerPid) return Refuse("server-pid-mismatch", out refusal);
            if (process.StartedUtc != candidate.ServerStartedUtc) return Refuse("server-start-mismatch", out refusal);
            if (!process.Arguments.SequenceEqual(candidate.ServerArguments)) return Refuse("server-arguments-mismatch", out refusal);
            if (!_helper(request)) return Refuse("helper-identity-mismatch", out refusal);
            if (_operation is { } prior && (prior.Profile != profileId || prior.Request != request))
                return Refuse("operation-already-owned", out refusal);
            _operation = (profileId, request);
            grant = candidate;
            return true;
        }
    }

    private static bool Refuse(string reason, out string? refusal)
    {
        // Only fixed reason codes reach the local log; never capability or process values.
        refusal = reason;
        try { ArenaContext.Logger?.Debug("[ArenaMode] Arena entry authorization refused: " + reason + "."); }
        catch { } // Diagnostics must not change the authentication result.
        return false;
    }

    internal void ReleaseBeforeQuiescence(string profileId, RewardEntryRequest request)
    {
        lock (_sync)
            if (_operation is { } current && current.Profile == profileId && current.Request == request) _operation = null;
    }

    private static bool SecretEquals(string expected, string supplied)
    {
        try
        {
            byte[] bytes = Convert.FromBase64String(supplied);
            return bytes.Length == 32 && CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(expected), bytes);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentNullException) { return false; }
    }
}
