using ArenaMode;
using ArenaMode.Config;
using ArenaMode.Effects;
using ArenaMode.Session;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using JsonType;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

internal static class Program
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            CheckPalette();
            CheckSoloPolicy();
            CheckProtectedTeamPolicy();
            CheckRosterRejections();
            CheckPatchRegistration();
            CheckPatchRenderingAndGates();
            CheckPatchGeometryAndFailure();
            CheckSourceAndLifecycleContract();
            Console.WriteLine($"ARENA_TRACER_CHECKS_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ARENA_TRACER_CHECKS_FAILED after {_assertions} assertions: {ex}");
            return 1;
        }
    }

    private static void CheckPalette()
    {
        AssertColor(ArenaTracerPalette.Resolve(ArenaTracerColor.Cyan), 0.25f, 1.25f, 2f,
            "friendly cyan is HDR");
        AssertColor(ArenaTracerPalette.Resolve(ArenaTracerColor.Red), 2f, 0.235f, 0.235f,
            "opponent red is HDR");
        AssertColor(ArenaTracerPalette.Resolve((ArenaTracerColor)999), 0.25f, 1.25f, 2f,
            "unknown palette value fails safe to cyan");
    }

    private static void CheckSoloPolicy()
    {
        Reset();
        Assert(!ArenaTracerTeams.BeginMatch("  ", protectedTeamMatch: false),
            "blank local profile cannot bind a tracer match");
        Assert(ArenaTracerTeams.BeginMatch(" local ", protectedTeamMatch: false),
            "solo tracer match binds a normalized local profile");
        Assert(ArenaTracerTeams.Classify("local", verifiedPlayerIdentity: true)
               == ArenaTracerSide.Friendly,
            "verified local profile is friendly");
        Assert(ArenaTracerTeams.Classify("local", verifiedPlayerIdentity: false)
               == ArenaTracerSide.Unknown,
            "known local ID without a matching shot bridge stays native");
        Assert(ArenaTracerTeams.Classify("bot", verifiedPlayerIdentity: true)
               == ArenaTracerSide.Opponent,
            "verified solo bot profile is an opponent");
        Assert(ArenaTracerTeams.Classify("bot", verifiedPlayerIdentity: false)
               == ArenaTracerSide.Unknown,
            "unverified solo source stays native");
        Assert(ArenaTracerTeams.Classify(" ", verifiedPlayerIdentity: true)
               == ArenaTracerSide.Unknown,
            "empty shooter identity stays native");
    }

    private static void CheckProtectedTeamPolicy()
    {
        Reset();
        Assert(ArenaTracerTeams.BeginMatch("local", protectedTeamMatch: true),
            "protected tracer match binds");
        Assert(ArenaTracerTeams.Classify("unknown", verifiedPlayerIdentity: true)
               == ArenaTracerSide.Unknown,
            "protected match never guesses that an unknown profile is an opponent");

        string[] followers = { "f1", "f2", "f3", "f4" };
        string[] opponents = { "e1", "e2", "e3", "e4", "e5" };
        Assert(ArenaTracerTeams.TryBindTeamRound(
                1, "local", followers, opponents, out string problem),
            "exact first-round rosters bind: " + problem);
        foreach (string friendly in followers.Append("local"))
            Assert(ArenaTracerTeams.Classify(friendly, verifiedPlayerIdentity: true)
                   == ArenaTracerSide.Friendly,
                $"verified frozen profile {friendly} is friendly");
        foreach (string opponent in opponents)
            Assert(ArenaTracerTeams.Classify(opponent, verifiedPlayerIdentity: true)
                   == ArenaTracerSide.Opponent,
                $"verified frozen profile {opponent} is opponent");

        string[] nextFollowers = { "f6", "f7", "f8", "f9" };
        string[] nextOpponents = { "e6", "e7", "e8", "e9", "e10" };
        Assert(ArenaTracerTeams.TryBindTeamRound(
                2, "local", nextFollowers, nextOpponents, out problem),
            "second-round rosters bind: " + problem);
        Assert(ArenaTracerTeams.Classify("f1", verifiedPlayerIdentity: true)
               == ArenaTracerSide.Friendly,
            "old friendly projectile retains its side after rebind");
        Assert(ArenaTracerTeams.Classify("e1", verifiedPlayerIdentity: true)
               == ArenaTracerSide.Opponent,
            "old opponent projectile retains its side after rebind");
        Assert(ArenaTracerTeams.Classify("f6", verifiedPlayerIdentity: true)
               == ArenaTracerSide.Friendly,
            "new-round follower is friendly");
        Assert(ArenaTracerTeams.Classify("e10", verifiedPlayerIdentity: true)
               == ArenaTracerSide.Opponent,
            "new-round enemy is opponent");
        Assert(ArenaTracerTeams.ShouldWarnUnknown("stranger"),
            "first protected unknown profile asks for one diagnostic");
        Assert(!ArenaTracerTeams.ShouldWarnUnknown("stranger"),
            "same protected unknown profile cannot spam diagnostics");

        ArenaTracerTeams.BeginMatch("new-local", protectedTeamMatch: false);
        Assert(ArenaTracerTeams.Classify("f1", verifiedPlayerIdentity: false)
               == ArenaTracerSide.Unknown,
            "new match clears historical roster IDs");
    }

    private static void CheckRosterRejections()
    {
        Reset();
        ArenaTracerTeams.BeginMatch("local", protectedTeamMatch: true);
        string[] goodFollowers = { "f1", "f2", "f3", "f4" };
        string[] goodOpponents = { "e1", "e2", "e3", "e4", "e5" };

        Assert(!ArenaTracerTeams.TryBindTeamRound(
                1, "other", goodFollowers, goodOpponents, out _),
            "mismatched local profile is rejected");
        Assert(!ArenaTracerTeams.TryBindTeamRound(
                1, "local", new[] { "f1", "f2", "f3" }, goodOpponents, out _),
            "incomplete follower roster is rejected");
        Assert(!ArenaTracerTeams.TryBindTeamRound(
                1, "local", new[] { "f1", "f1", "f3", "f4" }, goodOpponents, out _),
            "duplicate follower identity is rejected");
        Assert(!ArenaTracerTeams.TryBindTeamRound(
                1, "local", goodFollowers, new[] { "e1", "e2", "e3", "e4", "f1" }, out _),
            "same profile on both teams is rejected");
        Assert(ArenaTracerTeams.TryBindTeamRound(
                1, "local", goodFollowers, goodOpponents, out _),
            "valid roster still binds after rejected candidates");
        Assert(!ArenaTracerTeams.TryBindTeamRound(
                1, "local", goodFollowers, goodOpponents, out _),
            "same or older round epoch is rejected");
        Assert(!ArenaTracerTeams.TryBindTeamRound(
                2,
                "local",
                new[] { "e1", "nf2", "nf3", "nf4" },
                new[] { "ne1", "ne2", "ne3", "ne4", "ne5" },
                out _),
            "profile cannot switch from opponent to friendly between rounds");
        Assert(ArenaTracerTeams.Classify("e1", verifiedPlayerIdentity: true)
               == ArenaTracerSide.Unknown,
            "ambiguous protected rebind disables stale team colors");
        Assert(!ArenaTracerTeams.TryBindTeamRound(
                2,
                "local",
                new[] { "nf1", "nf2", "nf3", "nf4" },
                new[] { "f1", "ne2", "ne3", "ne4", "ne5" },
                out _),
            "profile cannot switch from friendly to opponent between rounds");
    }

    private static void CheckPatchRegistration()
    {
        Reset();
        ArenaTracerPatches.Enable();
        Assert(ModulePatch.EnabledTargets.Count == 1,
            "exactly one team-tracer hook is registered");
        MethodBase target = ModulePatch.EnabledTargets[0];
        Assert(target.DeclaringType == typeof(TracersLight)
               && target.Name == nameof(TracersLight.CreateTracer)
               && target.GetParameters().Select(parameter => parameter.ParameterType)
                   .SequenceEqual(new[] { typeof(Shot) }),
            "hook resolves exact SPT 4.1 TracersLight.CreateTracer(Shot)");
    }

    private static void CheckPatchRenderingAndGates()
    {
        Reset();
        ArenaTracerTeams.BeginMatch("local", protectedTeamMatch: false);
        Shot local = NewShot("local", resolvedBridge: true, tracer: false);
        var renderer = new TracersLight();
        Assert(InvokePrefix(renderer, local),
            "ordinary raid runs complete native tracer method");
        Assert(renderer.ShotTracerCalls == 0, "ordinary raid emits no Arena tracer");

        ArenaSession.IsActive = true;
        Settings.TeamTracers.Value = false;
        Assert(InvokePrefix(renderer, local),
            "disabled setting runs complete native tracer method");

        Settings.TeamTracers.Value = true;
        Assert(!InvokePrefix(renderer, local),
            "known local shot replaces native path");
        Assert(renderer.ShotTracerCalls == 1,
            "normally tracerless local ammunition receives a tracer");
        AssertColor(renderer.Color, 0.25f, 1.25f, 2f,
            "local tracer uses friendly cyan");

        Shot enemy = NewShot("enemy", resolvedBridge: true, tracer: false);
        Assert(!InvokePrefix(renderer, enemy),
            "verified solo opponent replaces native path");
        Assert(renderer.ShotTracerCalls == 2,
            "normally tracerless opponent ammunition receives a tracer");
        AssertColor(renderer.Color, 2f, 0.235f, 0.235f,
            "opponent tracer uses red");

        Shot unresolved = NewShot("environment", resolvedBridge: false, tracer: false);
        Assert(InvokePrefix(renderer, unresolved),
            "unverified solo source stays native");

        Reset();
        ArenaSession.IsActive = true;
        Settings.TeamTracers.Value = true;
        Settings.FriendlyTracerColor.Value = ArenaTracerColor.Green;
        Settings.OpponentTracerColor.Value = ArenaTracerColor.Orange;
        ArenaTracerTeams.BeginMatch("local", protectedTeamMatch: true);
        Assert(ArenaTracerTeams.TryBindTeamRound(
                1,
                "local",
                new[] { "f1", "f2", "f3", "f4" },
                new[] { "e1", "e2", "e3", "e4", "e5" },
                out _),
            "render test protected roster binds");
        renderer = new TracersLight();
        Shot oldFollowerFragment = NewShot("f1", resolvedBridge: true, tracer: false);
        oldFollowerFragment.FragmentIndex = 2;
        Assert(!InvokePrefix(renderer, oldFollowerFragment),
            "frozen follower fragment renders with its retained shot bridge");
        AssertColor(renderer.Color, 0.457f, 1.5f, 0.507f,
            "configured friendly green is used");
        Assert(!InvokePrefix(renderer, NewShot("e1", resolvedBridge: true, tracer: false)),
            "frozen opponent renders with its retained shot bridge");
        AssertColor(renderer.Color, 2f, 0.65f, 0.2f,
            "configured opponent orange is used");

        int rendered = renderer.ShotTracerCalls;
        Assert(InvokePrefix(renderer, NewShot("f2", resolvedBridge: false, tracer: false))
               && renderer.ShotTracerCalls == rendered,
            "known protected ID without its matching shot bridge stays native");
        Shot mismatched = NewShot("f3", resolvedBridge: true, tracer: false);
        mismatched.Player = new TestBridge
        {
            iPlayer = new TestPlayer { ProfileId = "different-profile" },
        };
        Assert(InvokePrefix(renderer, mismatched)
               && renderer.ShotTracerCalls == rendered,
            "known protected ID with a mismatched shot bridge stays native");

        int warnings = Plugin.ModLogger.Warnings.Count;
        Assert(InvokePrefix(renderer, NewShot("unknown", resolvedBridge: true, tracer: true)),
            "unknown protected shooter stays native");
        Assert(InvokePrefix(renderer, NewShot("unknown", resolvedBridge: true, tracer: true)),
            "repeated unknown protected shooter stays native");
        Assert(Plugin.ModLogger.Warnings.Count == warnings + 1,
            "unknown protected shooter logs once per profile");
    }

    private static void CheckPatchGeometryAndFailure()
    {
        Reset();
        ArenaSession.IsActive = true;
        Settings.TeamTracers.Value = true;
        ArenaTracerTeams.BeginMatch("local", protectedTeamMatch: false);
        var renderer = new TracersLight();

        Shot shortHistory = NewShot("local", resolvedBridge: true, tracer: false, addDefaultHistory: false);
        shortHistory.PositionHistory.Add(new Vector3(0f, 0f, 0f));
        Assert(!InvokePrefix(renderer, shortHistory) && renderer.ShotTracerCalls == 0,
            "known shot with fewer than two history points skips both render paths");

        Shot shortFirst = NewShot("local", resolvedBridge: true, tracer: false, addDefaultHistory: false);
        shortFirst.PositionHistory.Add(new Vector3(0f, 0f, 0f));
        shortFirst.PositionHistory.Add(new Vector3(9f, 0f, 0f));
        Assert(!InvokePrefix(renderer, shortFirst) && renderer.ShotTracerCalls == 0,
            "native 10m first-segment guard is preserved");

        Shot laterShort = NewShot("local", resolvedBridge: true, tracer: false, addDefaultHistory: false);
        laterShort.PositionHistory.Add(new Vector3(-1f, 0f, 0f));
        laterShort.PositionHistory.Add(new Vector3(0f, 0f, 0f));
        laterShort.PositionHistory.Add(new Vector3(1f, 0f, 0f));
        Assert(!InvokePrefix(renderer, laterShort) && renderer.ShotTracerCalls == 1,
            "later valid segment keeps native geometry behavior");

        Shot degenerate = NewShot("local", resolvedBridge: true, tracer: false, addDefaultHistory: false);
        degenerate.PositionHistory.Add(new Vector3(0f, 0f, 0f));
        degenerate.PositionHistory.Add(new Vector3(11f, 0f, 0f));
        degenerate.PositionHistory.Add(new Vector3(11f, 0f, 0f));
        Assert(!InvokePrefix(renderer, degenerate) && renderer.ShotTracerCalls == 1,
            "degenerate segment remains suppressed");

        Shot yellow = NewShot("local", resolvedBridge: true, tracer: true);
        ((Ammo)yellow.Ammo).TracerColor = TaxonomyColor.tracerYellow;
        Assert(!InvokePrefix(renderer, yellow)
               && renderer.GrenadeTracerCalls == 1
               && renderer.ShotTracerCalls == 1,
            "native yellow tracer keeps grenade rendering route");

        renderer.ThrowOnCreate = true;
        int warnings = Plugin.ModLogger.Warnings.Count;
        Assert(InvokePrefix(renderer, NewShot("local", resolvedBridge: true, tracer: false)),
            "renderer exception fails open to native path");
        Assert(InvokePrefix(renderer, NewShot("local", resolvedBridge: true, tracer: false)),
            "repeated renderer exception still fails open");
        Assert(Plugin.ModLogger.Warnings.Count == warnings + 1,
            "renderer failure logs once rather than once per frame");
    }

    private static void CheckSourceAndLifecycleContract()
    {
        string root = FindRepositoryRoot();
        string patch = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "Effects", "ArenaTracerPatches.cs"));
        string teams = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "Effects", "ArenaTracerTeams.cs"));
        string settings = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "Config", "Settings.cs"));
        string plugin = File.ReadAllText(Path.Combine(root, "ArenaMode.Client", "Plugin.cs"));
        string matchService = ReadContractSource(
            root,
            "ARENA_TRACER_MATCH_SERVICE_SOURCE",
            "ArenaMode.Client", "Modes", "ArenaMatchService.cs");
        string tactical = ReadContractSource(
            root,
            "ARENA_TRACER_TACTICAL_SOURCE",
            "ArenaMode.Client", "Modes", "TacticalMode.cs");
        string clientProject = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "ArenaMode.Client.csproj"));

        RequireAll(patch,
            "typeof(TracersLight)",
            "nameof(TracersLight.CreateTracer)",
            "new[] { typeof(Shot) }",
            "if (!ArenaSession.IsActive || !Settings.TeamTracers.Value)",
            "ArenaTracerTeams.Classify(",
            "__instance.CreateShotTracer(shot, p0, p1)",
            "__instance.CreateGrenadeTracer(shot, p0, p1)",
            "segmentSqrMagnitude < MinimumSegmentSqrMagnitude",
            "history.Count == 2",
            "return true;");
        Assert(!patch.Contains("if (!ammo.Tracer)", StringComparison.Ordinal)
               && !patch.Contains("AmmoTemplate", StringComparison.Ordinal)
               && !patch.Contains(".Tracer = ", StringComparison.Ordinal)
               && !patch.Contains(".TracerColor = ", StringComparison.Ordinal),
            "client renderer never requires or mutates shared ammo tracer state");
        Assert(!patch.Contains("System.Linq", StringComparison.Ordinal)
               && !patch.Contains("AccessTools.Field", StringComparison.Ordinal),
            "hot renderer path uses neither LINQ nor reflection");
        RequireAll(teams,
            "new(StringComparer.Ordinal)",
            "FriendlyProfileIds.UnionWith(nextFriendly)",
            "OpponentProfileIds.UnionWith(nextOpponents)",
            "if (!verifiedPlayerIdentity",
            "_protectedRosterValid = false",
            "_protectedRosterValid = true",
            "return !_protectedTeamMatch");
        RequireAll(settings,
            "public static ConfigEntry<bool> TeamTracers",
            "public static ConfigEntry<ArenaTracerColor> FriendlyTracerColor",
            "public static ConfigEntry<ArenaTracerColor> OpponentTracerColor",
            "ArenaTracerColor.Cyan",
            "ArenaTracerColor.Red");
        RequireAll(plugin, "Effects.ArenaTracerPatches.Enable()");
        RequireAll(matchService,
            "Effects.ArenaTracerTeams.BeginMatch(game.ProfileId, protectedTeam)",
            "Effects.ArenaTracerTeams.Reset()");
        Assert(CountOccurrences(
                   matchService,
                   "Effects.ArenaTracerTeams.Reset()") == 3,
            "StartMatch failure, raid end and new-raid recovery each own one tracer reset");
        int beginMatch = matchService.IndexOf(
            "Effects.ArenaTracerTeams.BeginMatch(game.ProfileId, protectedTeam)",
            StringComparison.Ordinal);
        int combatReset = matchService.IndexOf(
            "Combat.CombatPatches.ResetRaidState()", beginMatch, StringComparison.Ordinal);
        Assert(beginMatch >= 0 && combatReset > beginMatch,
            "tracer match identity binds before mode startup can raise shots");
        int startCatch = matchService.IndexOf(
            "catch (Exception ex)", beginMatch, StringComparison.Ordinal);
        int startCatchReset = matchService.IndexOf(
            "Effects.ArenaTracerTeams.Reset()", startCatch, StringComparison.Ordinal);
        int partialEnd = matchService.IndexOf(
            "if (partialMatch != null)", startCatch, StringComparison.Ordinal);
        Assert(startCatch >= 0 && startCatchReset > startCatch && partialEnd > startCatchReset,
            "failed StartMatch clears tracer identities before partial mode teardown");
        int raidEnd = matchService.IndexOf(
            "private static void OnRaidEnded()", StringComparison.Ordinal);
        int raidEndReset = matchService.IndexOf(
            "Effects.ArenaTracerTeams.Reset()", raidEnd, StringComparison.Ordinal);
        int takeCurrent = matchService.IndexOf(
            "var match = Current", raidEnd, StringComparison.Ordinal);
        Assert(raidEnd >= 0 && raidEndReset > raidEnd && takeCurrent > raidEndReset,
            "raid end clears tracer identities before its null-match early return");
        int recovery = matchService.IndexOf(
            "internal static void RecoverAtNewRaidStart()", StringComparison.Ordinal);
        int recoveryReset = matchService.IndexOf(
            "Effects.ArenaTracerTeams.Reset()", recovery, StringComparison.Ordinal);
        int recoveryEarlyReturn = matchService.IndexOf(
            "if (Current == null && !TeamStartAbortPending)", recovery, StringComparison.Ordinal);
        Assert(recovery >= 0 && recoveryReset > recovery && recoveryEarlyReturn > recoveryReset,
            "new-raid recovery clears stale identities before its early return");
        RequireAll(tactical,
            "Effects.ArenaTracerTeams.TryBindTeamRound(",
            "_boundTeamFollowerProfileIds",
            "_boundTeamEnemyProfileIds",
            "team tracers stayed native");
        int bind = tactical.IndexOf(
            "Effects.ArenaTracerTeams.TryBindTeamRound(", StringComparison.Ordinal);
        int live = tactical.IndexOf("_phase = Phase.Live", bind, StringComparison.Ordinal);
        Assert(bind >= 0 && live > bind,
            "exact Tactical roster binds before the round becomes live");
        Assert(!clientProject.Contains("ColoredTracers", StringComparison.OrdinalIgnoreCase)
               && !clientProject.Contains("ColorConverter", StringComparison.OrdinalIgnoreCase),
            "reference tracer mods are not dependencies or bundled assets");
    }

    private static Shot NewShot(
        string profileId,
        bool resolvedBridge,
        bool tracer,
        bool addDefaultHistory = true)
    {
        var ammo = new Ammo
        {
            Tracer = tracer,
            TracerColor = TaxonomyColor.tracerRed,
        };
        var shot = new Shot
        {
            Ammo = ammo,
            PlayerProfileID = profileId,
        };
        if (resolvedBridge)
        {
            shot.Player = new TestBridge
            {
                iPlayer = new TestPlayer { ProfileId = profileId },
            };
        }
        if (addDefaultHistory)
        {
            shot.PositionHistory.Add(new Vector3(0f, 0f, 0f));
            shot.PositionHistory.Add(new Vector3(11f, 0f, 0f));
        }
        return shot;
    }

    private static bool InvokePrefix(TracersLight renderer, Shot shot)
    {
        Type nested = typeof(ArenaTracerPatches).GetNestedType(
            "TeamTracerPatch", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TeamTracerPatch missing");
        MethodInfo prefix = nested.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TeamTracerPatch.Prefix missing");
        return (bool)prefix.Invoke(null, new object[] { renderer, shot });
    }

    private static void Reset()
    {
        ArenaSession.IsActive = false;
        Settings.TeamTracers.Value = true;
        Settings.FriendlyTracerColor.Value = ArenaTracerColor.Cyan;
        Settings.OpponentTracerColor.Value = ArenaTracerColor.Red;
        ArenaTracerTeams.Reset();
        Plugin.ModLogger.Warnings.Clear();
        ModulePatch.EnabledTargets.Clear();
    }

    private static string FindRepositoryRoot()
    {
        string overrideRoot = Environment.GetEnvironmentVariable("ARENA_TRACER_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot)
            && File.Exists(Path.Combine(overrideRoot, "ArenaMode.sln")))
        {
            return Path.GetFullPath(overrideRoot);
        }

        DirectoryInfo directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ArenaMode.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("ArenaMode.sln not found above harness output");
    }

    private static string ReadContractSource(
        string root,
        string overrideVariable,
        params string[] relativePath)
    {
        string overridePath = Environment.GetEnvironmentVariable(overrideVariable);
        return File.ReadAllText(string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(new[] { root }.Concat(relativePath).ToArray())
            : overridePath);
    }

    private static int CountOccurrences(string source, string fragment)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(fragment, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }
        return count;
    }

    private static void RequireAll(string source, params string[] fragments)
    {
        foreach (string fragment in fragments)
            Assert(source.Contains(fragment, StringComparison.Ordinal),
                "production source retains required contract: " + fragment);
    }

    private static void AssertColor(Color actual, float r, float g, float b, string message)
    {
        AssertNear(actual.r, r, message + " r");
        AssertNear(actual.g, g, message + " g");
        AssertNear(actual.b, b, message + " b");
        AssertNear(actual.a, 1f, message + " a");
    }

    private static void AssertNear(float actual, float expected, string message)
        => Assert(Math.Abs(actual - expected) < 0.0001f,
            $"{message} (expected {expected}, got {actual})");

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
