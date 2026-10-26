using System;

namespace ArenaMode.Audio;

/// <summary>Bounded chatter, independent of the native speaker and its asset lifetime.</summary>
internal static class ArenaPmcVoicePolicy
{
    internal const float CooldownSeconds = 7f;
    internal const float CueLifetimeSeconds = 1.25f;
    internal const float KillDelaySeconds = .35f;
    internal const int KillChancePercent = 40;
    internal static readonly string[] UsecVoices = { "Usec_1", "Usec_2", "Usec_3", "Usec_4", "Usec_5", "Usec_6" };
    internal static readonly string[] BearVoices = { "Bear_1", "Bear_1_Eng", "Bear_2", "Bear_2_Eng", "Bear_3" };

    internal static string Rotate(bool usec, ref int cursor, int initialIndex)
    {
        var voices = usec ? UsecVoices : BearVoices;
        if (cursor < 0) cursor = Math.Max(0, initialIndex) % voices.Length;
        string voice = voices[cursor % voices.Length];
        cursor = (cursor + 1) % voices.Length;
        return voice;
    }

    internal static bool CanSpeak(float now, float lastSpoke, bool announcer, bool busy)
        => !announcer && !busy && now - lastSpoke >= CooldownSeconds;

    internal static bool ValidCue(float now, float due, float expires, object body, object current)
        => ReferenceEquals(body, current) && now >= due && now <= expires;
}
