using System;
using Newtonsoft.Json.Linq;

namespace ArenaMode.Modes;

/// <summary>
/// The server's answer to one Tactical round report (<c>Protocol.TacticalRoundRoute</c>):
/// <c>accepted</c> (false only for a refusal), <c>applies</c> (the server writes a Tactical stage band
/// for this raid), the STORED <c>round</c> and <c>seq</c> (the client trusts these over its own
/// request), <c>duplicate</c> (a resend the server already holds; it still counts as acknowledged)
/// and the refusal <c>reason</c>. Pure data plus the one parser, so the gate and its harness never
/// need the game's HTTP stack.
/// </summary>
internal sealed class TacticalRoundReply
{
    public bool Accepted { get; set; }
    public bool Applies { get; set; }
    public int Round { get; set; }
    public int Seq { get; set; }
    public bool Duplicate { get; set; }
    public string Reason { get; set; }

    /// <summary>
    /// Reads the reply body. A body that is not JSON, or lacks <c>accepted</c>, <c>applies</c>, a round
    /// or a seq, throws: the caller counts that as a failed send (a malformed answer is a transport
    /// problem, not a statement of the server), never as an acknowledgement.
    /// </summary>
    public static TacticalRoundReply Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new FormatException("the Tactical round reply was empty");
        var root = JObject.Parse(json);
        return new TacticalRoundReply
        {
            Accepted = Required<bool>(root, "accepted"),
            Applies = Required<bool>(root, "applies"),
            Round = Required<int>(root, "round"),
            Seq = Required<int>(root, "seq"),
            Duplicate = root["duplicate"]?.Type == JTokenType.Boolean && root["duplicate"].Value<bool>(),
            Reason = root["reason"]?.Type == JTokenType.String ? root["reason"].Value<string>() : null,
        };
    }

    private static T Required<T>(JObject root, string name)
    {
        var token = root[name];
        bool typeOk = typeof(T) == typeof(bool)
            ? token?.Type == JTokenType.Boolean
            : token?.Type == JTokenType.Integer;
        if (!typeOk)
            throw new FormatException($"the Tactical round reply has no usable '{name}'");
        return token.Value<T>();
    }
}
