using ArenaMode.Spectator;
using EFT;
using EFT.HealthSystem;
using System.Globalization;

internal static class Program
{
    private static int _assertions;

    private static void Main()
    {
        TargetChangesAndDamage();
        LifetimeAndIdentity();
        InvalidAndUnavailableHealth();
        ChangesDuringNativeRead();
        Console.WriteLine($"ARENA_SPECTATOR_HEALTH_CHECKS_PASSED ({_assertions} assertions)");
    }

    private static void TargetChangesAndDamage()
    {
        var fixture = new Fixture();
        Assert(fixture.Read() == "SPECTATING ALLY: Ally\nHP 350 / 440    LMB/RMB switch",
            "living ally uses total current and actual maximum HP");
        Assert(fixture.Health.LastPart == EBodyPart.Common && fixture.Health.Reads == 1,
            "native read must aggregate Common, not one body part");

        fixture.Health.Total = new ValueStruct { Current = 213.7f, Maximum = 440f };
        Assert(fixture.Read().Contains("HP 214 / 440"), "damage is reflected without changing target");
        fixture.Health.Total = new ValueStruct { Current = 305.2f, Maximum = 440f };
        Assert(fixture.Read().Contains("HP 305 / 440"), "healing is reflected without changing target");

        var opponentHealth = new FakeHealth { Total = new ValueStruct { Current = 512f, Maximum = 600f } };
        var opponent = new Player { ProfileId = "opponent", HealthController = opponentHealth };
        fixture.Target = new ArenaSpectatorTarget(opponent, "opponent", "Opponent", ArenaSpectatorSide.Opponent);
        fixture.Frozen = opponent;
        Assert(fixture.Read() == "SPECTATING OPPONENT: Opponent\nHP 512 / 600    LMB/RMB switch",
            "switching target uses the newly selected body and custom maximum");
        Assert(fixture.Health.Reads == 3 && opponentHealth.Reads == 1,
            "switching never queries the prior target's controller");

        opponentHealth.Total = new ValueStruct { Current = 40f, Maximum = 600f };
        Assert(fixture.Read().Contains("HP 40 / 600"), "opponent damage updates independently");
        opponentHealth.IsAlive = false;
        Assert(fixture.Read() == null, "dead opponent clears previously displayed HP");

        fixture.Target = new ArenaSpectatorTarget(fixture.Bot, "ally", "<color=red>name</color>\nextra", ArenaSpectatorSide.Ally);
        fixture.Frozen = fixture.Bot;
        Assert(fixture.Read().Count(c => c == '\n') == 1,
            "mod-provided multiline names cannot displace the fixed health line");
        fixture.Target = new ArenaSpectatorTarget(fixture.Bot, "ally", null, ArenaSpectatorSide.Ally);
        Assert(fixture.Read().StartsWith("SPECTATING ALLY: ally\n"), "missing display name falls back to exact profile identity");

        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert(fixture.Read().Contains("HP 305 / 440"), "readout has stable numeric formatting across local cultures");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static void LifetimeAndIdentity()
    {
        var fixture = new Fixture();
        string prior = fixture.Read();
        Assert(prior != null, "lifetime rejection checks begin from a valid visible readout");

        fixture.OwnsCamera = false;
        Assert(fixture.Read() == null, "stop or killcam ownership hides the prior readout");
        fixture.OwnsCamera = true;
        fixture.Arena = false;
        Assert(fixture.Read() == null, "normal raids cannot display spectator HP");
        fixture.Arena = true;
        fixture.Team = false;
        Assert(fixture.Read() == null, "nonprotected Arena modes cannot display this spectator HP");
        fixture.Team = true;
        fixture.Epoch = 2;
        Assert(fixture.Read() == null, "round epoch drift hides a live old-round body");
        fixture.Epoch = 1;
        fixture.Eligible = false;
        Assert(fixture.Read() == null, "round end hides HP even before camera cleanup");
        fixture.Eligible = true;
        fixture.LocalHealth.IsAlive = true;
        Assert(fixture.Read() == null, "local respawn hides HP before camera cleanup");
        fixture.LocalHealth.IsAlive = false;
        fixture.Local.Destroyed = true;
        Assert(fixture.Read() == null, "destroyed local corpse invalidates lease");
        fixture.Local.Destroyed = false;
        fixture.Local.IsYourPlayer = false;
        Assert(fixture.Read() == null, "nonlocal request body is not an observer lease");
        fixture.Local.IsYourPlayer = true;
        fixture.Bot.Destroyed = true;
        Assert(fixture.Read() == null, "Unity-destroyed target clears readout");
        fixture.Bot.Destroyed = false;
        fixture.Bot.gameObject.activeInHierarchy = false;
        Assert(fixture.Read() == null, "inactive target clears readout");
        fixture.Bot.gameObject.activeInHierarchy = true;
        fixture.Bot.IsYourPlayer = true;
        Assert(fixture.Read() == null, "a local body cannot masquerade as the watched bot");
        fixture.Bot.IsYourPlayer = false;
        fixture.Bot.ProfileId = "drifted";
        Assert(fixture.Read() == null, "body profile drift clears readout");
        fixture.Bot.ProfileId = "ally";
        fixture.Frozen = new Player { ProfileId = "ally", HealthController = fixture.Health };
        Assert(fixture.Read() == null, "same profile on a different frozen body cannot authorize target HP");
        fixture.Frozen = null;
        Assert(fixture.Read() == null, "missing frozen body cannot authorize target HP");
        fixture.Frozen = fixture.Bot;
        fixture.Target = null;
        Assert(fixture.Read() == null, "cleared target hides prior HP");
        fixture.Target = new ArenaSpectatorTarget(fixture.Bot, "ally", "Ally", ArenaSpectatorSide.Ally);
        Assert(fixture.Read() == prior, "restored exact valid lease reads afresh without stale failure state");
        Assert(fixture.Health.Reads == 2, "invalid identities and ended leases never query the bot's native HP");
    }

    private static void InvalidAndUnavailableHealth()
    {
        var fixture = new Fixture();
        Assert(fixture.Read() != null, "unavailable checks begin with a valid HP readout");
        fixture.Bot.HealthController = null;
        Assert(fixture.Read() == null, "missing health controller cannot retain old HP");
        fixture.Bot.HealthController = fixture.Health;
        fixture.Health.ThrowOnRead = true;
        Assert(fixture.Read() == null, "throwing native health read cannot retain old HP or break GUI");
        fixture.Health.ThrowOnRead = false;

        foreach (ValueStruct invalid in new[]
        {
            new ValueStruct { Current = float.NaN, Maximum = 440 },
            new ValueStruct { Current = float.PositiveInfinity, Maximum = 440 },
            new ValueStruct { Current = float.NegativeInfinity, Maximum = 440 },
            new ValueStruct { Current = 300, Maximum = float.NaN },
            new ValueStruct { Current = 300, Maximum = float.PositiveInfinity },
            new ValueStruct { Current = 300, Maximum = float.NegativeInfinity },
            new ValueStruct { Current = -1, Maximum = 440 },
            new ValueStruct { Current = 300, Maximum = 0 },
            new ValueStruct { Current = 300, Maximum = -440 },
            new ValueStruct { Current = 500, Maximum = 440 },
        })
        {
            fixture.Health.Total = invalid;
            Assert(fixture.Read() == "SPECTATING ALLY: Ally\nHP unavailable    LMB/RMB switch",
                "malformed native health must replace old HP with unavailable");
        }

        fixture.Health.Total = new ValueStruct { Current = 350f, Maximum = 440f };
        Assert(fixture.Read().Contains("HP 350 / 440"), "health availability recovers with a fresh valid read");
        fixture.Request = null;
        Assert(fixture.Read() == null, "cleared request clears readout");
    }

    private static void ChangesDuringNativeRead()
    {
        var fixture = new Fixture();
        fixture.Health.AfterRead = () => fixture.Health.IsAlive = false;
        Assert(fixture.Read() == null, "target dying during read cannot display living HP");
        fixture.Health.IsAlive = true;
        fixture.Health.AfterRead = () => fixture.Epoch++;
        Assert(fixture.Read() == null, "round changing during read cannot display old-round HP");
        fixture.Epoch = 1;
        fixture.Health.AfterRead = () => fixture.Bot.HealthController = new FakeHealth();
        Assert(fixture.Read() == null, "controller replacement during read cannot display prior controller HP");
        fixture.Bot.HealthController = fixture.Health;
        fixture.Health.AfterRead = () => fixture.LocalHealth.IsAlive = true;
        Assert(fixture.Read() == null, "respawn during read cannot retain observer HP");
        fixture.LocalHealth.IsAlive = false;
        fixture.Health.AfterRead = null;
        fixture.Request = new ArenaSpectatorRequest(1, () => throw new InvalidOperationException("disposed mode"),
            () => true, fixture.Local, Array.Empty<string>(), () => Array.Empty<ArenaSpectatorTarget>());
        Assert(fixture.Read() == null, "disposed epoch provider fails closed without breaking GUI");
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
    }

    private sealed class Fixture
    {
        public bool Arena = true;
        public bool Team = true;
        public bool OwnsCamera = true;
        public bool Eligible = true;
        public int Epoch = 1;
        public readonly FakeHealth LocalHealth = new() { IsAlive = false };
        public readonly FakeHealth Health = new();
        public readonly Player Local;
        public readonly Player Bot;
        public ArenaSpectatorTarget Target;
        public Player Frozen;
        public ArenaSpectatorRequest Request;

        public Fixture()
        {
            Local = new Player { ProfileId = "local", IsYourPlayer = true, HealthController = LocalHealth };
            Bot = new Player { ProfileId = "ally", HealthController = Health };
            Target = new ArenaSpectatorTarget(Bot, "ally", "Ally", ArenaSpectatorSide.Ally);
            Frozen = Bot;
            Request = new ArenaSpectatorRequest(1, () => Epoch, () => Eligible, Local,
                new[] { "ally" }, () => new[] { Target });
        }

        public string Read() => SpectatorHealthReadout.ReadStatus(Arena, Team, OwnsCamera, Request, Target, Frozen);
    }

    private sealed class FakeHealth : IHealthController
    {
        public bool IsAlive { get; set; } = true;
        public bool ThrowOnRead;
        public ValueStruct Total = new() { Current = 350f, Maximum = 440f };
        public int Reads;
        public EBodyPart LastPart;
        public Action AfterRead;

        public ValueStruct GetBodyPartHealth(EBodyPart bodyPart, bool rounded = false)
        {
            Reads++;
            LastPart = bodyPart;
            if (ThrowOnRead) throw new InvalidOperationException("controller disposed");
            ValueStruct read = Total;
            AfterRead?.Invoke();
            return read;
        }
    }
}
