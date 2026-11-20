using ArenaMode.Modes;
using ArenaMode.Shared;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Text;
using System.Threading.Tasks;

namespace ArenaMode.Session;

/// <summary>
/// The HTTP half of the Tactical round report (<see cref="TacticalOpeningBandGate"/> is the logic).
///
/// Why not <c>ArenaServerClient</c> and <c>RequestHandler.PostJson</c>: that bridge blocks the calling
/// thread (<c>Task.Run(...).Result</c>) and writes two Info lines per call, which is right at arm time
/// and wrong for a call every round (gameplay is not an Info topic, docs/LOGGING.md). This follows the
/// client-settings poll instead: the call goes through <c>RequestHandler.HttpClient</c> inside
/// <c>Task.Run</c>, so no continuation can wait on Unity's context, and no wrapper message is written.
/// The caller never waits: the gate polls the returned task each frame.
///
/// Nothing here touches a Unity object off the main thread: the body is built synchronously on the
/// caller's (main) thread, and the background part is only the HTTP call and the JSON parse. The HTTP
/// client has up to three retries and no per-call timeout, so the gate owns the caps
/// (<see cref="TacticalOpeningBandGate.HoldCapSeconds"/>, <see cref="TacticalOpeningBandGate.InFlightLostSeconds"/>);
/// a call that hangs leaves one background task, and its seq makes a late answer harmless.
/// A failed call throws inside the task; the gate counts that as a lost report.
///
/// Logging, exactly: this class writes nothing. The gate writes Debug lines, ONE Warning per raid
/// for a report that never got through, and, for a fault inside the gate itself, one more Warning
/// (<c>LogFault</c>, once per raid). The SPT HTTP client, which is not ours, writes its own
/// <c>LogError</c> line per failed attempt; against a server without the route (an old server) the
/// first report costs up to four sends of up to four attempts, so up to 16 such lines once per
/// raid before the gate ends the feature. That is accepted: probing the route first would cost
/// every healthy raid an extra request, and an old server next to a new client is a misinstall
/// worth a loud log (docs/LOGGING.md, "Tactical opening rounds").
/// </summary>
internal static class ArenaTacticalRoundClient
{
    /// <summary>The sender handed to the gate: reports <paramref name="round"/> with <paramref name="seq"/> under the exact arm token.</summary>
    internal static Task<TacticalRoundReply> SendAsync(string armToken, int round, int seq)
    {
        byte[] body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { armToken, round, seq }));
        return Task.Run(async () =>
        {
            byte[] payload = await RequestHandler.HttpClient.PostAsync(Protocol.TacticalRoundRoute, body).ConfigureAwait(false);
            return TacticalRoundReply.Parse(Encoding.UTF8.GetString(payload ?? Array.Empty<byte>()));
        });
    }
}
