using ArenaMode.Session;
using ArenaMode.Modes;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string name)
    {
        _checks++;
        if (!value) throw new InvalidOperationException(name);
    }

    private static void Main()
    {
        ArenaTeamSideSelection.Reset();
        Check(!ArenaTeamSideSelection.SelectedUsec.HasValue, "fresh selection absent");
        foreach (bool native in new[] { true, false })
        foreach (string choice in new[] { "Profile", "USEC", "BEAR" })
        {
            bool expected = choice == "Profile" ? native : choice == "USEC";
            var game = new object();
            var world = new object();
            ArenaTeamSideSelection.Capture("teamdeathmatch", choice, native, "local", 6, "arm6");
            Check(ArenaTeamSideSelection.SelectedUsec == expected, "choice resolves before loading");
            Check(ArenaTeamSideSelection.NativeUsec == native, "native faction preserved separately");
            Check(!ArenaTeamSideSelection.TryResolve(5, "arm6", new object(), new object(), "local", native, out _), "stale epoch cannot bind");
            Check(!ArenaTeamSideSelection.TryResolve(6, "arm5", new object(), new object(), "local", native, out _), "stale token cannot bind");
            Check(!ArenaTeamSideSelection.TryResolve(6, "arm6", new object(), new object(), "other", native, out _), "foreign profile cannot bind");
            Check(!ArenaTeamSideSelection.TryResolve(6, "arm6", new object(), new object(), "local", !native, out _), "native faction drift cannot bind");
            Check(!ArenaTeamSideSelection.TryResolve(6, "arm6", null, world, "local", native, out _), "missing game cannot bind");
            Check(!ArenaTeamSideSelection.TryResolve(6, "arm6", game, null, "local", native, out _), "missing world cannot bind");
            Check(ArenaTeamSideSelection.TryResolve(6, "arm6", game, world, "local", native, out bool selected) && selected == expected,
                "valid pair binds despite earlier invalid attempts");
            Check(ArenaTeamSideSelection.TryResolve(6, "arm6", game, world, "local", native, out selected) && selected == expected,
                "same pair remains valid across respawn");
            Check(!ArenaTeamSideSelection.TryResolve(6, "arm6", new object(), world, "local", native, out _), "replacement game rejected");
            Check(!ArenaTeamSideSelection.TryResolve(6, "arm6", game, new object(), "local", native, out _), "replacement world rejected");
            Check(!ArenaTeamSideSelection.TryResolve(6, "arm6", game, world, "local", !native, out _), "active native faction drift rejected");
            Check(ArenaTeamSideSelection.SelectedUsec == expected, "failed resolutions do not flip frozen side");
            for (int size = 2; size <= 12; size++)
            {
                var population = new TeamDeathmatchPopulation(size, expected);
                Check(population.BotTarget(expected) == size - 1, "selected side reserves human slot");
                Check(population.BotTarget(!expected) == size, "opposing side has full bot capacity");
                Check(population.TryReserve(expected, 0, 0f, out var ticket) && ticket.Usec == expected,
                    "selected team ticket uses faction rather than native profile");
            }
            ArenaTeamSideSelection.Reset();
            Check(!ArenaTeamSideSelection.SelectedUsec.HasValue && !ArenaTeamSideSelection.NativeUsec.HasValue, "reset clears both authorities");
            Check(!ArenaTeamSideSelection.TryResolve(6, "arm6", game, world, "local", native, out _), "old continuation rejected after reset");
            ArenaTeamSideSelection.Capture("teamdeathmatch", expected ? "BEAR" : "USEC", native, "local", 7, "arm7");
            Check(!ArenaTeamSideSelection.TryResolve(6, "arm6", game, world, "local", native, out _), "rearm rejects previous token and epoch");
            Check(ArenaTeamSideSelection.TryResolve(7, "arm7", new object(), new object(), "local", native, out selected) && selected == !expected,
                "new match can choose opposite side");
        }
        foreach (string ordinaryMode in new[] { null, "", "deathmatch", "gungame", "tactical", "zombie" })
        {
            ArenaTeamSideSelection.Capture("teamdeathmatch", "BEAR", true, "local", 8, "arm8");
            ArenaTeamSideSelection.Capture(ordinaryMode, null, true, null, 9, null);
            Check(!ArenaTeamSideSelection.SelectedUsec.HasValue && !ArenaTeamSideSelection.NativeUsec.HasValue,
                "non-TDM capture clears stale side without requesting PMC mutation");
        }
        foreach (string invalid in new[] { null, "", "pmc", "scav", "Random", " USEC " })
        {
            Check(!ArenaTeamSideSelection.TryResolveChoice(invalid, true, out _), "invalid preference rejected");
            bool threw = false;
            try { ArenaTeamSideSelection.Capture("teamdeathmatch", invalid, true, "local", 10, "arm10"); }
            catch (InvalidOperationException) { threw = true; }
            Check(threw && !ArenaTeamSideSelection.SelectedUsec.HasValue, "invalid capture leaves no active team");
        }
        foreach (var identity in new[] { (profile: "", token: "x"), (profile: "local", token: "") })
        {
            bool threw = false;
            try { ArenaTeamSideSelection.Capture("teamdeathmatch", "USEC", false, identity.profile, 11, identity.token); }
            catch (InvalidOperationException) { threw = true; }
            Check(threw && !ArenaTeamSideSelection.SelectedUsec.HasValue, "missing identity fails closed");
        }
        Check(ArenaTeamSideSelection.TryResolveChoice("usec", false, out bool usec) && usec, "case-insensitive usec preference");
        Check(ArenaTeamSideSelection.TryResolveChoice("bear", true, out usec) && !usec, "case-insensitive bear preference");
        Console.WriteLine($"TDMSIDECHECKS_PASSED ({_checks} assertions)");
    }
}
