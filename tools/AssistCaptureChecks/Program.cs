using System.Reflection;
using ArenaMode;
using ArenaMode.Areas;
using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using SPT.Reflection.Patching;

internal static class Program
{
    private static int _checks;
    private static Player _local, _victim, _attacker;
    private static GameWorld _world;
    private static LocalGame _game;
    private static ArenaMatch _match;

    private static void Assert(bool okay, string label)
    {
        _checks++;
        if (!okay) throw new Exception("FAILED: " + label);
    }

    private static Player Body(string id, bool local = false)
    {
        var body = new Player { ProfileId = id, IsYourPlayer = local, IsAI = !local };
        if (!local) body.AIData = new AIData { BotOwner = new BotOwner { GetPlayer = body, BotsGroup = new BotsGroup() } };
        _world.Ever[id] = _world.Alive[id] = body;
        return body;
    }

    private static void Reset(IArenaMode mode = null)
    {
        ArenaAdministrativeDeathGate.Reset();
        ArenaMode.Combat.Fire.ArenaFireRuntime.Owner = ArenaMode.Combat.Fire.ArenaFireRuntime.Victim = null;
        ArenaHitHistory.Reset();
        TeamDeathmatchRelations.Affiliations.Clear();
        _world = new GameWorld();
        _local = Body("local", true); _victim = Body("victim"); _attacker = Body("attacker");
        _world.MainPlayer = _local;
        _game = new LocalGame { LocalPlayer = _local, Status = GameStatus.Started };
        _match = new ArenaMatch { Mode = mode ?? new RoundsMode() };
        Singleton<GameWorld>.Instance = ArenaSession.World = _world;
        Singleton<AbstractGame>.Instance = ArenaSession.Game = _game;
        ArenaSession.IsActive = true;
        ArenaAreaRuntime.IsPending = ArenaMatchService.TeamStartAbortPending = false;
        ArenaMatchService.Current = _match;
    }

    private static void Hit(float delta = -25, Player source = null, EDamageType type = EDamageType.Bullet,
        bool ownerless = false, Player victim = null)
        => ArenaAssistCapture.ObserveHealthChange(victim ?? _victim, delta, new DamageInfo
        {
            DamageType = type, Player = ownerless ? null : new Bridge { iPlayer = source ?? _attacker },
            Damage = 9000, DidArmorDamage = 8000
        });

    private static void Unattributed(string label)
    {
        Assert(_match.Damage.Count == 1 && _match.Damage[0].Damage == 25, label + " preserves actual damage denominator");
        var d = _match.Damage[0];
        Assert(d.AttackerBodyId == 0 && d.AttackerProfileId == null && !d.AttackerIsLocal
            && d.AttackerIsFriendly == null,
            label + " has no invented contributor");
    }

    private static void Positive()
    {
        Reset();
        Hit(); var d = _match.Damage.Single();
        Assert(d.Damage == 25 && d.Damage != 9000, "only actual clamped HP delta counts");
        Assert(d.VictimBodyId == ArenaHitHistory.BodyId(_victim) && d.AttackerBodyId == ArenaHitHistory.BodyId(_attacker), "physical body identities captured");
        Assert(d.AttackerProfileId == "attacker" && !d.AttackerIsLocal && d.AttackerIsFriendly == null, "FFA identity is not a fake team");
        Hit(-7); Hit(-3);
        Assert(_match.Damage.Count == 3 && _match.Damage.Sum(x => x.Damage) == 35, "native body-part spillover counts each HP loss once");
        Reset(); Hit(source: _local);
        Assert(_match.Damage.Single().AttackerIsLocal, "local source recognized by physical body");
        foreach (var type in new[] { EDamageType.Bullet, EDamageType.Blunt, EDamageType.Melee, EDamageType.Explosion, EDamageType.GrenadeFragment })
        {
            Reset(); Hit(type: type);
            Assert(_match.Damage.Single().AttackerBodyId > 0, "direct combat source accepted: " + type);
        }
        Reset(); _attacker.HealthController.IsAlive = false; _world.Alive.Remove(_attacker.ProfileId); Hit(type: EDamageType.GrenadeFragment);
        Assert(_match.Damage.Single().AttackerBodyId == ArenaHitHistory.BodyId(_attacker), "dead exact known thrower can contribute before replacement");
        Reset(); _local.HealthController.IsAlive = false; _world.Alive.Remove(_local.ProfileId); Hit();
        Assert(_match.Damage.Count == 1, "live match continues observing after local death");
    }

    private static void Sources()
    {
        Reset(); Hit(ownerless: true); Unattributed("ownerless source");
        Reset(); Hit(type: EDamageType.Flame); Unattributed("unowned environment fire");
        foreach (bool freeForAll in new[] { false, true })
        {
            Reset(new RoundsMode { FreeForAll = freeForAll });
            var original = _local;
            _local = Body("local", true); _world.MainPlayer = _local; _game.LocalPlayer = _local;
            ArenaMode.Combat.Fire.ArenaFireRuntime.Owner = original;
            ArenaMode.Combat.Fire.ArenaFireRuntime.Victim = _victim;
            Hit(source: original, type: EDamageType.Flame);
            var fire = _match.Damage.Single();
            Assert(fire.AttackerIsLocal && fire.AttackerBodyId == ArenaHitHistory.BodyId(original),
                "owned fire retains local contribution after respawn " + freeForAll);
        }
        foreach (var type in new[] { EDamageType.LightBleeding, EDamageType.HeavyBleeding, EDamageType.Fall, EDamageType.Undefined, EDamageType.Medicine, EDamageType.Landmine })
        {
            Reset(); Hit(type: type); Unattributed("unproven source " + type);
        }
        Reset(); Hit(source: _victim); Unattributed("self damage");
        Reset(); var clone = new Player { ProfileId = _victim.ProfileId }; Hit(source: clone); Unattributed("same-profile replacement self damage");
        Reset(); var old = _attacker; Body(old.ProfileId); Hit(source: old); Unattributed("replaced old physical source");
        Reset(); _attacker.ProfileId = ""; Hit(); Unattributed("missing source identity");
        Reset(new RoundsMode { FreeForAll = false }); Hit(); Unattributed("all-vs-player bot friendly damage");
        Reset(new RoundsMode { FreeForAll = false }); Hit(source: _local);
        Assert(_match.Damage.Single().AttackerIsLocal, "all-vs-player local enemy damage counts");
        Reset(new RoundsMode { FreeForAll = false }); Hit(victim: _local);
        Assert(_match.Damage.Single().AttackerBodyId > 0, "all-vs-player enemy damage on local counts");
    }

    private static void Gates()
    {
        foreach (var diff in new[] { 0f, 1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Reset(); Hit(diff); Assert(_match.Damage.Count == 0, "non-damage delta rejected " + diff);
        }
        foreach (var close in new Action[]
        {
            () => ArenaSession.IsActive = false,
            () => ArenaSession.World = new GameWorld(),
            () => ArenaSession.Game = new LocalGame(),
            () => _game.Status = GameStatus.Stopped,
            () => _game.LocalPlayer = new Player(),
            () => _world.MainPlayer.IsYourPlayer = false,
            () => _local.IsAI = true,
            () => Singleton<AbstractGame>.Instance = new AbstractGame(),
            () => ArenaAreaRuntime.IsPending = true,
            () => ArenaMatchService.TeamStartAbortPending = true,
            () => _victim.HealthController.IsAlive = false,
            () => _world.Alive.Remove(_victim.ProfileId),
            () => Body(_victim.ProfileId),
            () => ((RoundsMode)_match.Mode).AssistScoringOpen = false,
            () => ((RoundsMode)_match.Mode).IsComplete = true,
            () => _match.Mode = new Mode(),
            () => ArenaMatchService.Current = null,
            () => _match.Mode = null
        })
        {
            Reset(); close(); Hit(); Assert(_match.Damage.Count == 0, "exact live arena and body gate " + _checks);
        }
        Reset(); using (ArenaAdministrativeDeathGate.Enter(_victim.ProfileId)) Hit();
        Assert(_match.Damage.Count == 0, "administrative death mutation never enters assist ledger");
        foreach (var mode in new IArenaMode[] { new TacticalMode { AssistScoringOpen = false }, new TeamDeathmatchMode { AssistScoringOpen = false }, new GunGameMode { AssistScoringOpen = false } })
        {
            Reset(mode); Hit(); Assert(_match.Damage.Count == 0, "mode closed before damage admission");
        }
    }

    private static void Teams()
    {
        Reset(new TeamDeathmatchMode());
        TeamDeathmatchRelations.Affiliations[_local] = TeamDeathmatchRelations.Affiliations[_attacker] = EPlayerSide.Usec;
        TeamDeathmatchRelations.Affiliations[_victim] = EPlayerSide.Bear;
        Hit(); Assert(_match.Damage.Single().AttackerIsFriendly == true, "TDM exact friendly affiliation captured");
        _match.Damage.Clear(); _attacker.HealthController.IsAlive = false;
        _world.Alive.Remove(_attacker.ProfileId); _world.Ever.Remove(_attacker.ProfileId); Hit(type: EDamageType.GrenadeFragment);
        Assert(_match.Damage.Single().AttackerBodyId == ArenaHitHistory.BodyId(_attacker), "proven posthumous TDM affiliation survives deregistration");
        _match.Damage.Clear(); TeamDeathmatchRelations.Affiliations[_attacker] = EPlayerSide.Bear; Hit(); Unattributed("TDM friendly fire");
        _match.Damage.Clear(); TeamDeathmatchRelations.Affiliations.Remove(_attacker); Hit(); Unattributed("TDM unknown old thrower");
        Reset(new TeamDeathmatchMode());
        TeamDeathmatchRelations.Affiliations[_local] = TeamDeathmatchRelations.Affiliations[_victim] = EPlayerSide.Usec;
        TeamDeathmatchRelations.Affiliations[_attacker] = EPlayerSide.Bear;
        Hit(); Assert(_match.Damage.Single().AttackerIsFriendly == false, "TDM opponent affiliation captured");
        var tactical = new TacticalMode(); Reset(tactical);
        tactical.Teams[_local] = tactical.Teams[_attacker] = 1; tactical.Teams[_victim] = 2;
        Hit(); Assert(_match.Damage.Single().AttackerIsFriendly == true, "Tactical follower exact opposing body accepted");
        _match.Damage.Clear(); tactical.Teams[_attacker] = 2; Hit(); Unattributed("Tactical same-team damage");
        _match.Damage.Clear(); tactical.Teams.Remove(_attacker); Hit(); Unattributed("Tactical unbound body");
        tactical.Teams[_attacker] = 2; tactical.Teams[_victim] = 1; _match.Damage.Clear(); Hit();
        Assert(_match.Damage.Single().AttackerIsFriendly == false, "Tactical opponent classification uses current exact local");
    }

    private static void GunGame()
    {
        Reset(new GunGameMode()); Hit(); Unattributed("Gun Game unknown hostility");
        _match.Damage.Clear(); _attacker.AIData.BotOwner.BotsGroup.EnemiesReadThrows = true;
        Hit(); Unattributed("Gun Game unavailable native group");
        var warnings = Plugin.ModLogger.Lines.Count;
        Hit(); Hit(); Assert(Plugin.ModLogger.Lines.Count == warnings, "native observation warning is bounded");
        _attacker.AIData.BotOwner.BotsGroup.EnemiesReadThrows = false;
        _match.Damage.Clear(); _attacker.AIData.BotOwner.BotsGroup.Enemies[_victim] = new object(); Hit(); Unattributed("Gun Game one-sided bot hostility");
        _match.Damage.Clear(); _victim.AIData.BotOwner.BotsGroup.Enemies[_attacker] = new object(); Hit();
        Assert(_match.Damage.Single().AttackerBodyId > 0, "Gun Game mutually hostile exact bots qualify");
        _match.Damage.Clear(); _attacker.AIData.BotOwner.BotsGroup.Allies.Add(_victim); Hit(); Unattributed("Gun Game explicit allies override stale enemy entry");
        Reset(new GunGameMode()); _victim.AIData.BotOwner.BotsGroup.Enemies[_local] = new object(); Hit(source: _local);
        Assert(_match.Damage.Single().AttackerIsLocal, "Gun Game explicit local opponent accepted");
        Reset(new GunGameMode()); _attacker.AIData.BotOwner.BotsGroup.Enemies[_local] = new object(); Hit(victim: _local);
        Assert(_match.Damage.Single().AttackerBodyId > 0, "Gun Game hostile bot attacking local accepted");
        Reset(new GunGameMode()); var shared = _attacker.AIData.BotOwner.BotsGroup;
        _victim.AIData.BotOwner.BotsGroup = shared; shared.Enemies[_victim] = shared.Enemies[_attacker] = new object(); Hit();
        Unattributed("Gun Game same-group bots");
    }

    private static MethodInfo Patch(string nested, string method)
        => typeof(ArenaAssistCapture).GetNestedType(nested, BindingFlags.NonPublic)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static);

    private static void Terminal()
    {
        void End(Player killer = null, EDamageType type = EDamageType.Bullet)
        {
            _victim.HealthController.IsAlive = false;
            _world.Alive.Remove(_victim.ProfileId);
            ArenaAssistCapture.PrepareKill(_match, _victim, killer ?? _attacker, type);
        }
        void Cleared(string label) => Assert(_match.Forgotten.Count == 1
            && _match.Forgotten[0] == ArenaHitHistory.BodyId(_victim), label);
        void Retained(string label) => Assert(_match.Forgotten.Count == 0 && _match.Damage.Count == 1, label);

        Reset(new RoundsMode { FreeForAll = false }); Hit(source: _local); End();
        Cleared("all-vs-player friendly bot final cannot cash out assist");
        Reset(new RoundsMode { FreeForAll = false }); Hit(source: _local); End(_local);
        Retained("all-vs-player opposed local final preserves contributors until scoring");
        Reset(); Hit(source: _local); End();
        Retained("FFA opposed bot final preserves contributors until scoring");
        foreach (var type in new[] { EDamageType.HeavyBleeding, EDamageType.Fall, EDamageType.Undefined })
        {
            Reset(); Hit(source: _local); End(type: type);
            Cleared("noncombat final does not borrow retained aggressor: " + type);
        }
        Reset(); Hit(source: _local); End(_victim); Cleared("self final cannot cash out assist");
        Reset(); Hit(source: _local); var oldKiller = _attacker; Body(_attacker.ProfileId); End(oldKiller);
        Cleared("same-profile successor cannot prove replaced final source");
        Reset(); Hit(source: _local); var originalVictim = _victim; Body(_victim.ProfileId); End();
        Cleared("replaced victim final clears only original body ledger");
        Assert(_match.Forgotten[0] != ArenaHitHistory.BodyId(_world.Ever[originalVictim.ProfileId]), "replaced victim cleanup never targets successor identity");
        Reset(); Hit(source: _local); ((RoundsMode)_match.Mode).AssistScoringOpen = false; End();
        Cleared("closed scoring phase cannot cash out assist");
        Reset(); Hit(source: _local); using (ArenaAdministrativeDeathGate.Enter(_victim.ProfileId)) End();
        Cleared("administrative final cannot cash out assist");
        Reset(); Hit(source: _local); ArenaSession.World = new GameWorld(); End();
        Cleared("stale exact-raid context cannot cash out assist");
        Reset(); Hit(source: _local); ArenaSession.IsActive = false; End();
        Cleared("inactive captured match cannot cash out pending assist");
        Reset(); Hit(source: _local); var nextMatch = new ArenaMatch { Mode = new RoundsMode() };
        ArenaMatchService.Current = nextMatch; End();
        Cleared("captured old match cleared when current match changes");
        Assert(nextMatch.Forgotten.Count == 0 && nextMatch.Damage.Count == 0, "terminal rejection never touches new current match");

        Reset(new GunGameMode()); Hit(ownerless: true); End();
        Cleared("Gun Game unproven final hostility cannot cash out assist");
        Reset(new GunGameMode()); _attacker.AIData.BotOwner.BotsGroup.Enemies[_victim] = new object();
        _victim.AIData.BotOwner.BotsGroup.Enemies[_attacker] = new object(); Hit(); End();
        Retained("Gun Game mutual exact hostility survives native victim death for scoring");
        Reset(new GunGameMode()); _attacker.AIData.BotOwner.BotsGroup.Enemies[_victim] = new object();
        _victim.AIData.BotOwner.BotsGroup.Enemies[_attacker] = new object();
        _attacker.AIData.BotOwner.BotsGroup.Allies.Add(_victim); Hit(); End();
        Cleared("Gun Game explicit allied final overrides stale enemy dictionaries");
        Reset(new GunGameMode()); _victim.AIData.BotOwner.BotsGroup.Enemies[_local] = new object();
        Hit(source: _local); End(_local); Retained("Gun Game opposed local final retained");
        Reset(new GunGameMode()); Hit(ownerless: true); _attacker.AIData.BotOwner.BotsGroup.EnemiesReadThrows = true; End();
        Cleared("native terminal relationship failure clears assist conservatively");

        Reset(new TeamDeathmatchMode());
        TeamDeathmatchRelations.Affiliations[_local] = EPlayerSide.Usec;
        TeamDeathmatchRelations.Affiliations[_attacker] = TeamDeathmatchRelations.Affiliations[_victim] = EPlayerSide.Bear;
        Hit(source: _local); End(); Cleared("TDM same-team final cannot cash out assist");
        Reset(new TeamDeathmatchMode());
        TeamDeathmatchRelations.Affiliations[_local] = TeamDeathmatchRelations.Affiliations[_attacker] = EPlayerSide.Usec;
        TeamDeathmatchRelations.Affiliations[_victim] = EPlayerSide.Bear;
        Hit(source: _local); _attacker.HealthController.IsAlive = false;
        _world.Ever.Remove(_attacker.ProfileId); _world.Alive.Remove(_attacker.ProfileId); End(type: EDamageType.GrenadeFragment);
        Retained("TDM proven posthumous hostile final retained");
        var tactical = new TacticalMode(); Reset(tactical);
        tactical.Teams[_local] = 1; tactical.Teams[_victim] = tactical.Teams[_attacker] = 2;
        Hit(source: _local); End(); Cleared("Tactical same-team final cannot cash out assist");
        tactical = new TacticalMode(); Reset(tactical);
        tactical.Teams[_local] = tactical.Teams[_attacker] = 1; tactical.Teams[_victim] = 2;
        Hit(source: _local); End(); Retained("Tactical bound opposing final retained");
    }

    private static void Lifecycle()
    {
        Reset();
        Assert(ModulePatch.Targets.Count == 2 && ModulePatch.Targets.Any(x => x.Name == nameof(Player.OnHealthChanged))
            && ModulePatch.Targets.Any(x => x.Name == nameof(Player.OnDead)), "only HP event and death cleanup are observed");
        var hp = Patch("ArenaAssistCaptureHealthChangedPatch", "Prefix");
        Assert(hp.GetCustomAttribute<PatchPrefixAttribute>() != null, "HP observation is prefix before native callback body");
        hp.Invoke(null, new object[] { _victim, -35f, new DamageInfo { DamageType = EDamageType.Bullet, Player = new Bridge { iPlayer = _attacker } } });
        Assert(_match.Damage.Single().Damage == 35, "lethal event contribution exists before death callback scoring");
        var before = Patch("ArenaAssistCaptureDeathCleanupPatch", "Prefix");
        var finish = Patch("ArenaAssistCaptureDeathCleanupPatch", "Finalizer");
        Assert(finish.GetCustomAttribute<PatchFinalizerAttribute>() != null, "cleanup survives native death callback exceptions");
        var state = new object[] { null }; before.Invoke(null, state);
        Assert(ReferenceEquals(state[0], _match) && _match.Forgotten.Count == 0, "death entry does not clear contributions before scoring");
        var nativeError = new InvalidOperationException("native death error");
        var returned = finish.Invoke(null, new object[] { _victim, nativeError, state[0] });
        Assert(ReferenceEquals(returned, nativeError), "native exception preserved");
        Assert(_match.Forgotten.Single() == ArenaHitHistory.BodyId(_victim), "death finalizer clears only victim after scoring");
        Reset(); var oldMatch = _match; ArenaMatchService.Current = new ArenaMatch { Mode = new RoundsMode() };
        ArenaAssistCapture.FinishDeath(_victim, oldMatch);
        Assert(oldMatch.Forgotten.Count == 0 && ArenaMatchService.Current.Forgotten.Count == 0, "late old-match callback cannot clear new match");
        Reset(); ArenaSession.IsActive = false; ArenaAssistCapture.FinishDeath(_victim, _match);
        Assert(_match.Forgotten.Count == 0, "normal raid cleanup untouched");
    }

    private static void ZombieTeams()
    {
        var zombie = new ZombieMode { IsTeamArena = true };
        Reset(zombie);
        zombie.Teams[_local] = zombie.Teams[_attacker] = 0; zombie.Teams[_victim] = 1;
        Hit(source: _local); Hit(source: _attacker);
        Assert(_match.Damage.Count == 2 && _match.Damage.All(d => d.AttackerIsFriendly == true),
            "local and follower damage contribute to an infected kill");
        _victim.HealthController.IsAlive = false; _world.Alive.Remove(_victim.ProfileId);
        ArenaAssistCapture.PrepareKill(_match, _victim, _attacker, EDamageType.Bullet);
        Assert(_match.Forgotten.Count == 0, "a follower final hit preserves the local assist contribution");
        Reset(zombie); zombie.Teams[_local] = zombie.Teams[_attacker] = 0; zombie.Teams[_victim] = 1;
        _attacker.HealthController.IsAlive = false; _world.Alive.Remove(_attacker.ProfileId);
        Hit(source: _attacker, type: EDamageType.GrenadeFragment);
        Assert(_match.Damage.Single().AttackerIsFriendly == true,
            "a dead follower's delayed grenade retains friendly assist affiliation");
        Reset(zombie); zombie.Teams[_local] = zombie.Teams[_attacker] = zombie.Teams[_victim] = 0;
        Hit(source: _attacker); Unattributed("friendly Zombie team damage");
        Reset(new ZombieMode()); Hit(source: _attacker); Unattributed("solo Zombie still has no follower attribution");
    }

    public static int Main(string[] args)
    {
        try
        {
            ArenaAssistCapture.Enable();
            string suite = args.FirstOrDefault() ?? "all";
            if (suite is "all" or "positive") Positive();
            if (suite is "all" or "sources") Sources();
            if (suite is "all" or "gates") Gates();
            if (suite is "all" or "teams") Teams();
            if (suite is "all" or "gungame") GunGame();
            if (suite is "all" or "lifecycle") Lifecycle();
            if (suite is "all" or "terminal") Terminal();
            if (suite is "all" or "zombie") ZombieTeams();
            Console.WriteLine("ASSIST_CAPTURE_PRODUCTION_PASSED: " + _checks + " assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetBaseException().Message); return 1; }
    }
}
