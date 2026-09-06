using System;
using System.Collections.Generic;

namespace ArenaMode.Bots;

/// <summary>Round-owned, value-only log accounting; never participates in AI decisions.</summary>
internal sealed class TeamArenaAwarenessDiagnostics
{
    private sealed class Channel
    {
        internal readonly HashSet<string> Seen = new(StringComparer.Ordinal);
        internal readonly HashSet<string> Pending = new(StringComparer.Ordinal);
        internal readonly HashSet<string> Recovered = new(StringComparer.Ordinal);
        internal float NextRepeat;
        internal int Suppressed;
    }

    private readonly Dictionary<string, Channel> _channels = new(StringComparer.Ordinal);

    internal bool Failure(string sourceChannel, string target, string stage, float now,
        out int suppressed, out int pending)
    {
        if (!_channels.TryGetValue(sourceChannel, out var channel))
            _channels[sourceChannel] = channel = new Channel();
        channel.Pending.Add(target);
        pending = channel.Pending.Count;
        suppressed = channel.Suppressed;
        bool firstReason = channel.Seen.Add(target + ":" + stage);
        if (!firstReason && now < channel.NextRepeat)
        {
            if (channel.Suppressed < int.MaxValue) channel.Suppressed++;
            return false;
        }
        channel.Suppressed = 0;
        // One repeated report per source/channel, regardless of target cycling.
        channel.NextRepeat = now + 30f;
        return true;
    }

    internal bool Recovery(string sourceChannel, string target)
        => _channels.TryGetValue(sourceChannel, out var channel)
            && channel.Pending.Remove(target) && channel.Recovered.Add(target);

    internal void Clear() => _channels.Clear();
}
