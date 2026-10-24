using System.Collections.Generic;

namespace ArenaMode.Shared;

// Untrusted gameplay/consent transport only. No profile path, amount, payout
// authority or restoration assertion is accepted from the game client.
public static class RewardWire
{
    public const int Schema = 1;
    public const string StatusRoute = "/arenamode/rewards/status";
    public const string ConsentRoute = "/arenamode/rewards/consent";
    public const string PrepareControlRoute = "/arenamode/rewards/control/prepare";
    public const string BeginRoute = "/arenamode/rewards/begin";
    public const string CaptureRoute = "/arenamode/rewards/capture";
}

// Preparing a private-session control capability returns no bearer credential.
// The helper copies the normal server's bounded export from disk instead.
public class RewardControlPrepareRequest
{
    public string ControlId { get; set; } = "";
}

public sealed class RewardControlPrepareResponse
{
    public bool Accepted { get; set; }
    public string Reason { get; set; } = "";
}

public class RewardConsentRequest
{
    public bool Enabled { get; set; }
    public long? Revision { get; set; }
}

public sealed class RewardStatusResponse
{
    public bool Available { get; set; }
    public bool Enabled { get; set; }
    public bool Enrolled { get; set; }
    public bool PrivateSession { get; set; }
    public bool CanChange { get; set; }
    public long ConsentRevision { get; set; }
    public string State { get; set; } = "unavailable";
    public string Detail { get; set; } = "";
    public long PendingRoubles { get; set; }
    public long LastPaidRoubles { get; set; }
    public long RoublesPerKill { get; set; }
    public long RoublesPerRoundWin { get; set; }
    public long MaxRoubles { get; set; }
}

public class RewardBeginRequest
{
    public int Schema { get; set; } = RewardWire.Schema;
    public string ArmToken { get; set; } = "";
    public string NativeEndIdentity { get; set; } = "";
    public string Mode { get; set; } = "";
    // KillsOnly, LocalTarget, FreeForAllScore, TeamScore,
    // SoloElimination, TeamElimination; server checks against its armed mode.
    public string Rule { get; set; } = "";
    public long TimeLimitMilliseconds { get; set; }
    public int KillTarget { get; set; }
}

public sealed class RewardBeginResponse
{
    public bool Accepted { get; set; }
    public string RunId { get; set; } = "";
    public string Reason { get; set; } = "";
}

public class RewardCaptureRequest
{
    public int Schema { get; set; } = RewardWire.Schema;
    public string ArmToken { get; set; } = "";
    public string NativeEndIdentity { get; set; } = "";
    public string RunId { get; set; } = "";
    // Same revision must have identical semantic contents. Later snapshots may
    // only append; earlier deaths/actors/closed rounds must never change.
    public long Revision { get; set; }
    public bool Final { get; set; }
    public string State { get; set; } = "Completed";
    public List<RewardRoundCapture> Rounds { get; set; } = new List<RewardRoundCapture>();
}

public sealed class RewardRoundCapture
{
    public int Number { get; set; }
    public bool Closed { get; set; }
    public long ObservedDurationMilliseconds { get; set; }
    public List<RewardActorCapture> Actors { get; set; } = new List<RewardActorCapture>();
    public List<RewardDeathCapture> Deaths { get; set; } = new List<RewardDeathCapture>();
}

public sealed class RewardActorCapture
{
    // A match-local generation for one exact physical Player wrapper. It is
    // never reused when a pooled native instance ID or participant respawns.
    public long Body { get; set; }
    public string ParticipantId { get; set; } = "";
    public string Side { get; set; } = "";
    public long SpawnedAtMilliseconds { get; set; }
}

public sealed class RewardDeathCapture
{
    public long Sequence { get; set; }
    public long AtMilliseconds { get; set; }
    public long VictimBody { get; set; }
    public long? KillerBody { get; set; }
    public string Kind { get; set; } = "Combat";
}

public sealed class RewardCaptureResponse
{
    public bool Accepted { get; set; }
    public long Revision { get; set; }
    public string Reason { get; set; } = "";
}
