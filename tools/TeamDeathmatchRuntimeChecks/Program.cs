using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Effects;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void Check(bool result, string description)
    {
        if (!result) throw new InvalidOperationException(description);
        _checks++;
    }

    private sealed class Raid
    {
        internal readonly LocalGame Game = new();
        internal readonly GameWorld World = new();
        internal readonly TeamDeathmatchMode Mode = new();
        internal readonly ArenaMatch Match;
        internal Player Local => World.MainPlayer;
        internal List<BotOwner> Bots => Game.BotsController.BotSpawner._bots.BotOwners;
        internal Raid(bool usec = true, int size = 5, bool active = true,
            string mode = "teamdeathmatch", bool pft = false, EPlayerSide? localSide = null)
        {
            Time.time = 0;
            Model.Events.Clear();
            BurstSpawnService.Requests.Clear();
            BurstSpawnService.OnRequest = null;
            ArenaMode.Plugin.ModLogger.Lines.Clear();
            Singleton<AbstractGame>.Instance = Game;
            Singleton<IBotGame>.Instance = Game;
            Singleton<GameWorld>.Instance = World;
            ArenaSession.IsActive = active;
            ArenaSession.ActiveMode = mode;
            ArenaSession.OwnedGame = Game;
            ArenaSession.OwnedWorld = World;
            ArenaMode.Compat.PitFireTeamCompat.HasFrozenTeamRoster = pft;
            ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress = false;
            Settings.TeamDeathmatchSize.Value = size;
            Settings.NoCorpseLooting.Value = true;
            TeamDeathmatchRelations.Reset();
            ArenaTracerTeams.Reset();
            ArenaAdministrativeDeathGate.Reset();
            Model.Body("local", localSide ?? (usec ? EPlayerSide.Usec : EPlayerSide.Bear));
            Match = new ArenaMatch { Scoreboard = new ArenaScoreboard("local", "You", "PMC") };
            Mode.OnMatchStart(Match);
        }
        internal void Step(float at) { Time.time = at; Mode.Tick(1f); }
        internal int Alive(EPlayerSide side) => Bots.Count(bot => bot.HealthController.IsAlive && bot.Profile.Info.Side == side);
        internal void Fill()
        {
            BurstSpawnService.OnRequest = request =>
            {
                Deliver(request, EBotState.Active);
                // Real EFT may invoke observer before dispatch returns.
                request.DispatchFinished(true);
            };
            for (int tick = 0; tick < Mode.TeamSize + 2; tick++) Step(Time.time + 1);
        }
    }

    private static BotOwner Deliver(SpawnRequest request, EBotState state = EBotState.PreActive,
        string id = null, EPlayerSide? actualSide = null, Action<BotOwner> change = null)
    {
        EPlayerSide side = actualSide ?? (request.Role == WildSpawnType.pmcUSEC ? EPlayerSide.Usec : EPlayerSide.Bear);
        var player = Model.Body(id ?? "bot-" + ++Model.NextId, side, true, request.World, request.Game);
        var owner = player.AIData.BotOwner;
        owner.BotState = state;
        change?.Invoke(owner);
        request.Delivered = true;
        request.Observer(owner);
        return owner;
    }

    private static SpawnRequest Request(EPlayerSide side)
        => BurstSpawnService.Requests.Last(request => !request.Delivered
            && (request.Role == WildSpawnType.pmcUSEC) == (side == EPlayerSide.Usec));

    private static void InitializationAndReady()
    {
        foreach (bool usec in new[] { true, false })
        foreach (int size in new[] { 2, 5, 12 })
        {
            var raid = new Raid(usec, size);
            Check(!raid.Mode.IsComplete && raid.Mode.ControlsOwnSpawns && !raid.Mode.HoldRespawn
                && raid.Mode.DeathReplaySeconds == 0, "TDM starts as independent continuous instant-respawn mode");
            Check(raid.Game.BotsController.BotSpawner.MaxBots == 2 * size - 1,
                "native cap reserves the human slot");
            raid.Fill();
            Check(raid.Alive(usec ? EPlayerSide.Usec : EPlayerSide.Bear) == size - 1,
                "human capacity: local faction has exactly N minus one bots");
            Check(raid.Alive(usec ? EPlayerSide.Bear : EPlayerSide.Usec) == size,
                "opposing faction has exactly N bots");
            Check(raid.Match.Cues.Count == 1 && raid.Match.Cues[0] == ArenaAnnouncerCue.RoundStart,
                "synchronous arrivals and later ticks announce ready exactly once");
            Check(raid.Match.Scoreboard.Count(ArenaScoreboardSide.Friendly, true) == size
                && raid.Match.Scoreboard.Count(ArenaScoreboardSide.Opponent, true) == size,
                "real mode populates real scoreboard with both complete factions");
            int before = BurstSpawnService.Requests.Count;
            raid.Step(Time.time + 1);
            Check(BurstSpawnService.Requests.Count == before, "full factions never request extra bots");
        }
        foreach (var setup in new (bool Active, string Mode, bool Pft, EPlayerSide Side)[]
        {
            (false, "teamdeathmatch", false, EPlayerSide.Usec),
            (true, "endless", false, EPlayerSide.Usec),
            (true, "teamdeathmatch", true, EPlayerSide.Usec),
            (true, "teamdeathmatch", false, EPlayerSide.Savage)
        })
        {
            var raid = new Raid(active: setup.Active, mode: setup.Mode, pft: setup.Pft, localSide: setup.Side);
            raid.Step(1);
            Check(raid.Mode.IsComplete && BurstSpawnService.Requests.Count == 0,
                "normal, foreign mode, selected PFT and non-PMC starts cannot spawn TDM actors");
        }
    }

    private static void LoadingAndPreActiveOccupancy()
    {
        var raid = new Raid(size: 2);
        raid.Step(0);
        Check(BurstSpawnService.Requests.Count == 2, "first tick starts one request per faction");
        var usecRequest = Request(EPlayerSide.Usec);
        var bearRequest = Request(EPlayerSide.Bear);
        raid.Step(50);
        Check(BurstSpawnService.Requests.Count == 2, "expired unresolved profile loads remain single-flight");
        usecRequest.DispatchFinished(false);
        bearRequest.DispatchFinished(false);
        raid.Step(51);
        Check(BurstSpawnService.Requests.Count == 2, "confirmed failures wait for retry cooldown");
        raid.Step(55);
        var usec = Deliver(Request(EPlayerSide.Usec));
        var firstBear = Deliver(Request(EPlayerSide.Bear));
        Check(usec.HealthController.IsAlive && firstBear.HealthController.IsAlive,
            "PreActive physical arrivals are admitted before activation");
        int before = BurstSpawnService.Requests.Count;
        raid.Step(56);
        Check(BurstSpawnService.Requests.Count == before + 1
            && BurstSpawnService.Requests[^1].Role == WildSpawnType.pmcBEAR,
            "PreActive immediately occupies the only friendly bot slot");
        var secondBear = Deliver(Request(EPlayerSide.Bear));
        raid.Step(57);
        Check(raid.Match.Cues.Count == 0, "physical fullness alone cannot announce combat readiness");
        usec.BotState = firstBear.BotState = secondBear.BotState = EBotState.Active;
        raid.Step(58);
        Check(raid.Match.Cues.Count == 1, "ready waits until every admitted body reaches Active");
    }

    private static void HumanDeathRespawnAndAwareness()
    {
        foreach (bool usec in new[] { true, false })
        {
            var raid = new Raid(usec, 3);
            raid.Fill();
            var oldLocal = raid.Local;
            oldLocal.HealthController.IsAlive = false;
            raid.World.Players.Remove(oldLocal.ProfileId);
            raid.Mode.OnLocalDeath();
            int before = BurstSpawnService.Requests.Count;
            Model.Events.Clear();
            raid.Step(Time.time + 1);
            Check(BurstSpawnService.Requests.Count == before, "human death never frees an AI team slot");
            Check(Model.Events.Count(item => item.StartsWith("awareness:")) == raid.Bots.Count,
                "both factions receive opposing bot knowledge while human is dead");
            var replacement = Model.Body("local", oldLocal.Profile.Info.Side);
            raid.Mode.OnLocalRespawn(replacement);
            raid.Step(Time.time + 1);
            Check(!raid.Mode.IsComplete && raid.Match.Scoreboard.Local.IsActive,
                "same-profile replacement rejoins active scoreboard and mode");
            foreach (var friend in raid.Bots.Where(bot => bot.Profile.Info.Side == replacement.Profile.Info.Side))
                Check(friend.BotsGroup.Allies.Contains(replacement) && !friend.BotsGroup.Allies.Contains(oldLocal),
                    "friendly groups replace the exact old local body");
            foreach (var enemy in raid.Bots.Where(bot => bot.Profile.Info.Side != replacement.Profile.Info.Side))
                Check(!enemy.BotsGroup.Enemies.ContainsKey(oldLocal), "old local enemy keys are removed before new awareness");
            Check(!TeamDeathmatchRelations.ShouldBlockFriendlyDamage(replacement, oldLocal),
                "old self-thrown projectile keeps self-damage semantics across local respawn");
        }
        var changed = new Raid();
        changed.Local.Profile.Info.Side = EPlayerSide.Bear;
        changed.Step(1);
        Check(changed.Mode.IsComplete && BurstSpawnService.Requests.Count == 0,
            "same-body local faction mutation stops before spawning");

        var rebuilding = new Raid();
        rebuilding.Fill();
        rebuilding.Local.HealthController.IsAlive = false;
        ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress = true;
        var partial = Model.Body("local", EPlayerSide.Usec);
        Health finalHealth = partial.HealthController;
        partial.HealthController = null;
        rebuilding.Step(Time.time + 1);
        Check(!rebuilding.Mode.IsComplete,
            "intermediate local body published during respawn must not be adopted before readiness");
        partial.HealthController = finalHealth;
        ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress = false;
        rebuilding.Mode.OnLocalRespawn(partial);
        rebuilding.Step(Time.time + 1);
        Check(!rebuilding.Mode.IsComplete && rebuilding.Match.Scoreboard.Local.IsActive,
            "completed local respawn rejoins after the intermediate phase");
    }

    private static void RejectionsAndRetirement()
    {
        foreach (string failure in new[] { "faction", "role", "foreign-group", "mixed-group" })
        {
            var raid = new Raid();
            raid.Step(0);
            var request = Request(EPlayerSide.Usec);
            var bot = Deliver(request, change: owner =>
            {
                if (failure == "faction") owner.GetPlayer.Profile.Info.Side = EPlayerSide.Bear;
                if (failure == "role") owner.GetPlayer.Profile.Info.Settings.Role = WildSpawnType.assault;
                if (failure == "foreign-group") owner.BotsGroup.BotGame = new LocalGame();
                if (failure == "mixed-group")
                    owner.BotsGroup.Members.Add(Model.Body("mixed", EPlayerSide.Bear, true).AIData.BotOwner);
            });
            Check(!bot.HealthController.IsAlive && bot.HealthController.KillCalls == 1,
                "invalid " + failure + " arrival is retired before adoption");
            Check(raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Friendly).Kills == 0
                && !ArenaAdministrativeDeathGate.IsActive(bot.ProfileId),
                "administrative retirement earns no combat points and releases its scope");
        }
        foreach (bool throwOnKill in new[] { false, true })
        {
            var raid = new Raid();
            var unowned = Model.Body("unowned", EPlayerSide.Usec, true).AIData.BotOwner;
            unowned.GetPlayer.HealthController.ThrowOnKill = throwOnKill;
            unowned.GetPlayer.HealthController.SuppressKill = !throwOnKill;
            raid.Step(0);
            Check(raid.Mode.IsComplete && BurstSpawnService.Requests.Count == 0,
                "retirement failure stops refill even when native Kill silently does nothing");
            Check(!ArenaAdministrativeDeathGate.IsActive(unowned.ProfileId), "failed retirement releases administrative scope");
        }
        var successful = new Raid();
        var outsider = Model.Body("outsider", EPlayerSide.Bear, true).AIData.BotOwner;
        successful.Step(0);
        Check(!outsider.HealthController.IsAlive && !successful.Mode.IsComplete,
            "unexpected living native owners are removed without stopping a healthy match");
        Check(Model.Events.FindIndex(value => value == "kill:outsider")
            < Model.Events.FindIndex(value => value.StartsWith("request:")),
            "unexpected bodies retire before any refill request");
        var unreadable = new Raid();
        unreadable.Game.BotsController.BotSpawner._bots.BotOwners = null;
        unreadable.Step(0);
        Check(BurstSpawnService.Requests.Count == 0, "unreadable native population never means empty");

        var preflight = new Raid();
        BurstSpawnService.OnRequest = request =>
        {
            request.RejectedBeforePlan = true;
            request.DispatchFinished(false);
        };
        preflight.Step(0);
        preflight.Step(4);
        Check(BurstSpawnService.Requests.Count == 2, "synchronous preflight refusal cannot spin on every frame");
        preflight.Step(5);
        Check(BurstSpawnService.Requests.Count == 4, "confirmed preflight refusal becomes retryable after bounded delay");
    }

    private static void ActivationLifetime()
    {
        foreach (var state in new[] { EBotState.PreActive, EBotState.ActiveFail, EBotState.Disposed, EBotState.NonActive })
        {
            var raid = new Raid(size: 2);
            raid.Step(0);
            var bot = Deliver(Request(EPlayerSide.Usec), state);
            raid.Step(state == EBotState.PreActive ? 44 : 1);
            Check(bot.HealthController.IsAlive == (state == EBotState.PreActive),
                "only bounded PreActive is allowed to retain a healthy unready body");
            if (state == EBotState.PreActive) raid.Step(45);
            Check(!bot.HealthController.IsAlive && !raid.Mode.IsComplete,
                "failed or timed-out activation retires its exact body");
            int before = BurstSpawnService.Requests.Count;
            raid.Step(Time.time + 4);
            Check(BurstSpawnService.Requests.Count == before, "activation retry respects cooldown");
            raid.Step(Time.time + 1);
            Check(BurstSpawnService.Requests.Count == before + 1
                && BurstSpawnService.Requests[^1].Role == WildSpawnType.pmcUSEC,
                "confirmed retired activation frees only its faction slot");
        }
        var bounded = new Raid(size: 2);
        bounded.Step(0);
        for (int failure = 0; failure < 3; failure++)
        {
            Deliver(Request(EPlayerSide.Usec), EBotState.ActiveFail);
            bounded.Step(Time.time + 1);
            if (failure != 2) bounded.Step(Time.time + 5);
        }
        int stopped = BurstSpawnService.Requests.Count;
        bounded.Step(Time.time + 100);
        Check(bounded.Mode.IsComplete && BurstSpawnService.Requests.Count == stopped,
            "three consecutive activation failures stop further native growth");
    }

    private static void ExactRaidAndLateCallbacks()
    {
        var oldRaid = new Raid();
        oldRaid.Step(0);
        var delayed = Request(EPlayerSide.Usec);
        var nextRaid = new Raid();
        int before = BurstSpawnService.Requests.Count;
        oldRaid.Step(10);
        Check(BurstSpawnService.Requests.Count == before, "old mode cannot tick into new exact game/world");
        var oldBody = Deliver(delayed);
        Check(!oldBody.HealthController.IsAlive && nextRaid.World.Players.Count == 1,
            "late old-world observer cannot adopt a bot into the next raid");
        var raid = new Raid();
        raid.Step(0);
        var request = Request(EPlayerSide.Usec);
        request.DispatchFinished(true);
        raid.Step(45);
        var late = Deliver(request);
        Check(!late.HealthController.IsAlive, "expired dispatched observer is retired despite a still-active raid");
        var pending = Request(EPlayerSide.Bear);
        raid.Mode.OnMatchEnd();
        int count = BurstSpawnService.Requests.Count;
        Deliver(pending);
        raid.Step(100);
        Check(BurstSpawnService.Requests.Count == count, "match end invalidates detached observers and refill");
    }

    private static void NativeFriendshipAndDamagePolicy()
    {
        var raid = new Raid(size: 3);
        raid.Fill();
        var friend = raid.Bots.First(bot => bot.Profile.Info.Side == EPlayerSide.Usec);
        var enemy = raid.Bots.First(bot => bot.Profile.Info.Side == EPlayerSide.Bear);
        foreach (var cause in Enum.GetValues<EBotEnemyCause>())
        {
            Check(!friend.BotsGroup.AddEnemy(raid.Local, cause),
                "native friendly admission rejects initial, revenge and direct causes");
            Check(friend.BotsGroup.AddEnemy(enemy, cause), "native opposing faction admission stays available");
        }
        var fake = new BotGroupEnemyInfo { Player = raid.Local, _botGroup = friend.BotsGroup };
        friend.Memory.AddEnemy(raid.Local, fake, false);
        Check(!friend.EnemiesController.EnemyInfos.ContainsKey(raid.Local), "direct memory bypass cannot add a teammate");
        var friendlyInfo = new EnemyInfo { Person = raid.Local, GroupInfo = fake, Owner = friend, GroupOwner = friend.BotsGroup };
        friend.Memory.GoalEnemy = friendlyInfo;
        Check(friend.Memory.GoalEnemy == null, "goal setter rejects stale friendly enemy info");
        friend.BotsGroup.Enemies[raid.Local] = fake; // seed preexisting bad relation, bypassing normal admission
        friend.EnemiesController.EnemyInfos[raid.Local] = friendlyInfo;
        raid.Step(Time.time + 1);
        Check(!friend.BotsGroup.Enemies.ContainsKey(raid.Local)
            && !friend.EnemiesController.EnemyInfos.ContainsKey(raid.Local)
            && friend.BotsGroup.Allies.Contains(raid.Local), "reconciliation removes group and memory remnants before adding friendship");
        Check(TeamDeathmatchRelations.ShouldBlockFriendlyDamage(raid.Local, friend)
            && TeamDeathmatchRelations.ShouldBlockFriendlyDamage(friend.GetPlayer, raid.Local),
            "attributed friendly-fire policy works in both directions and with BotOwner wrapper");
        Check(!TeamDeathmatchRelations.ShouldBlockFriendlyDamage(raid.Local, enemy)
            && !TeamDeathmatchRelations.ShouldBlockFriendlyDamage(raid.Local, null),
            "opponent and unattributed damage fail open");
        friend.HealthController.IsAlive = false;
        raid.World.Players.Remove(friend.ProfileId);
        Check(TeamDeathmatchRelations.ShouldBlockFriendlyDamage(raid.Local, friend.GetPlayer),
            "dead admitted teammate retains projectile affiliation");
        Check(TeamDeathmatchRelations.AreOpponents(enemy, friend), "dead aggressor can still earn valid opposing-faction kill");
        var foreign = new Player { ProfileId = friend.ProfileId, IsAI = true };
        Check(TeamDeathmatchRelations.Faction(foreign) == null,
            "same profile id on an unknown physical body does not inherit faction");
        ArenaSession.IsActive = false;
        Check(!TeamDeathmatchRelations.ShouldBlockFriendlyDamage(raid.Local, friend),
            "ordinary raid gate overrides known old affiliation");
        Check(friend.BotsGroup.AddEnemy(raid.Local, EBotEnemyCause.byKill), "ordinary raid retains native hostility mutations");
    }

    private static ArenaKill Kill(string killer, string victim, bool local = false)
        => new() { KillerProfileId = killer, VictimProfileId = victim, VictimRole = ArenaRole.Pmc,
            KillerName = killer, VictimName = victim, KilledByLocal = local, AwardEligible = true,
            KillerBodyId = 1, VictimBodyId = 2, SingleHitHeadshot = true, Headshot = true };

    private static void ScoreAwardsTracersAndLoot()
    {
        var raid = new Raid(size: 3);
        raid.Fill();
        var friend = raid.Bots.First(bot => bot.Profile.Info.Side == EPlayerSide.Usec);
        var enemy = raid.Bots.First(bot => bot.Profile.Info.Side == EPlayerSide.Bear);
        var awards = new ArenaCombatAwards();
        var teamkill = Kill("local", friend.ProfileId, true);
        raid.Mode.ClassifyKill(teamkill);
        raid.Match.Scoreboard.ReportKill(teamkill);
        Check(!teamkill.ScoreEligible && !teamkill.AwardEligible
            && awards.ReportKill(teamkill, "KILLING SPREE") == null,
            "mode suppresses teamkill points and combat awards before downstream consumers");
        Check(raid.Match.Scoreboard.Local.Kills == 0 && raid.Match.Scoreboard.Local.Score == 0,
            "same-team kill cannot add points, headshot or streak bonuses");
        var opposing = Kill("local", enemy.ProfileId, true);
        raid.Mode.ClassifyKill(opposing);
        raid.Match.Scoreboard.ReportKill(opposing);
        Check(opposing.ScoreEligible && opposing.AwardEligible && awards.ReportKill(opposing, null) == "FIRST BLOOD",
            "suppressed friendly fire does not consume First Blood");
        Check(raid.Match.Scoreboard.Local.Kills == 1 && raid.Match.Scoreboard.Local.Score == 250,
            "opposing PMC headshot preserves production score rules");
        friend.HealthController.IsAlive = false;
        raid.World.Players.Remove(friend.ProfileId);
        raid.Step(Time.time + 1);
        var late = Kill(friend.ProfileId, enemy.ProfileId);
        raid.Mode.ClassifyKill(late);
        Check(late.ScoreEligible && late.KillerIsFriendly == true,
            "mode scalar affiliation survives removal of a dead aggressor's physical body");
        Check(ArenaTracerTeams.Classify(friend.ProfileId, true) == ArenaTracerSide.Friendly
            && ArenaTracerTeams.Classify(enemy.ProfileId, true) == ArenaTracerSide.Opponent
            && ArenaTracerTeams.Classify("foreign", true) == ArenaTracerSide.Unknown,
            "production tracer roster retains known dead shooter colors and leaves unknown shots native");
        var unknown = Kill("foreign", enemy.ProfileId);
        raid.Mode.ClassifyKill(unknown);
        Check(!unknown.ScoreEligible && !unknown.AwardEligible, "unknown shooters cannot gain TDM awards or score");
        var loot = new BotDeadBodyWork { _owner = friend };
        friend.BotsGroup.AddAlly(raid.Local);
        loot.StartLook();
        Check(loot.NativeStarts == 0, "TDM friendly bots do not inherit commandable follower looting exception");
        ArenaSession.ActiveMode = "tactical";
        loot.StartLook();
        Check(loot.NativeStarts == 1, "ordinary allied Tactical follower looting exception remains");
        ArenaSession.ActiveMode = "teamdeathmatch";
        Settings.NoCorpseLooting.Value = false;
        loot.StartLook();
        Check(loot.NativeStarts == 2, "disabled no-looting preference remains respected");
        raid.Mode.OnMatchEnd();
        var ended = Kill("local", enemy.ProfileId, true);
        raid.Mode.ClassifyKill(ended);
        Check(!ended.ScoreEligible && !ended.AwardEligible, "ended match does not score delayed callbacks");
    }

    private static void Main()
    {
        TeamDeathmatchRelations.Enable();
        new NoCorpseLootingPatch().Enable();
        InitializationAndReady();
        LoadingAndPreActiveOccupancy();
        HumanDeathRespawnAndAwareness();
        RejectionsAndRetirement();
        ActivationLifetime();
        ExactRaidAndLateCallbacks();
        NativeFriendshipAndDamagePolicy();
        ScoreAwardsTracersAndLoot();
        Console.WriteLine($"PASS: {_checks} production TDM mode/relations runtime-model checks.");
        Console.WriteLine("ARENA_TDM_RUNTIME_CHECKS_PASSED");
    }
}
