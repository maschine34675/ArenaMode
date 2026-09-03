using ArenaMode.BigBrainCompat;
using ArenaMode.Bots;
using ArenaMode.Session;
using EFT;

internal static class Program
{
    private const string SourceProfile = "arena-source";
    private const string TargetProfile = "round-2-target";
    private const int CustomActionId = 0x5A17;
    private static int _assertions;

    private static int Main()
    {
        try
        {
            VerifyCurrentDictionaryKeyWinsOverStaleEnemySnapshot();
            VerifyBotOwnerKeyedTargetIsUsable();
            VerifyExactGoalWinsWhenBothWrappersShareTheProfile();
            VerifyLeaseSurvivesGoalDriftAndRebindsExactTarget();
            VerifyGoalPersonMustBeTheExactDictionaryKey();
            VerifyEnemyInfoMustBelongToTheSourceBotAndGroup();
            VerifyGroupInfoMustBelongToTheCurrentGroup();
            VerifyGroupPlayerMustBeCanonicalAndAlive();
            VerifyReplacedGroupEntryFailsClosed();
            VerifyStaleEnemySnapshotCannotImpersonateCurrentKey();
            VerifyMissingAndDeadTargetsFailClosed();
            VerifySourceBodyMustBeCanonical();
            VerifyInactiveMedicalSignalsDoNotBlockPursuit();
            VerifyActiveMedicalWorkBlocksPursuit();
            VerifyPursuitResumesWhenActiveHealingEnds();
            Console.WriteLine(
                $"TEAM_ARENA_PURSUIT_RUNTIME_CHECKS_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"TEAM_ARENA_PURSUIT_RUNTIME_CHECKS_FAILED after {_assertions} assertions: {exception.Message}");
            return 1;
        }
        finally
        {
            TeamArenaPursuitLeaseBridge.Reset();
            ArenaSession.IsActive = false;
        }
    }

    private static void VerifyBotOwnerKeyedTargetIsUsable()
    {
        BotOwner source = CreateBotWithLease(TargetProfile);
        (BotOwner targetOwner, TestPlayer targetPlayer, EnemyInfo enemy) = AddBotOwnerEnemy(
            source,
            capturedProfile: "round-1-target",
            currentProfile: TargetProfile);
        source.Memory.GoalEnemy = enemy;

        Assert(ReferenceEquals(enemy.Person, targetOwner)
               && ReferenceEquals(enemy.GroupInfo.Player, targetPlayer)
               && ReferenceEquals(targetOwner.GetPlayer, targetPlayer)
               && source.EnemiesController.EnemyInfos.ContainsKey(targetOwner)
               && source.BotsGroup.Enemies.ContainsKey(targetOwner),
            "the target is registered through a real BotOwner IPlayer key and its canonical Player");
        Assert(enemy.ProfileId == "round-1-target"
               && targetOwner.ProfileId == TargetProfile
               && targetPlayer.ProfileId == TargetProfile,
            "the BotOwner-keyed target models a rebound profile while EnemyInfo retains its old snapshot");
        AssertUsable(source,
            "a coherent BotOwner-keyed target resolves through its current canonical Player");
    }

    private static void VerifyExactGoalWinsWhenBothWrappersShareTheProfile()
    {
        BotOwner bot = CreateBotWithLease(TargetProfile);

        // Player and BotOwner can coexist as distinct IPlayer keys for one AI
        // profile. The earlier entry must not hide the exact GoalEnemy entry.
        (_, EnemyInfo otherWrapper) = AddEnemy(bot, TargetProfile, TargetProfile);
        (_, _, EnemyInfo currentGoal) = AddBotOwnerEnemy(
            bot,
            TargetProfile,
            TargetProfile);
        bot.Memory.GoalEnemy = currentGoal;

        Assert(!ReferenceEquals(otherWrapper, currentGoal),
            "the duplicate-profile setup contains two distinct enemy wrappers");
        AssertUsable(bot,
            "the exact GoalEnemy remains usable when another wrapper with the same profile was inserted first");
    }

    private static void VerifyLeaseSurvivesGoalDriftAndRebindsExactTarget()
    {
        BotOwner bot = CreateBotWithLease(TargetProfile);
        (TestPlayer targetKey, EnemyInfo leasedEnemy) = AddEnemy(
            bot,
            TargetProfile,
            TargetProfile);
        (_, EnemyInfo decisionEnemy) = AddEnemy(
            bot,
            "sain-decision-target",
            "sain-decision-target");
        bot.Memory.GoalEnemy = decisionEnemy;

        Assert(!ReferenceEquals(leasedEnemy, decisionEnemy)
               && ReferenceEquals(leasedEnemy.Person, targetKey),
            "the setup models SAIN selecting a different valid enemy during Arena ownership");
        AssertUsable(bot,
            "a transient GoalEnemy change cannot invalidate an otherwise exact Arena lease target");

        Assert(ArenaPursuitRuntime.TryGetUsableLease(bot, out TeamArenaPursuitLease lease)
               && ArenaPursuitRuntime.TryBindLeaseTarget(bot, lease, out EnemyInfo rebound)
               && ReferenceEquals(rebound, leasedEnemy)
               && ReferenceEquals(bot.Memory.GoalEnemy, leasedEnemy),
            "the Arena action rebinds the exact leased target before every movement update");
    }

    private static void VerifyGoalPersonMustBeTheExactDictionaryKey()
    {
        BotOwner bot = CreateBotWithLease(TargetProfile);
        var dictionaryKey = new TestPlayer(TargetProfile);
        var differentPerson = new TestPlayer(TargetProfile);
        var groupInfo = new BotGroupEnemyInfo(dictionaryKey, bot.BotsGroup);
        var incoherentGoal = new EnemyInfo(bot.BotsGroup, differentPerson, bot, groupInfo);
        bot.BotsGroup.Enemies.Add(dictionaryKey, groupInfo);
        bot.EnemiesController.EnemyInfos.Add(dictionaryKey, incoherentGoal);
        bot.Memory.GoalEnemy = incoherentGoal;

        AssertBlocked(bot,
            "a GoalEnemy whose Person is not its exact dictionary key fails closed");
    }

    private static void VerifyEnemyInfoMustBelongToTheSourceBotAndGroup()
    {
        BotOwner wrongOwner = CreateReadyBot();
        wrongOwner.Memory.GoalEnemy.Owner = new BotOwner
        {
            ProfileId = "different-source",
            GetPlayer = new TestPlayer("different-source"),
        };
        AssertBlocked(wrongOwner,
            "an EnemyInfo owned by another BotOwner cannot keep the lease alive");

        BotOwner wrongGroupOwner = CreateReadyBot();
        wrongGroupOwner.Memory.GoalEnemy.GroupOwner = new BotsGroup();
        AssertBlocked(wrongGroupOwner,
            "an EnemyInfo owned by another BotsGroup cannot keep the lease alive");
    }

    private static void VerifyGroupInfoMustBelongToTheCurrentGroup()
    {
        BotOwner wrongBackReference = CreateReadyBot();
        wrongBackReference.Memory.GoalEnemy.GroupInfo._botGroup = new BotsGroup();
        AssertBlocked(wrongBackReference,
            "a BotGroupEnemyInfo whose _botGroup points elsewhere fails closed");

        BotOwner replacedGroup = CreateReadyBot();
        replacedGroup.BotsGroup = new BotsGroup();
        AssertBlocked(replacedGroup,
            "an EnemyInfo retained from a replaced BotsGroup cannot keep the lease alive");

        BotOwner replacedEnemyGroupInfo = CreateReadyBot();
        EnemyInfo goal = replacedEnemyGroupInfo.Memory.GoalEnemy;
        goal.GroupInfo = new BotGroupEnemyInfo(
            (Player)goal.Person,
            replacedEnemyGroupInfo.BotsGroup);
        AssertBlocked(replacedEnemyGroupInfo,
            "a replacement EnemyInfo.GroupInfo not stored in the current group fails closed");
    }

    private static void VerifyGroupPlayerMustBeCanonicalAndAlive()
    {
        BotOwner nonCanonical = CreateReadyBot();
        nonCanonical.Memory.GoalEnemy.GroupInfo.Player = new TestPlayer(TargetProfile);
        AssertBlocked(nonCanonical,
            "an equal-profile Player that is not the exact dictionary key is noncanonical");

        BotOwner staleBotOwnerPlayer = CreateBotWithLease(TargetProfile);
        (BotOwner targetOwner, TestPlayer oldPlayer, EnemyInfo stalePlayerEnemy) =
            AddBotOwnerEnemy(staleBotOwnerPlayer, TargetProfile, TargetProfile);
        targetOwner.GetPlayer = new TestPlayer(TargetProfile);
        staleBotOwnerPlayer.Memory.GoalEnemy = stalePlayerEnemy;
        Assert(!ReferenceEquals(oldPlayer, targetOwner.GetPlayer)
               && ReferenceEquals(stalePlayerEnemy.GroupInfo.Player, oldPlayer),
            "the BotOwner setup retains an old group Player after its current Player is replaced");
        AssertBlocked(staleBotOwnerPlayer,
            "an old BotOwner Player retained by GroupInfo cannot impersonate the current body");

        BotOwner oldProfile = CreateBotWithLease(TargetProfile);
        (BotOwner oldProfileOwner, TestPlayer oldProfilePlayer, EnemyInfo oldProfileEnemy) =
            AddBotOwnerEnemy(oldProfile, TargetProfile, TargetProfile);
        oldProfilePlayer.ProfileId = "round-1-target";
        oldProfile.Memory.GoalEnemy = oldProfileEnemy;
        Assert(oldProfileOwner.ProfileId == TargetProfile
               && oldProfileEnemy.GroupInfo.Player.ProfileId != TargetProfile,
            "the BotOwner key remains current while GroupInfo.Player carries an old profile");
        AssertBlocked(oldProfile,
            "an old-profile GroupInfo.Player cannot satisfy a current BotOwner-keyed target");

        BotOwner deadGroupPlayer = CreateBotWithLease(TargetProfile);
        (_, TestPlayer deadPlayer, EnemyInfo deadGroupEnemy) = AddBotOwnerEnemy(
            deadGroupPlayer,
            TargetProfile,
            TargetProfile);
        deadPlayer.HealthController.IsAlive = false;
        deadGroupPlayer.Memory.GoalEnemy = deadGroupEnemy;
        AssertBlocked(deadGroupPlayer,
            "a dead canonical GroupInfo.Player is rejected");
    }

    private static void VerifyReplacedGroupEntryFailsClosed()
    {

        BotOwner replacedGroupEntry = CreateReadyBot();
        IPlayer key = replacedGroupEntry.Memory.GoalEnemy.Person;
        replacedGroupEntry.BotsGroup.Enemies[key] = new BotGroupEnemyInfo(
            (Player)key,
            replacedGroupEntry.BotsGroup);
        AssertBlocked(replacedGroupEntry,
            "a replaced group entry cannot be combined with an old EnemyInfo");
    }

    private static void VerifyCurrentDictionaryKeyWinsOverStaleEnemySnapshot()
    {
        BotOwner bot = CreateBotWithLease(TargetProfile);

        // Insert the false candidate first. Its EnemyInfo snapshot names the
        // lease target, but its live dictionary key has moved to another ID.
        (TestPlayer decoyKey, EnemyInfo decoy) = AddEnemy(
            bot,
            capturedProfile: TargetProfile,
            currentKeyProfile: "other-current-profile");

        // This is the actual current-round target. Its live key names the lease
        // target even though EnemyInfo still carries the old-round snapshot.
        (TestPlayer currentKey, EnemyInfo current) = AddEnemy(
            bot,
            capturedProfile: "round-1-target",
            currentKeyProfile: TargetProfile);
        bot.Memory.GoalEnemy = current;

        Assert(decoy.ProfileId == TargetProfile && decoyKey.ProfileId != TargetProfile,
            "the decoy models a stale EnemyInfo profile snapshot");
        Assert(current.ProfileId != TargetProfile && currentKey.ProfileId == TargetProfile,
            "the real entry models a current dictionary key after profile rebind");
        AssertUsable(bot,
            "the current dictionary-key profile resolves even when EnemyInfo.ProfileId is stale");
    }

    private static void VerifyStaleEnemySnapshotCannotImpersonateCurrentKey()
    {
        BotOwner bot = CreateBotWithLease(TargetProfile);
        (_, EnemyInfo stale) = AddEnemy(
            bot,
            capturedProfile: TargetProfile,
            currentKeyProfile: "different-current-profile");
        bot.Memory.GoalEnemy = stale;

        AssertBlocked(bot,
            "a stale EnemyInfo.ProfileId cannot match a lease when its dictionary key has another profile");
    }

    private static void VerifyMissingAndDeadTargetsFailClosed()
    {
        BotOwner missing = CreateBotWithLease(TargetProfile);
        AssertBlocked(missing, "a missing target entry is rejected");

        BotOwner blank = CreateBotWithLease(string.Empty);
        (_, EnemyInfo blankEnemy) = AddEnemy(blank, "old-profile", string.Empty);
        blank.Memory.GoalEnemy = blankEnemy;
        AssertBlocked(blank, "an empty target profile is rejected");

        BotOwner dead = CreateBotWithLease(TargetProfile);
        (TestPlayer deadKey, EnemyInfo deadEnemy) = AddEnemy(dead, TargetProfile, TargetProfile);
        deadKey.HealthController.IsAlive = false;
        dead.Memory.GoalEnemy = deadEnemy;
        AssertBlocked(dead, "a resolved but dead target is rejected");
    }

    private static void VerifySourceBodyMustBeCanonical()
    {
        BotOwner wrongProfile = CreateReadyBot();
        wrongProfile.GetPlayer.ProfileId = "old-source-body";
        AssertBlocked(wrongProfile,
            "a source Player whose profile differs from its BotOwner cannot consume the lease");

        BotOwner wrongOwner = CreateReadyBot();
        wrongOwner.GetPlayer.AIData.BotOwner = new BotOwner
        {
            ProfileId = SourceProfile,
            GetPlayer = new TestPlayer(SourceProfile),
        };
        AssertBlocked(wrongOwner,
            "a source Player whose AIData points at another BotOwner cannot consume the lease");
    }

    private static void VerifyInactiveMedicalSignalsDoNotBlockPursuit()
    {
        BotOwner firstAidPossible = CreateReadyBot();
        firstAidPossible.Medecine.FirstAid.Have2Do = true;
        AssertUsable(firstAidPossible,
            "possible first aid without an active medical action does not block pursuit");

        BotOwner surgeryPossible = CreateReadyBot();
        surgeryPossible.Medecine.SurgicalKit.HaveWork = true;
        AssertUsable(surgeryPossible,
            "possible surgery without an active medical action does not block pursuit");

        BotOwner bothPossible = CreateReadyBot();
        bothPossible.Medecine.FirstAid.Have2Do = true;
        bothPossible.Medecine.SurgicalKit.HaveWork = true;
        AssertUsable(bothPossible,
            "multiple possible treatments still leave an otherwise idle bot available to pursue");

        BotOwner aggregateUsing = CreateReadyBot();
        aggregateUsing.Medecine.Using = true;
        AssertUsable(aggregateUsing,
            "aggregate medicine state alone does not prove a live hands operation");

        BotOwner healDecision = CreateReadyBot();
        healDecision.Brain.Agent.Decision = BotLogicDecision.heal;
        AssertUsable(healDecision,
            "a heal decision alone does not prove a live hands operation");

        BotOwner stimulantDecision = CreateReadyBot();
        stimulantDecision.Brain.Agent.Decision = BotLogicDecision.healStimulators;
        AssertUsable(stimulantDecision,
            "a heal-stimulator decision alone does not prove a live hands operation");

        BotOwner customActionDecision = CreateReadyBot();
        customActionDecision.Brain.Agent.Decision = (BotLogicDecision)CustomActionId;
        AssertUsable(customActionDecision,
            "an unknown custom action ID does not get mistaken for active medical work");
    }

    private static void VerifyActiveMedicalWorkBlocksPursuit()
    {
        VerifyActiveMedicalState(
            bot => bot.Medecine.FirstAid.Using = true,
            "active first aid blocks pursuit");
        VerifyActiveMedicalState(
            bot => bot.Medecine.SurgicalKit.Using = true,
            "active surgery blocks pursuit");
        VerifyActiveMedicalState(
            bot => bot.Medecine.Stimulators.Using = true,
            "active stimulator use blocks pursuit");
    }

    private static void VerifyPursuitResumesWhenActiveHealingEnds()
    {
        BotOwner bot = CreateReadyBot();
        bot.Medecine.FirstAid.Have2Do = true;
        bot.Medecine.SurgicalKit.HaveWork = true;
        bot.Medecine.Using = true;
        bot.Medecine.FirstAid.Using = true;
        bot.Brain.Agent.Decision = BotLogicDecision.heal;
        AssertBlocked(bot, "an active heal owns the bot while treatment is in progress");

        bot.Medecine.FirstAid.Using = false;
        AssertUsable(bot,
            "pursuit resumes when child Using clears despite possible work, aggregate Using and a stale heal decision");
    }

    private static void VerifyActiveMedicalState(Action<BotOwner> activate, string message)
    {
        BotOwner bot = CreateReadyBot();
        activate(bot);
        AssertBlocked(bot, message);
    }

    private static BotOwner CreateReadyBot()
    {
        BotOwner bot = CreateBotWithLease(TargetProfile);
        (_, EnemyInfo enemy) = AddEnemy(bot, TargetProfile, TargetProfile);
        bot.Memory.GoalEnemy = enemy;
        return bot;
    }

    private static BotOwner CreateBotWithLease(string targetProfile)
    {
        ArenaSession.IsActive = true;
        TeamArenaPursuitLeaseBridge.Reset();

        var bot = new BotOwner
        {
            ProfileId = SourceProfile,
            GetPlayer = new TestPlayer(SourceProfile),
        };
        bot.GetPlayer.AIData.BotOwner = bot;
        TeamArenaPursuitLeaseBridge.Set(new TeamArenaPursuitLease(
            SourceProfile,
            targetProfile,
            epoch: 7,
            generation: 11));
        return bot;
    }

    private static (TestPlayer Key, EnemyInfo Enemy) AddEnemy(
        BotOwner bot,
        string capturedProfile,
        string currentKeyProfile)
    {
        var key = new TestPlayer(capturedProfile);
        var targetOwner = new BotOwner
        {
            ProfileId = capturedProfile,
            GetPlayer = key,
        };
        key.AIData.BotOwner = targetOwner;
        var groupInfo = new BotGroupEnemyInfo(key, bot.BotsGroup);
        var enemy = new EnemyInfo(bot.BotsGroup, key, bot, groupInfo);
        key.ProfileId = currentKeyProfile;
        targetOwner.ProfileId = currentKeyProfile;
        bot.BotsGroup.Enemies.Add(key, groupInfo);
        bot.EnemiesController.EnemyInfos.Add(key, enemy);
        return (key, enemy);
    }

    private static (BotOwner Key, TestPlayer Player, EnemyInfo Enemy) AddBotOwnerEnemy(
        BotOwner source,
        string capturedProfile,
        string currentProfile)
    {
        var player = new TestPlayer(capturedProfile);
        var key = new BotOwner
        {
            ProfileId = capturedProfile,
            GetPlayer = player,
        };
        player.AIData.BotOwner = key;
        var groupInfo = new BotGroupEnemyInfo(player, source.BotsGroup);
        var enemy = new EnemyInfo(source.BotsGroup, key, source, groupInfo);
        key.ProfileId = currentProfile;
        player.ProfileId = currentProfile;
        source.BotsGroup.Enemies.Add(key, groupInfo);
        source.EnemiesController.EnemyInfos.Add(key, enemy);
        return (key, player, enemy);
    }

    private static void AssertUsable(BotOwner bot, string message)
    {
        bool usable = ArenaPursuitRuntime.TryGetUsableLease(bot, out TeamArenaPursuitLease lease);
        Assert(usable
               && lease.SourceProfileId == SourceProfile
               && lease.TargetProfileId == TargetProfile
               && lease.Epoch == 7
               && lease.Generation == 11,
            message);
    }

    private static void AssertBlocked(BotOwner bot, string message)
    {
        bool usable = ArenaPursuitRuntime.TryGetUsableLease(bot, out TeamArenaPursuitLease lease);
        Assert(!usable
               && lease.SourceProfileId == null
               && lease.TargetProfileId == null
               && lease.Epoch == 0
               && lease.Generation == 0,
            message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        _assertions++;
    }
}
