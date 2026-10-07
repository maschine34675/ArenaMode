// Reuse the announcer's Unity device stand-ins; only these hit-feed boundaries
// are additional. No game state, health mutation or physical raid is simulated.
namespace ArenaMode.Config
{
    internal static partial class Settings
    {
        public static Entry<bool> HitmarkerSounds { get; } = new() { Value = true };
        public static Entry<float> HitmarkerVolume { get; } = new() { Value = 0.5f };
    }
}
namespace ArenaMode.Session
{
    internal static partial class ArenaSession { public static bool IsActive; }
}
namespace ArenaMode.Modes
{
    internal static class ArenaMatchService { public static ArenaMatch Current; }
}
namespace ArenaMode.Events
{
    internal static class ArenaEvents
    {
        public static event Action<Combat.ArenaDamage> OnLocalDamageDealt;
        public static int Subscribers => OnLocalDamageDealt?.GetInvocationList().Length ?? 0;
        public static void Damage(bool head = false, bool kill = false, bool eligible = true)
            => OnLocalDamageDealt?.Invoke(new Combat.ArenaDamage
                { AudioEligible = eligible, Headshot = head, Kill = kill, BodyDamage = 30 });
    }
}
namespace UnityEngine { public struct Vector3 { public float x, y, z; } }
