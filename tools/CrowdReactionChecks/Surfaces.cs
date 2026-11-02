// Stand-in ArenaMapPort SpectatorCrowd surfaces. ArenaMapPort.Client.SpectatorCrowd is the phase A
// surface of D:\ArenaMapPort\docs\PLAN-CROWD-REACTIONS.md 4.3 (the four 0.4.0 members plus the four
// reaction members); the Variants namespace holds the published 0.4.0 surface and the broken ones.
// All of them forward to FakeCrowd, which behaves like the MapPort hub: one holder, equal owners
// deduplicated after truncation to 64 characters (CrowdReactionQueue.CleanOwner; the plan had 40, as
// for the score monitors), a stale handle changes nothing. The real queue itself is driven by the
// real-surface stage at the end of Program.cs (RealMapPort.cs).
public static class FakeCrowd
{
    // The phase A kinds; MapPort adds the Lull bit only once it ships the Lull bank (D14).
    public const int AllKinds = (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4) | (1 << 5);
    public const int LullBit = 1 << 6;
    public static int Version = 1, Kinds = AllKinds, Handle, NextHandle, Claims, KindReads, Releases, ReactCalls;
    public static bool Available = true, RefuseClaim, ReactResult = true, ThrowReact, ThrowExcite, ThrowKinds, ThrowVersion;
    public static string Owner;
    public static readonly List<string> Owners = new();
    public static readonly List<string> SentOwners = new();   // as passed, before MapPort's truncation
    public static readonly List<(int kind, float strength)> Reactions = new();
    public static readonly List<float> Excitement = new();

    public static void Reset()
    {
        Version = 1; Kinds = AllKinds; Handle = Claims = KindReads = Releases = ReactCalls = 0;
        Available = true; RefuseClaim = ThrowReact = ThrowExcite = ThrowKinds = ThrowVersion = false; ReactResult = true;
        Owner = null; Owners.Clear(); SentOwners.Clear(); Reactions.Clear(); Excitement.Clear();
    }

    public static int GetVersion() => ThrowVersion ? throw new InvalidOperationException("version exception") : Version;
    public static bool GetAvailable() => Available;
    public static void SetExcitement(float level)
    {
        if (ThrowExcite) throw new InvalidOperationException("excitement exception");
        Excitement.Add(level);
    }
    public static int GetKinds()
    {
        KindReads++;
        if (ThrowKinds) throw new InvalidOperationException("kinds exception");
        return Kinds;
    }
    // Deliberately does not refuse on an empty mask: the checks prove ArenaMode never asks then.
    public static int Claim(string owner)
    {
        Claims++;
        SentOwners.Add(owner);
        if (RefuseClaim || string.IsNullOrWhiteSpace(owner)) return 0;
        owner = owner.Length > 64 ? owner.Substring(0, 64) : owner;
        if (Handle != 0) return owner == Owner ? Handle : 0;
        Owner = owner; Owners.Add(owner);
        return Handle = ++NextHandle;
    }
    public static bool React(int claim, int kind, float strength)
    {
        ReactCalls++;
        if (ThrowReact) throw new InvalidOperationException("react exception");
        if (!ReactResult || claim == 0 || claim != Handle) return false;
        if (kind < 1 || kind > 31 || (Kinds & (1 << kind)) == 0 || float.IsNaN(strength)) return false;
        Reactions.Add((kind, strength));
        return true;
    }
    public static bool Release(int claim)
    {
        Releases++;
        if (claim == 0 || claim != Handle) return false;
        Handle = 0; Owner = null;
        return true;
    }
}

namespace ArenaMapPort.Client
{
    // Same assembly as this anchor lets the real plugin lookup find the stand-in contract.
    public sealed class PluginAnchor { }

    public static class SpectatorCrowd
    {
        public static int ContractVersion => FakeCrowd.GetVersion();
        public static bool IsAvailable => FakeCrowd.GetAvailable();
        public static float Excitement => 0f;
        public static void SetExcitement(float level01) => FakeCrowd.SetExcitement(level01);
        public static int ReactionKinds => FakeCrowd.GetKinds();
        public static int ClaimReactions(string owner) => FakeCrowd.Claim(owner);
        public static bool React(int claim, int kind, float strength01) => FakeCrowd.React(claim, kind, strength01);
        public static bool ReleaseReactions(int claim) => FakeCrowd.Release(claim);
    }
}

namespace Variants
{
    // The published ArenaMapPort 0.4.0 contract: exactly these four members.
    public static class V040
    {
        public static int ContractVersion => FakeCrowd.GetVersion();
        public static bool IsAvailable => FakeCrowd.GetAvailable();
        public static float Excitement => 0f;
        public static void SetExcitement(float level01) => FakeCrowd.SetExcitement(level01);
    }

    public static class NoExcitement
    {
        public static int ContractVersion => FakeCrowd.GetVersion();
        public static int ReactionKinds => FakeCrowd.GetKinds();
        public static int ClaimReactions(string owner) => FakeCrowd.Claim(owner);
        public static bool React(int claim, int kind, float strength01) => FakeCrowd.React(claim, kind, strength01);
        public static bool ReleaseReactions(int claim) => FakeCrowd.Release(claim);
    }

    public static class PartialReactions
    {
        public static int ContractVersion => FakeCrowd.GetVersion();
        public static bool IsAvailable => FakeCrowd.GetAvailable();
        public static void SetExcitement(float level01) => FakeCrowd.SetExcitement(level01);
        public static int ReactionKinds => FakeCrowd.GetKinds();
        public static int ClaimReactions(string owner) => FakeCrowd.Claim(owner);
    }

    public static class WrongReact
    {
        public static int ContractVersion => FakeCrowd.GetVersion();
        public static bool IsAvailable => FakeCrowd.GetAvailable();
        public static void SetExcitement(float level01) => FakeCrowd.SetExcitement(level01);
        public static int ReactionKinds => FakeCrowd.GetKinds();
        public static int ClaimReactions(string owner) => FakeCrowd.Claim(owner);
        public static bool React(int claim, int kind, double strength01) => FakeCrowd.React(claim, kind, (float)strength01);
        public static bool ReleaseReactions(int claim) => FakeCrowd.Release(claim);
    }

    public static class ConvertibleClaim
    {
        public static int ContractVersion => FakeCrowd.GetVersion();
        public static void SetExcitement(float level01) => FakeCrowd.SetExcitement(level01);
        public static int ReactionKinds => FakeCrowd.GetKinds();
        public static int ClaimReactions(object owner) => FakeCrowd.Claim(owner?.ToString());
        public static bool React(int claim, int kind, float strength01) => FakeCrowd.React(claim, kind, strength01);
        public static bool ReleaseReactions(int claim) => FakeCrowd.Release(claim);
    }

    public static class WrongExcitement
    {
        public static int ContractVersion => FakeCrowd.GetVersion();
        public static bool IsAvailable => FakeCrowd.GetAvailable();
        public static void SetExcitement(double level01) => FakeCrowd.SetExcitement((float)level01);
        public static int ReactionKinds => FakeCrowd.GetKinds();
        public static int ClaimReactions(string owner) => FakeCrowd.Claim(owner);
        public static bool React(int claim, int kind, float strength01) => FakeCrowd.React(claim, kind, strength01);
        public static bool ReleaseReactions(int claim) => FakeCrowd.Release(claim);
    }

    public static class WrongAvailable
    {
        public static int ContractVersion => FakeCrowd.GetVersion();
        public static int IsAvailable => 1;
        public static void SetExcitement(float level01) => FakeCrowd.SetExcitement(level01);
        public static int ReactionKinds => FakeCrowd.GetKinds();
        public static int ClaimReactions(string owner) => FakeCrowd.Claim(owner);
        public static bool React(int claim, int kind, float strength01) => FakeCrowd.React(claim, kind, strength01);
        public static bool ReleaseReactions(int claim) => FakeCrowd.Release(claim);
    }

    // A reaction member that became internal: present (as for ArenaMapPortSpawnGates.BindCompletion),
    // so a changed member, not an older MapPort.
    public static class InternalKinds
    {
        public static int ContractVersion => FakeCrowd.GetVersion();
        public static void SetExcitement(float level01) => FakeCrowd.SetExcitement(level01);
        internal static int ReactionKinds => FakeCrowd.GetKinds();
        public static int ClaimReactions(string owner) => FakeCrowd.Claim(owner);
        public static bool React(int claim, int kind, float strength01) => FakeCrowd.React(claim, kind, strength01);
        public static bool ReleaseReactions(int claim) => FakeCrowd.Release(claim);
    }

    // Exact parameters, another return type: the binder's return-type check is the only guard;
    // without it Delegate.CreateDelegate throws and the whole contract (the murmur too) would fail.
    public static class WrongReactReturn
    {
        public static int ContractVersion => FakeCrowd.GetVersion();
        public static void SetExcitement(float level01) => FakeCrowd.SetExcitement(level01);
        public static int ReactionKinds => FakeCrowd.GetKinds();
        public static int ClaimReactions(string owner) => FakeCrowd.Claim(owner);
        public static int React(int claim, int kind, float strength01) => FakeCrowd.React(claim, kind, strength01) ? 1 : 0;
        public static bool ReleaseReactions(int claim) => FakeCrowd.Release(claim);
    }

    public static class WrongReleaseReturn
    {
        public static int ContractVersion => FakeCrowd.GetVersion();
        public static void SetExcitement(float level01) => FakeCrowd.SetExcitement(level01);
        public static int ReactionKinds => FakeCrowd.GetKinds();
        public static int ClaimReactions(string owner) => FakeCrowd.Claim(owner);
        public static bool React(int claim, int kind, float strength01) => FakeCrowd.React(claim, kind, strength01);
        public static void ReleaseReactions(int claim) => FakeCrowd.Release(claim);
    }

    // A reaction member that is a field: present, but not the property contract.
    public static class FieldKinds
    {
        public static int ContractVersion => FakeCrowd.GetVersion();
        public static void SetExcitement(float level01) => FakeCrowd.SetExcitement(level01);
        public static int ReactionKinds = FakeCrowd.AllKinds;
        public static int ClaimReactions(string owner) => FakeCrowd.Claim(owner);
        public static bool React(int claim, int kind, float strength01) => FakeCrowd.React(claim, kind, strength01);
        public static bool ReleaseReactions(int claim) => FakeCrowd.Release(claim);
    }

    // An extra overload is not a changed member: the exact one still binds.
    public static class OverloadedReact
    {
        public static int ContractVersion => FakeCrowd.GetVersion();
        public static void SetExcitement(float level01) => FakeCrowd.SetExcitement(level01);
        public static int ReactionKinds => FakeCrowd.GetKinds();
        public static int ClaimReactions(string owner) => FakeCrowd.Claim(owner);
        public static bool React(int claim, int kind, float strength01) => FakeCrowd.React(claim, kind, strength01);
        public static bool React(int claim, int kind) => FakeCrowd.React(claim, kind, 1f);
        public static bool ReleaseReactions(int claim) => FakeCrowd.Release(claim);
    }

    public static class ContractTwo
    {
        public static int ContractVersion => 2;
        public static bool IsAvailable => FakeCrowd.GetAvailable();
        public static void SetExcitement(float level01) => FakeCrowd.SetExcitement(level01);
        public static int ReactionKinds => FakeCrowd.GetKinds();
        public static int ClaimReactions(string owner) => FakeCrowd.Claim(owner);
        public static bool React(int claim, int kind, float strength01) => FakeCrowd.React(claim, kind, strength01);
        public static bool ReleaseReactions(int claim) => FakeCrowd.Release(claim);
    }

    public static class NoVersion
    {
        public static bool IsAvailable => FakeCrowd.GetAvailable();
        public static void SetExcitement(float level01) => FakeCrowd.SetExcitement(level01);
        public static int ReactionKinds => FakeCrowd.GetKinds();
        public static int ClaimReactions(string owner) => FakeCrowd.Claim(owner);
        public static bool React(int claim, int kind, float strength01) => FakeCrowd.React(claim, kind, strength01);
        public static bool ReleaseReactions(int claim) => FakeCrowd.Release(claim);
    }

    public static class FieldVersion
    {
        public static int ContractVersion = 1;
        public static void SetExcitement(float level01) => FakeCrowd.SetExcitement(level01);
    }

    public static class ThrowingVersion
    {
        public static int ContractVersion => FakeCrowd.GetVersion();
        public static void SetExcitement(float level01) => FakeCrowd.SetExcitement(level01);
    }
}
