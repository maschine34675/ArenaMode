using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.Ballistics;

// Executable stand-in for the reviewed library's body lifecycle. Real
// published metadata is checked independently with Cecil.
namespace EFTEffectManager
{
    public sealed class EffectRegistration { }
    public sealed class SpeedPenaltyRegistry
    {
        private MovementContext _localContext;
        public readonly List<string> Providers = new() { "BrokenLimbs", "OtherProvider" };
        private void Reset()
        {
            if (_localContext != null) _localContext.Limited = false;
            _localContext = null;
        }
        public void Apply(Player player)
        {
            _localContext = player.MovementContext;
            _localContext.Limited = player.HealthController.Effects.Count > 0;
        }
        internal void WrongContext(MovementContext context) => _localContext = context;
    }
    public static class EffectsManager
    {
        private static bool _patched;
        private static GameWorld _raidWorld;
        private static float? _lastErgonomicsPenalty;
        public static Player RaidPlayer { get; private set; }
        public static readonly Dictionary<Type, EffectRegistration> Registrations = new();
        public static SpeedPenaltyRegistry MovementSpeedPenalties { get; } = new();
        private static readonly Dictionary<IHealthController, Dictionary<ESkillId, int>> SkillBonusPool = new();
        private sealed class EffectSkillBonus { internal ActiveHealthController Controller; }
        private static readonly Dictionary<IHealthEffect, EffectSkillBonus> EffectSkillBonuses = new();
        public static int Observations, StrengthCalls, SkillCalls, OriginalEnds, OriginalStarts;
        public static event Action<Player, IHealthEffect> LocalEffectStarted;
        public static void Configure(bool ready = true)
        {
            _patched = ready;
            var start = typeof(Patches.RaidLifecyclePatch.RaidStarted).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
            var end = typeof(Patches.RaidLifecyclePatch.WorldDisposing).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            HarmonyLib.Harmony.Bound[typeof(GameWorld).GetMethod("OnGameStarted")] = new()
            { Postfixes = new() { new() { owner = "com.eft.effectmanager.core", PatchMethod = start } } };
            HarmonyLib.Harmony.Bound[typeof(GameWorld).GetMethod("Dispose")] = new()
            { Prefixes = new() { new() { owner = "com.eft.effectmanager.core", PatchMethod = end } } };
        }
        internal static void NotifyRaidStarted(GameWorld world)
        {
            OriginalStarts++;
            Program.Call(typeof(EffectsManager).GetMethod(nameof(NotifyRaidEnded), BindingFlags.Static | BindingFlags.NonPublic), null, new object[] { null });
            RaidPlayer = world.MainPlayer; _raidWorld = world;
            Patches.PlayerLifecyclePatch.Attach(RaidPlayer); Patches.BulletWhizzPatch.Attach();
            RefreshPenalties(RaidPlayer);
        }
        internal static void NotifyRaidEnded(GameWorld world)
        {
            if (world != null && !ReferenceEquals(world, _raidWorld)) return;
            OriginalEnds++;
            Patches.PlayerLifecyclePatch.Detach(); Patches.BulletWhizzPatch.Detach();
            foreach (var entry in SkillBonusPool)
                foreach (var bonus in entry.Value) FindSkill(entry.Key, bonus.Key).Buff -= bonus.Value;
            SkillBonusPool.Clear(); EffectSkillBonuses.Clear();
            typeof(SpeedPenaltyRegistry).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(MovementSpeedPenalties, null);
            RaidPlayer = null; _raidWorld = null; _lastErgonomicsPenalty = null;
        }
        public static void RefreshPenalties(Player player)
        {
            if (ReferenceEquals(player, RaidPlayer)) MovementSpeedPenalties.Apply(player);
        }
        internal static void NotifyEffectStrengthChanged(IHealthEffect effect) => StrengthCalls++;
        internal static void ApplyEffectSkillBonuses(IHealthEffect effect) => SkillCalls++;
        private static Skill FindSkill(IHealthController controller, ESkillId skill) => (controller as ActiveHealthController)?.Skills[skill];
        public static void Bonus(ActiveHealthController controller, int value, IHealthEffect effect)
        {
            controller.Skills[ESkillId.Strength].Buff += value;
            SkillBonusPool[controller] = new() { [ESkillId.Strength] = value };
            EffectSkillBonuses[effect] = new() { Controller = controller };
        }
        public static void MoreBonus(ActiveHealthController controller, ESkillId skill, int value)
        {
            controller.Skills[skill].Buff += value;
            SkillBonusPool[controller][skill] = value;
        }
        public static bool HasBonus(IHealthController controller) => SkillBonusPool.ContainsKey(controller);
        public static bool HasEffect(IHealthEffect effect) => EffectSkillBonuses.ContainsKey(effect);
        public static void Expire(ActiveHealthController controller, IHealthEffect effect)
        {
            foreach (var entry in SkillBonusPool[controller]) controller.Skills[entry.Key].Buff -= entry.Value;
            SkillBonusPool.Remove(controller); EffectSkillBonuses.Remove(effect);
        }
        public static void Observe(Player player, IHealthEffect effect)
        { Observations++; LocalEffectStarted?.Invoke(player, effect); }
    }
}
namespace EFTEffectManager.Patches
{
    internal static class RaidLifecyclePatch
    {
        internal static class RaidStarted { private static void Postfix(GameWorld world) { } }
        internal static class WorldDisposing { private static void Prefix(GameWorld world) { } }
    }
    internal static class BulletWhizzPatch
    {
        internal static bool Attached;
        internal static void Attach() => Attached = true;
        internal static void Detach() => Attached = false;
    }
    internal static class PlayerLifecyclePatch
    {
        private static Player _player;
        private static ActiveHealthController _controller;
        internal static void Attach(Player player)
        {
            Detach(); _player = player; _controller = player.HealthController;
            _controller.EffectAddedEvent += Added;
            _controller.EffectStartedEvent += Started;
            _controller.EffectUpdatedEvent += Updated;
            _controller.EffectResidualEvent += Residued;
            _controller.EffectRemovedEvent += Removed;
            _controller.BodyPartDestroyedEvent += Destroyed;
            _player.OnDamageReceived += Damaged;
        }
        internal static void Detach()
        {
            if (_player != null) _player.OnDamageReceived -= Damaged;
            if (_controller != null)
            {
                _controller.EffectAddedEvent -= Added;
                _controller.EffectStartedEvent -= Started;
                _controller.EffectUpdatedEvent -= Updated;
                _controller.EffectResidualEvent -= Residued;
                _controller.EffectRemovedEvent -= Removed;
                _controller.BodyPartDestroyedEvent -= Destroyed;
            }
            _player = null; _controller = null;
        }
        private static void Added(IHealthEffect effect) => EffectsManager.Observe(_player, effect);
        private static void Started(IHealthEffect effect) => EffectsManager.Observe(_player, effect);
        private static void Updated(IHealthEffect effect) => EffectsManager.Observe(_player, effect);
        private static void Residued(IHealthEffect effect) => EffectsManager.Observe(_player, effect);
        private static void Removed(IHealthEffect effect) => EffectsManager.Observe(_player, effect);
        private static void Destroyed(EBodyPart part, EDamageType damage) => EffectsManager.Observe(_player, null);
        private static void Damaged(float damage, EBodyPart part, EDamageType type, float absorbed, MaterialType material)
            => EffectsManager.Observe(_player, null);
    }
}
