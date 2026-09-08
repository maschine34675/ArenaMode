// Public shapes verified against installed SAIN 4.5.1. These are independent
// test fixtures, not a port of its implementation: admission outcomes and contact
// states are explicit knobs. The age predicate below is not SAIN search execution.
using EFT;
using UnityEngine;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.Models.Structs;

namespace SAIN.Models.Structs
{
    public struct SAINHearingReport
    {
        public Vector3 position;
        public SAIN.Preset.Shared.Enums.SAINSoundType soundType;
        public EEnemyPlaceType placeType;
        public bool isDanger, shallReportToSquad;
    }
}
namespace SAIN.Preset.Shared.Enums
{ public enum SAINSoundType { None, Generic, FootStep, GunShot } }
namespace SAIN.SAINComponent.Classes.EnemyClasses
{
    public enum EEnemyPlaceType { Vision, Hearing, Flashlight, Injury }
    public class EnemyPlace { }
    public class Enemy
    {
        public Player EnemyPlayer { get; set; }
        public EnemyInfo EnemyInfo { get; set; }
        public bool Seen { get; set; }
        public bool IsVisible { get; set; }
        public bool EnemyKnown { get; set; } = true;
        public bool CanShoot;
        public bool Heard => HearingState.Heard;
        public float TimeSinceHeard => Heard ? Time.time - HearingState.At : float.MaxValue;
        public int PositionUpdates;
        public Vector3 KnownPosition;
        public Components.PlayerTransform EnemyTransform { get; set; } = new();
        public Components.KnownPlaces KnownPlaces { get; set; } = new();
        public Components.EnemyPath Path { get; set; } = new();
        public Action OnPositionUpdate, OnGetHearing;
        public Components.BotComponentSpace.Classes.EnemyClasses.EnemyHearing Hearing
        { get { OnGetHearing?.Invoke(); return HearingState; } }
        public readonly Components.BotComponentSpace.Classes.EnemyClasses.EnemyHearing HearingState = new();
        public void UpdateLastSeenPosition(Vector3 position, float currentTime)
        { PositionUpdates++; KnownPosition = position; OnPositionUpdate?.Invoke(); }
    }
    public class EnemyListController
    {
        public BotOwner BotOwner { get; set; }
        private Enemy tryAddEnemy(IPlayer player) => null;
    }
    public class EnemyController
    {
        public BotOwner Owner;
        public Enemy GoalEnemy { get; set; }
        public Dictionary<string, Enemy> Enemies = new();
        public Func<IPlayer, Enemy> OverrideAdd;
        public Action OnAdd, OnRemove;
        public Enemy CheckAddEnemy(IPlayer player)
        {
            OnAdd?.Invoke();
            if (OverrideAdd != null) return OverrideAdd(player);
            if (!Enemies.TryGetValue(player.ProfileId, out var enemy))
            {
                enemy = new Enemy { EnemyPlayer = (Player)player, EnemyInfo = Owner.EnemiesController.EnemyInfos.GetValueOrDefault(player) };
                Enemies[player.ProfileId] = enemy;
            }
            return enemy;
        }
        public void RemoveEnemy(string profileId)
        {
            if (Enemies.Remove(profileId, out var enemy)) Owner.Memory.DeleteInfoAboutEnemy(enemy.EnemyPlayer);
            OnRemove?.Invoke();
        }
    }
}
namespace SAIN.Components.BotComponentSpace.Classes.EnemyClasses
{
    public class EnemyHearing
    {
        public bool Heard, Ignore, ReturnNull, SuppressReadback, Throw;
        public float At;
        public int Calls;
        public SAINHearingReport LastReport;
        public Action OnSet;
        public EnemyPlace SetHeard(SAINHearingReport report, float currentTime)
        {
            Calls++; LastReport = report;
            if (Throw) throw new InvalidOperationException("modeled hearing failure");
            if (Ignore) return null;
            Heard = !SuppressReadback; At = Time.time;
            OnSet?.Invoke();
            return ReturnNull ? null : new EnemyPlace();
        }
    }
}
namespace SAIN.Components
{
    public class BotComponent
    {
        public BotOwner BotOwner { get; set; }
        public Player Player { get; set; }
        public EnemyController EnemyController { get; set; }
        public Action OnGetGoal;
        public Action OnGetTransform;
        public PlayerTransform TransformState = new();
        public PlayerTransform Transform { get { OnGetTransform?.Invoke(); return TransformState; } }
        // The fixture explicitly supplies the active goal; it does not simulate
        // SAIN's target selection or liveness filter.
        public Enemy GoalEnemy { get { OnGetGoal?.Invoke(); return EnemyController.GoalEnemy; } }
        public Info Info { get; } = new();
        public Decision Decision { get; } = new();
    }
    public class Info { public float TimeBeforeSearch { get; set; } = 60; }
    // Value fixtures only. No NavMesh sampling or path computation is available.
    public class PlayerTransform { public NavData NavData { get; set; } = new(); }
    public class NavData
    {
        public Vector3 Position { get; set; }
        public NavHit LastValidHit { get; set; }
        public string Status { get; set; }
        public bool IsOnNavMesh { get; set; }
        public float TimeLastOnNavMesh { get; set; }
    }
    public struct NavHit { public Vector3 position; public bool hit; }
    public class KnownPlaces { public Vector3? LastKnownPosition { get; set; } }
    public class EnemyPath
    {
        public string PathToEnemyStatus { get; set; }
        public int PathCornerCount { get; set; }
        public float PathLength { get; set; }
    }
    public class Decision
    {
        public string CurrentCombatDecision { get; set; } = "None";
        public EnemyDecision EnemyDecisions { get; } = new();
    }
    public class EnemyDecision
    {
        public bool? DebugShallSearch { get; set; }
        public SearchReasons DebugSearchReasons { get; set; } = new() { NotSearchReason = "StillDelaying" };
    }
    public struct SearchReasons { public string NotSearchReason, PathCalcFailReason; public WantReasons WantSearchReasons; }
    public struct WantReasons { public string WantToSearchReason, NotWantToSearchReason, CantStartReason; }
}
