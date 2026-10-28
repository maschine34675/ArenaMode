using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

// Optional native check: hold an ordinary notifier connection until <output>.stop exists.
// No game/profile messages are sent. TLS must match the installation's certificate.
internal static class DrainProbe
{
    internal static async Task<int> Run(string[] args)
    {
        string output = args[3];
        try
        {
            var uri = new Uri(args[1]);
            if (!uri.IsLoopback || uri.Scheme != "wss") throw new ArgumentException("A loopback wss endpoint is required");
            string pem = File.ReadAllText(args[2]);
            byte[] cert = Convert.FromBase64String(pem.Replace("-----BEGIN CERTIFICATE-----", "").Replace("-----END CERTIFICATE-----", ""));
            string pin;
            using (var hash = SHA256.Create()) pin = Convert.ToBase64String(hash.ComputeHash(cert));
            ServicePointManager.ServerCertificateValidationCallback = (_, certificate, chain, errors) =>
            {
                if (certificate == null) return false;
                using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(certificate.GetRawCertData())) == pin;
            };
            using (var socket = new ClientWebSocket())
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6)))
            {
                socket.Options.Proxy = null;
                await socket.ConnectAsync(uri, timeout.Token);
                File.WriteAllText(output, "CONNECTED " + DateTime.UtcNow.ToString("O"));
                var receive = ReceiveUntilClose(socket, timeout.Token);
                while (!File.Exists(output + ".stop") && !timeout.IsCancellationRequested && !receive.IsCompleted)
                    await Task.Delay(200);
                if (receive.IsCompleted && !File.Exists(output + ".stop"))
                    throw new IOException("Notifier closed before the requested test stop");
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test finished", timeout.Token);
                await Task.WhenAny(receive, Task.Delay(3000));
                File.AppendAllText(output, "\nCLOSED " + DateTime.UtcNow.ToString("O"));
            }
            return 0;
        }
        catch (Exception error)
        {
            File.AppendAllText(output, "\nFAILED " + error.GetType().Name + ": " + error.Message);
            return 1;
        }
    }

    private static async Task ReceiveUntilClose(ClientWebSocket socket, CancellationToken cancellation)
    {
        var buffer = new ArraySegment<byte>(new byte[4096]);
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellation);
            if (result.MessageType == WebSocketMessageType.Close) return;
        }
    }
}
