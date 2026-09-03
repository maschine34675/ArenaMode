// Only the EFT state read by ArenaPursuitRuntime and the cross-assembly lease
// transport are stand-ins. The runtime decision code itself is linked from the
// production companion project.
namespace ArenaMode.Bots
{
    internal readonly struct TeamArenaPursuitLease
    {
        public TeamArenaPursuitLease(
            string sourceProfileId,
            string targetProfileId,
            int epoch,
            long generation)
        {
            SourceProfileId = sourceProfileId;
            TargetProfileId = targetProfileId;
            Epoch = epoch;
            Generation = generation;
        }

        public string SourceProfileId { get; }
        public string TargetProfileId { get; }
        public int Epoch { get; }
        public long Generation { get; }
    }

    internal static class TeamArenaPursuitLeaseBridge
    {
        private static readonly Dictionary<string, TeamArenaPursuitLease> Leases =
            new(StringComparer.Ordinal);

        public static bool TryGet(string sourceProfileId, out TeamArenaPursuitLease lease)
            => Leases.TryGetValue(sourceProfileId ?? string.Empty, out lease);

        public static bool HasExactTeamArenaContext() => true;

        public static void Set(TeamArenaPursuitLease lease)
            => Leases[lease.SourceProfileId] = lease;

        public static void Reset() => Leases.Clear();
    }
}

namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        public static bool IsActive { get; set; }
    }
}

namespace EFT
{
    internal sealed class BotGroupEnemyInfo
    {
        public BotGroupEnemyInfo(Player player, BotsGroup botGroup)
        {
            Player = player;
            _botGroup = botGroup;
        }

        public Player Player { get; set; }
        public BotsGroup _botGroup;
    }

    internal sealed class BotsGroup
    {
        public Dictionary<IPlayer, BotGroupEnemyInfo> Enemies { get; } = new();
    }

    internal enum EBotState
    {
        Active,
        Disposed,
    }

    internal enum BotLogicDecision
    {
        holdPosition,
        heal,
        healStimulators,
    }

    internal interface IPlayer
    {
        string ProfileId { get; }
        HealthController HealthController { get; }
    }

    internal class Player : IPlayer
    {
        public Player(string profileId)
        {
            ProfileId = profileId;
        }

        public string ProfileId { get; set; }
        public HealthController HealthController { get; } = new();
    }

    internal sealed class TestPlayer : Player
    {
        public TestPlayer(string profileId)
            : base(profileId)
        {
        }
    }

    internal sealed class HealthController
    {
        public bool IsAlive { get; set; } = true;
    }

    internal sealed class EnemyInfo
    {
        public EnemyInfo(
            BotsGroup groupOwner,
            IPlayer person,
            BotOwner owner,
            BotGroupEnemyInfo groupInfo)
        {
            Person = person;
            Owner = owner;
            GroupOwner = groupOwner;
            GroupInfo = groupInfo;
            // EFT captures this once in EnemyInfo's constructor. The dictionary
            // key's ProfileId can later reflect a rebound Profile object.
            ProfileId = person.ProfileId;
        }

        public string ProfileId { get; }
        public IPlayer Person { get; set; }
        public BotOwner Owner { get; set; }
        public BotsGroup GroupOwner { get; set; }
        public BotGroupEnemyInfo GroupInfo { get; set; }
        public bool IsVisible { get; set; }
        public bool CanShoot { get; set; }
    }

    internal sealed class BotEnemiesController
    {
        public Dictionary<IPlayer, EnemyInfo> EnemyInfos { get; } = new();
    }

    internal sealed class BotMemory
    {
        public EnemyInfo GoalEnemy { get; set; }
    }

    internal sealed class BotMedecine
    {
        public bool Using { get; set; }
        public BotFirstAid FirstAid { get; } = new();
        public BotSurgicalKit SurgicalKit { get; } = new();
        public BotStimulators Stimulators { get; } = new();
    }

    internal sealed class BotFirstAid
    {
        public bool Have2Do { get; set; }
        public bool Using { get; set; }
    }

    internal sealed class BotSurgicalKit
    {
        public bool HaveWork { get; set; }
        public bool Using { get; set; }
    }

    internal sealed class BotStimulators
    {
        public bool Using { get; set; }
    }

    internal sealed class BotBrain
    {
        public BotAgent Agent { get; } = new();
    }

    internal sealed class BotAgent
    {
        public BotLogicDecision Decision { get; set; } = BotLogicDecision.holdPosition;
        public BotActionResult LastResult() => new() { Action = Decision };
    }

    internal sealed class BotActionResult
    {
        public BotLogicDecision Action { get; init; }
    }

    internal sealed class BotWeaponManager
    {
        public BotReload Reload { get; } = new();
        public BotGrenades Grenades { get; } = new();
    }

    internal sealed class BotReload
    {
        public bool Reloading { get; set; }
    }

    internal sealed class BotGrenades
    {
        public bool ThrowindNow { get; set; }
    }

    internal sealed class BotDoorOpener
    {
        public bool Interacting { get; set; }
    }

    internal sealed class BotDanger
    {
        public bool Active { get; set; }
        public bool ShallRunAway() => Active;
    }

    internal sealed class BotFlashGrenade
    {
        public bool IsFlashed { get; set; }
    }

    internal sealed class BotOwner : IPlayer
    {
        public string ProfileId { get; set; }
        public bool IsDead { get; set; }
        public EBotState BotState { get; set; } = EBotState.Active;
        public Player GetPlayer { get; set; }
        public HealthController HealthController => GetPlayer?.HealthController;
        public object Mover { get; set; } = new();
        public BotDanger BewareGrenade { get; } = new();
        public BotDanger BewareBTR { get; } = new();
        public BotDanger ArtilleryDangerPlace { get; } = new();
        public BotFlashGrenade FlashGrenade { get; } = new();
        public BotMedecine Medecine { get; } = new();
        public BotBrain Brain { get; } = new();
        public BotWeaponManager WeaponManager { get; } = new();
        public BotDoorOpener DoorOpener { get; } = new();
        public BotEnemiesController EnemiesController { get; } = new();
        public BotsGroup BotsGroup { get; set; } = new();
        public BotMemory Memory { get; } = new();
    }
}
