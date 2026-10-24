using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArenaMode.Server.Rewards;

internal static class RewardConsentTransport
{
    internal const int MaxBytes = 16384;

    internal static async Task<RewardControlReply> SendAsync(RewardControlGrant grant, RewardControlRequest request, CancellationToken ct)
    {
        RewardConsentControl.ValidateGrant(grant, DateTimeOffset.UtcNow);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert is not null
                && Convert.ToHexStringLower(SHA256.HashData(cert.RawData)) == grant.CertificateSha256
        };
        using var client = new HttpClient(handler);
        using var message = new HttpRequestMessage(HttpMethod.Post, grant.OriginUrl.TrimEnd('/') + RewardConsentControl.Route);
        message.Headers.Add("Cookie", "PHPSESSID=" + grant.ProfileId);
        message.Content = new StringContent(RewardRuntimeFiles.Json(request), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        string body = await ReadBoundedAsync(stream, timeout.Token);
        using var doc = JsonDocument.Parse(body);
        RewardStartupFiles.RejectDuplicateProperties(doc.RootElement);
        return doc.RootElement.Deserialize<RewardControlReply>(RewardSettlementCore.JournalJson)
            ?? throw new RewardDataException("reward-control-empty-response");
    }

    internal static async Task<string> ReadBoundedAsync(Stream stream, CancellationToken ct)
    {
        byte[] bytes = new byte[MaxBytes + 1];
        int count = 0;
        while (count < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(count, bytes.Length - count), ct);
            if (read == 0) break;
            count += read;
        }
        if (count > MaxBytes) throw new RewardDataException("reward-control-body-too-large");
        return new UTF8Encoding(false, true).GetString(bytes, 0, count);
    }
}
