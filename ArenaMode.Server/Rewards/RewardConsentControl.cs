using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ArenaMode.Shared;

namespace ArenaMode.Server.Rewards;

// These records are server/helper transport, never public client status DTOs.
internal sealed record RewardControlGrant(int Schema, string ControlId, string ProfileId, string Token,
    string RuntimeRoot, string OriginUrl, string CertificateSha256, DateTimeOffset IssuedUtc, DateTimeOffset ExpiresUtc);
internal sealed record RewardControlLease(int Schema, RewardControlGrant Grant, string PrivateRoot, string MarkerCreatedUtc, int Port);
internal sealed record RewardControlRequest(int Schema, string ControlId, string Token, long? ExpectedRevision, bool? Enabled);
internal sealed record RewardControlReply(bool Accepted, string Reason, long Revision, RewardStatusResponse? Status, RewardHandoff? Handoff);

/// <summary>A process-local grant only authorizes consent metadata, never money or a profile replacement.</summary>
internal sealed class RewardConsentControl(string root, Func<DateTimeOffset> now)
{
    internal const string Route = "/arenamode/rewards/private-control";
    private readonly Dictionary<string, RewardControlGrant> _grants = new(StringComparer.Ordinal);
    private readonly HashSet<string> _issued = new(StringComparer.Ordinal);

    // The coordinator holds its profile-state lock around issue/validate/use.
    internal RewardControlGrant Issue(string id, string controlId)
    {
        if (!RewardStartupFiles.MarkerAbsent(root) || !RewardIdentity.IsGuid(controlId))
            throw new RewardDataException("reward-control-prepare-rejected");
        _ = RewardRuntimeFiles.DirectoryFor(root, id);
        if (_grants.TryGetValue(id, out var current) && current.ControlId == controlId && current.ExpiresUtc > now())
            return current;
        if (_issued.Contains(controlId) || _issued.Count >= 4096)
            throw new RewardDataException("reward-control-prepare-stale");
        var config = Path.Combine(root, "SPT_Data", "configs", "http.json");
        RewardStartupFiles.RequireLocalPath(root, config);
        using var doc = JsonDocument.Parse(File.ReadAllText(config), new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        string host = doc.RootElement.GetProperty("ip").GetString() ?? "";
        int port = doc.RootElement.GetProperty("port").GetInt32();
        if (!IPAddress.TryParse(host, out var ip) || !(IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            || port is < 1 or > 65535) throw new RewardDataException("reward-control-needs-local-server");
        string origin = "https://" + (ip.Equals(IPAddress.IPv6Loopback) ? "[::1]" : "127.0.0.1") + ":" + port;
        var certPath = Path.Combine(root, "user", "certs", "server.crt");
        RewardStartupFiles.RequireLocalPath(root, certPath);
        using var cert = X509CertificateLoader.LoadCertificateFromFile(certPath);
        DateTimeOffset issued = now();
        var grant = new RewardControlGrant(1, controlId, id, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            root, origin, Convert.ToHexStringLower(SHA256.HashData(cert.RawData)), issued, issued.AddHours(12));
        var directory = RewardRuntimeFiles.DirectoryFor(root, id);
        RewardRuntimeFiles.EnsureDirectory(root, directory);
        RewardRuntimeFiles.Write(root, Path.Combine(directory, "control.json"), grant);
        // A later entry or a normal-server restart invalidates all older copies.
        _grants[id] = grant;
        _issued.Add(controlId);
        return grant;
    }

    internal bool Accepts(string id, RewardControlRequest request)
    {
        if (!RewardStartupFiles.MarkerAbsent(root) || request.Schema != 1
            || !_grants.TryGetValue(id, out var grant) || grant.ControlId != request.ControlId
            || grant.ExpiresUtc <= now() || grant.IssuedUtc > now()) return false;
        try
        {
            byte[] supplied = Convert.FromBase64String(request.Token);
            return supplied.Length == 32 && CryptographicOperations.FixedTimeEquals(supplied, Convert.FromBase64String(grant.Token));
        }
        catch (Exception e) when (e is FormatException or ArgumentNullException) { return false; }
    }

    internal static void ValidateGrant(RewardControlGrant grant, DateTimeOffset at)
    {
        if (grant.Schema != 1 || !RewardIdentity.IsGuid(grant.ControlId) || !RewardIdentity.IsHex(grant.ProfileId, 24)
            || !RewardIdentity.IsHex(grant.CertificateSha256, 64) || Convert.FromBase64String(grant.Token).Length != 32
            || grant.ExpiresUtc <= at || grant.IssuedUtc > at.AddMinutes(1) || grant.ExpiresUtc <= grant.IssuedUtc
            || grant.ExpiresUtc - grant.IssuedUtc > TimeSpan.FromHours(12)
            || !Path.IsPathFullyQualified(grant.RuntimeRoot)) throw new RewardDataException("invalid-reward-control-grant");
        if (!Uri.TryCreate(grant.OriginUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) || !IPAddress.IsLoopback(ip)
            || uri.AbsolutePath != "/" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new RewardDataException("invalid-reward-control-origin");
    }
}
