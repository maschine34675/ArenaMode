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
            CheckFactionPolicy();
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

    private static void CheckFactionPolicy()
    {
        Reset();
        Assert(ArenaTracerTeams.BeginFactionMatch("local"), "faction tracer match binds");
        Assert(ArenaTracerTeams.RegisterFactionActor("friend", true), "validated friendly body registers");
        Assert(ArenaTracerTeams.RegisterFactionActor("enemy", false), "validated opposing body registers");
        Assert(ArenaTracerTeams.Classify("local", true) == ArenaTracerSide.Friendly, "human keeps friendly color through respawn");
        Assert(ArenaTracerTeams.Classify("friend", true) == ArenaTracerSide.Friendly, "same faction has friendly color");
        Assert(ArenaTracerTeams.Classify("enemy", true) == ArenaTracerSide.Opponent, "other faction has opposing color");
        Assert(ArenaTracerTeams.Classify("unadopted", true) == ArenaTracerSide.Unknown, "unowned faction body stays native");
        Assert(ArenaTracerTeams.Classify("friend", false) == ArenaTracerSide.Unknown, "faction shot still needs exact attribution");
        Assert(!ArenaTracerTeams.RegisterFactionActor("friend", false), "dead or live faction identities cannot change sides");
        Assert(!ArenaTracerTeams.RegisterFactionActor("local", false), "human cannot be registered as opponent");
        Assert(ArenaTracerTeams.RegisterFactionActor("replacement", true), "replacement receives own identity");
        Assert(ArenaTracerTeams.Classify("friend", true) == ArenaTracerSide.Friendly, "old projectile retains faction color");
        ArenaTracerTeams.Reset();
        Assert(!ArenaTracerTeams.RegisterFactionActor("friend", true), "registration after teardown fails");
        Assert(ArenaTracerTeams.Classify("friend", true) == ArenaTracerSide.Unknown, "teardown removes faction history");
        ArenaTracerTeams.BeginMatch("local", true);
        Assert(!ArenaTracerTeams.RegisterFactionActor("friend", true), "faction API cannot alter protected Tactical roster");
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
        // Colored tracers are team-only (2026-10-08): Team DM (faction match) is
        // the renderable "solo-style" case, protected Tactical 5v5 follows below,
        // and every solo mode must stay native regardless of TeamTracers.
        Reset();
        ArenaTracerTeams.BeginFactionMatch("local");
        ArenaTracerTeams.RegisterFactionActor("enemy", false);
        ArenaSession.ActiveMode = "teamdeathmatch";
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
        Assert(Settings.RequestedMode == "teamdeathmatch" && !Settings.RequestedTeam,
            "renderer asks for the Team DM tracer entry from the frozen active mode");
        AssertColor(renderer.Color, 0.25f, 1.25f, 2f,
            "local tracer uses friendly cyan");

        Shot enemy = NewShot("enemy", resolvedBridge: true, tracer: false);
        Assert(!InvokePrefix(renderer, enemy),
            "verified Team DM opponent replaces native path");
        Assert(renderer.ShotTracerCalls == 2,
            "normally tracerless opponent ammunition receives a tracer");
        AssertColor(renderer.Color, 2f, 0.235f, 0.235f,
            "opponent tracer uses red");

        Shot unresolved = NewShot("environment", resolvedBridge: false, tracer: false);
        Assert(InvokePrefix(renderer, unresolved),
            "unverified source stays native");

        // Solo modes: shooters are still classified (killfeed names and friendly
        // markers use Classify in every mode), but the tracer stays native because
        // TracerEntry has no entry there, whatever TeamTracers says.
        foreach (string soloMode in new[] { "deathmatch", "gungame", "zombie", "tactical" })
        {
            Reset();
            ArenaTracerTeams.BeginMatch("local", protectedTeamMatch: false);
            ArenaSession.IsActive = true;
            ArenaSession.ActiveMode = soloMode;
            Settings.TeamTracers.Value = true;
            Assert(ArenaTracerTeams.Classify("local", true) == ArenaTracerSide.Friendly
                   && ArenaTracerTeams.Classify("bot", true) == ArenaTracerSide.Opponent,
                "solo shooters stay classified for killfeed and markers: " + soloMode);
            var soloRenderer = new TracersLight();
            Assert(InvokePrefix(soloRenderer, NewShot("local", resolvedBridge: true, tracer: false))
                   && InvokePrefix(soloRenderer, NewShot("bot", resolvedBridge: true, tracer: false))
                   && soloRenderer.ShotTracerCalls == 0 && soloRenderer.GrenadeTracerCalls == 0,
                "renderer stays native in solo mode " + soloMode + " regardless of TeamTracers");
            Assert(Settings.RequestedMode == soloMode && !Settings.RequestedTeam,
                "solo renderer asked for its own mode's entry: " + soloMode);
        }

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
        bool followerStaysNative = InvokePrefix(renderer, oldFollowerFragment);
        Assert(Settings.RequestedMode == "tactical" && Settings.RequestedTeam,
            "renderer selects protected team preference from frozen tracer match");
        Assert(!followerStaysNative,
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
        ArenaSession.ActiveMode = "teamdeathmatch";
        Settings.TeamTracers.Value = true;
        ArenaTracerTeams.BeginFactionMatch("local");
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
        string settings = ReadContractSource(
            root,
            "ARENA_TRACER_SETTINGS_SOURCE",
            "ArenaMode.Client", "Config", "Settings.cs");
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
            "if (!ArenaSession.IsActive || Settings.TracerEntry(ArenaSession.ActiveMode, ArenaTracerTeams.ProtectedTeamMatch)?.Value != true)",
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
        // Colored tracers are team-only like the lasers (2026-10-08): the one
        // TeamTracers entry belongs to Team DM and protected Tactical 5v5 and the
        // per-mode solo settings are gone. The behaviour of the real body is
        // executed by tools/TdmMatchMenuChecks; this pins its shape for the
        // renderer contract the StandIns mirror.
        int entryStart = settings.IndexOf(
            "internal static ConfigEntry<bool> TracerEntry(string mode, bool protectedTeam)",
            StringComparison.Ordinal);
        int entryEnd = entryStart < 0 ? -1 : settings.IndexOf("};", entryStart, StringComparison.Ordinal);
        Assert(entryStart >= 0 && entryEnd > entryStart, "Settings.TracerEntry is still declared");
        string tracerEntry = settings.Substring(entryStart, entryEnd - entryStart);
        RequireAll(tracerEntry,
            "ArenaModeCatalog.TeamDeathmatchId => TeamTracers",
            "ArenaModeCatalog.TacticalId when protectedTeam => TeamTracers",
            "_ => null");
        Assert(CountOccurrences(tracerEntry, "=> TeamTracers") == 2
               && !tracerEntry.Contains("ArenaModeCatalog.DeathmatchId", StringComparison.Ordinal)
               && !tracerEntry.Contains("GunGameId", StringComparison.Ordinal)
               && !tracerEntry.Contains("ZombieId", StringComparison.Ordinal),
            "TracerEntry maps only Team DM and protected Tactical 5v5 to TeamTracers");
        foreach (string retired in new[] { "DeathmatchColoredTracers", "GunGameColoredTracers",
            "TacticalSoloColoredTracers", "ZombieColoredTracers", "SoloColoredTracers" })
            Assert(!settings.Contains(retired, StringComparison.Ordinal),
                "retired per-mode solo tracer setting stays removed: " + retired);
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
        ArenaSession.ActiveMode = "tactical";
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
