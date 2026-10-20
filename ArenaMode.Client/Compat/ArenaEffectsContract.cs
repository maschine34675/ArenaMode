using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>The manager is an on-demand library, not a BepInEx plugin.</summary>
internal sealed class ArenaEffectsContract
{
    internal const string AssemblyName = "EFTEffectManager";
    internal const string BrokenLimbsGuid = "com.harmonyzt.BrokenLimbsDontHeal";
    internal const string ReviewedMvid = "09338347-931e-4943-8d90-4fa7b05af4c0";
    internal Assembly Assembly;
    internal MethodInfo Started, Ended, Attach, Detach, WhizzAttach, WhizzDetach, Refresh,
        Strength, ApplySkills, FindSkill;
    internal PropertyInfo RaidPlayer, Speed;
    internal FieldInfo RaidWorld, LastErgonomics, BoundPlayer, BoundController, SkillPool, EffectBonuses,
        BonusController, SpeedContext, Patched;
    internal MethodInfo SpeedReset;
    internal FieldInfo Registrations;
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => o.Value);

    internal static ArenaEffectsContract Resolve(Assembly assembly)
    {
        if (assembly?.GetName().Name != AssemblyName || Normalize(assembly.GetName().Version) < new Version(1, 0, 0, 0))
            throw new NotSupportedException("EFT Effects Manager library identity/version is unsupported");
        var c = new ArenaEffectsContract { Assembly = assembly };
        var manager = assembly.GetType("EFTEffectManager.EffectsManager", true);
        c.Started = Method(manager, "NotifyRaidStarted", true, typeof(void), typeof(GameWorld));
        c.Ended = Method(manager, "NotifyRaidEnded", true, typeof(void), typeof(GameWorld));
        c.Patched = Field(manager, "_patched", true, typeof(bool));
        c.RaidWorld = Field(manager, "_raidWorld", true, typeof(GameWorld));
        c.LastErgonomics = Field(manager, "_lastErgonomicsPenalty", true, typeof(float?));
        c.RaidPlayer = Property(manager, "RaidPlayer", typeof(Player), true);
        c.Refresh = Method(manager, "RefreshPenalties", true, typeof(void), typeof(Player));
        c.Strength = Method(manager, "NotifyEffectStrengthChanged", true, typeof(void), typeof(IHealthEffect));
        c.ApplySkills = Method(manager, "ApplyEffectSkillBonuses", true, typeof(void), typeof(IHealthEffect));
        c.FindSkill = Method(manager, "FindSkill", true, typeof(Skill), typeof(IHealthController), typeof(ESkillId));
        c.SkillPool = Field(manager, "SkillBonusPool", true, typeof(Dictionary<IHealthController, Dictionary<ESkillId, int>>));
        var bonus = manager.GetNestedType("EffectSkillBonus", BindingFlags.NonPublic);
        if (bonus == null) throw new MissingMemberException("Effects skill ledger type changed");
        c.BonusController = Field(bonus, "Controller", false, typeof(ActiveHealthController));
        c.EffectBonuses = Field(manager, "EffectSkillBonuses", true,
            typeof(Dictionary<,>).MakeGenericType(typeof(IHealthEffect), bonus));
        var registration = assembly.GetType("EFTEffectManager.EffectRegistration", true);
        c.Registrations = Field(manager, "Registrations", true, typeof(Dictionary<,>).MakeGenericType(typeof(Type), registration));
        var lifecycle = assembly.GetType("EFTEffectManager.Patches.PlayerLifecyclePatch", true);
        c.Attach = Method(lifecycle, "Attach", true, typeof(void), typeof(Player));
        c.Detach = Method(lifecycle, "Detach", true, typeof(void));
        c.BoundPlayer = Field(lifecycle, "_player", true, typeof(Player));
        c.BoundController = Field(lifecycle, "_controller", true, typeof(ActiveHealthController));
        var whizz = assembly.GetType("EFTEffectManager.Patches.BulletWhizzPatch", true);
        c.WhizzAttach = Method(whizz, "Attach", true, typeof(void));
        c.WhizzDetach = Method(whizz, "Detach", true, typeof(void));
        var speed = assembly.GetType("EFTEffectManager.SpeedPenaltyRegistry", true);
        c.Speed = Property(manager, "MovementSpeedPenalties", speed, false);
        c.SpeedReset = Method(speed, "Reset", false, typeof(void));
        c.SpeedContext = Field(speed, "_localContext", false, typeof(MovementContext));
        // These event names/signatures and the private field shapes are the
        // ownership contract; an unknown replacement implementation is rejected.
        foreach (var name in new[] { "Added", "Started", "Updated", "Residued", "Removed" })
            Method(lifecycle, name, true, typeof(void), typeof(IHealthEffect));
        Method(lifecycle, "Destroyed", true, typeof(void), typeof(EBodyPart), typeof(EDamageType));
        Method(lifecycle, "Damaged", true, typeof(void), typeof(float), typeof(EBodyPart),
            typeof(EDamageType), typeof(float), typeof(EFT.Ballistics.MaterialType));
        c.VerifyCallbackBridge();
        c.VerifyNativeBindings();
        return c;
    }

    private void VerifyCallbackBridge()
    {
        var expected = new[] { "EffectAddedEvent", "EffectStartedEvent", "EffectUpdatedEvent", "EffectResidualEvent",
            "EffectRemovedEvent", "BodyPartDestroyedEvent", "OnDamageReceived" }.OrderBy(x => x).ToArray();
        foreach (var method in new[] { Attach, Detach })
        {
            var members = Members(method).ToArray();
            string prefix = method == Attach ? "add_" : "remove_";
            var actual = members.Where(m => (m.Op == OpCodes.Call || m.Op == OpCodes.Callvirt)
                && m.Member is MethodInfo mi && mi.Name.StartsWith(prefix, StringComparison.Ordinal))
                .Select(m => m.Member.Name.Substring(prefix.Length)).OrderBy(x => x).ToArray();
            if (!actual.SequenceEqual(expected))
                throw new InvalidOperationException("Effects callback bridge no longer attaches/detaches exactly seven native callbacks");
            if (members.Any(m => (m.Op == OpCodes.Stfld || m.Op == OpCodes.Stsfld)
                && m.Member != BoundPlayer && m.Member != BoundController && !IsCallbackCache(m.Member)))
                throw new InvalidOperationException("Effects callback bridge writes unknown state");
            foreach (var member in members.Where(m => m.Op == OpCodes.Call || m.Op == OpCodes.Callvirt))
                if (member.Member != Detach && !member.Member.Name.StartsWith(prefix, StringComparison.Ordinal)
                    && member.Member.Name != "get_HealthController"
                    && !(member.Member.DeclaringType?.FullName == "UnityEngine.Object" && member.Member.Name == "op_Inequality"))
                    throw new InvalidOperationException("Effects callback bridge calls an unknown helper");
        }
    }
    private bool IsCallbackCache(MemberInfo member)
    {
        if (member is not FieldInfo field || !field.IsStatic
            || field.DeclaringType?.DeclaringType != Attach.DeclaringType
            || !field.DeclaringType.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
            return false;
        var handler = new[] { "Added", "Started", "Updated", "Residued", "Removed", "Destroyed", "Damaged" }
            .FirstOrDefault(name => field.Name.EndsWith("__" + name, StringComparison.Ordinal));
        if (handler == null) return false;
        Type expected = handler == "Destroyed" ? typeof(Action<EBodyPart, EDamageType>)
            : handler == "Damaged" ? typeof(Player).GetEvent("OnDamageReceived")?.EventHandlerType
            : typeof(Action<IHealthEffect>);
        return field.FieldType == expected;
    }

    private static IEnumerable<(OpCode Op, MemberInfo Member)> Members(MethodBase method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException("Effects helper body is unavailable");
        for (int at = 0; at < il.Length;)
        {
            short code = il[at++];
            if (code == 0xfe)
            {
                if (at == il.Length) throw new InvalidOperationException("Truncated Effects IL");
                code = (short)(0xfe00 | il[at++]);
            }
            if (!Codes.TryGetValue(code, out var op)) throw new InvalidOperationException("Unknown Effects IL");
            int size;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: size = 0; break;
                case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                case OperandType.InlineVar: size = 2; break;
                case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                case OperandType.InlineSwitch:
                    if (at + 4 > il.Length) throw new InvalidOperationException("Truncated Effects switch");
                    int count = BitConverter.ToInt32(il, at);
                    if (count < 0 || count > (il.Length - at - 4) / 4) throw new InvalidOperationException("Invalid Effects switch");
                    size = 4 + count * 4; break;
                default: size = 4; break;
            }
            if (at + size > il.Length) throw new InvalidOperationException("Truncated Effects operand");
            if (op.OperandType == OperandType.InlineField || op.OperandType == OperandType.InlineMethod)
                yield return (op, method.Module.ResolveMember(BitConverter.ToInt32(il, at),
                    method.DeclaringType?.GetGenericArguments(), (method as MethodInfo)?.GetGenericArguments()));
            at += size;
        }
    }

    internal void VerifyNativeBindings()
    {
        if (!(Patched.GetValue(null) is true))
            throw new InvalidOperationException("EFT Effects Manager has not completed its shared initialization");
        var start = Assembly.GetType("EFTEffectManager.Patches.RaidLifecyclePatch+RaidStarted", true);
        var end = Assembly.GetType("EFTEffectManager.Patches.RaidLifecyclePatch+WorldDisposing", true);
        Receipt(typeof(GameWorld).GetMethod("OnGameStarted"), Method(start, "Postfix", true, typeof(void), typeof(GameWorld)), false);
        Receipt(typeof(GameWorld).GetMethod("Dispose"), Method(end, "Prefix", true, typeof(void), typeof(GameWorld)), true);
    }
    private static void Receipt(MethodInfo target, MethodInfo patch, bool prefix)
    {
        var info = target == null ? null : Harmony.GetPatchInfo(target);
        if ((prefix ? info?.Prefixes : info?.Postfixes)?.Count(p => p.PatchMethod == patch) != 1)
            throw new MissingMethodException("Effects Manager native lifecycle hook changed: " + target?.Name);
    }
    private static MethodInfo Method(Type type, string name, bool stat, Type result, params Type[] args)
    {
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.IsStatic != stat || method.ReturnType != result || method.ContainsGenericParameters
            || method.GetMethodBody() == null) throw new MissingMethodException(type.FullName, name);
        return method;
    }
    private static FieldInfo Field(Type type, string name, bool stat, Type value)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (field == null || field.IsStatic != stat || field.FieldType != value)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }
    private static PropertyInfo Property(Type type, string name, Type value, bool setter)
    {
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (property?.PropertyType != value || property.GetGetMethod()?.IsStatic != true
            || setter && property.GetSetMethod(true)?.IsStatic != true)
            throw new MissingMemberException(type.FullName, name);
        return property;
    }
    internal static Version Normalize(Version version) => version == null ? new Version(0, 0, 0, 0)
        : new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));

    internal Player Player => RaidPlayer.GetValue(null) as Player;
    internal GameWorld World => RaidWorld.GetValue(null) as GameWorld;
    internal bool IsBound(Player player) => ReferenceEquals(Player, player)
        && ReferenceEquals(BoundPlayer.GetValue(null), player)
        && ReferenceEquals(BoundController.GetValue(null), player?.HealthController);

    internal void RemoveBodyState(Player player)
    {
        var controller = player.HealthController;
        if (!IsBound(player)) throw new InvalidOperationException("Effects binding changed before cleanup");
        var speed = Speed.GetValue(null);
        var context = SpeedContext.GetValue(speed);
        if (context != null && !ReferenceEquals(context, player.MovementContext))
            throw new InvalidOperationException("Effects speed limit belongs to another body");
        var pool = (IDictionary)SkillPool.GetValue(null);
        var effects = (IDictionary)EffectBonuses.GetValue(null);
        if (pool == null || effects == null)
            throw new InvalidOperationException("Effects temporary ledgers are unavailable");
        // Clear only this controller's ledger. ClearAllSkillBonuses would also
        // remove contributions explicitly registered for unrelated controllers.
        if (pool[controller] is IDictionary bonuses)
        {
            foreach (var key in bonuses.Keys.Cast<object>().ToArray())
            {
                var skill = FindSkill.Invoke(null, new[] { controller, key }) as Skill
                    ?? throw new InvalidOperationException("Effects temporary skill target disappeared");
                int expected = checked(skill.Buff - (int)bonuses[key]);
                try { skill.Buff = expected; }
                catch
                {
                    // Native Skill.Buff writes first, then invokes listeners.
                    // A completed write must not be subtracted again on retry.
                    if (skill.Buff == expected) bonuses.Remove(key);
                    throw;
                }
                if (skill.Buff != expected)
                    throw new InvalidOperationException("Effects temporary skill cleanup changed unexpectedly");
                bonuses.Remove(key);
            }
            pool.Remove(controller);
        }
        foreach (var key in effects.Keys.Cast<object>().ToArray())
            if (ReferenceEquals(BonusController.GetValue(effects[key]), controller)) effects.Remove(key);
        // Keep the old binding intact while any skill write needs retry.
        Detach.Invoke(null, null);
        if (BoundPlayer.GetValue(null) != null || BoundController.GetValue(null) != null)
            throw new InvalidOperationException("Effects callbacks did not detach");
        if (context != null) SpeedReset.Invoke(speed, null);
        RaidPlayer.SetValue(null, null);
        RaidWorld.SetValue(null, null);
        LastErgonomics.SetValue(null, null);
    }

    internal int SkillBonusFor(Player player, Skill skill)
    {
        if (!IsBound(player)) throw new InvalidOperationException("Effects binding changed during skill ownership lookup");
        var pool = (IDictionary)SkillPool.GetValue(null);
        if (pool[player.HealthController] is not IDictionary bonuses) return 0;
        foreach (DictionaryEntry entry in bonuses)
            if (ReferenceEquals(FindSkill.Invoke(null, new[] { player.HealthController, entry.Key }), skill))
                return (int)entry.Value;
        return 0;
    }

    internal void BindBody(Player player, GameWorld world)
    {
        if (!ReferenceEquals(Player, null) || !ReferenceEquals(World, null)
            || BoundPlayer.GetValue(null) != null || BoundController.GetValue(null) != null)
            throw new InvalidOperationException("Effects binding was not released");
        RaidPlayer.SetValue(null, player);
        RaidWorld.SetValue(null, world);
        Attach.Invoke(null, new object[] { player });
        WhizzAttach.Invoke(null, null); // idempotent; registrations and observers remain intact
        foreach (var effect in player.HealthController.GetAllEffects().ToArray())
        {
            Strength.Invoke(null, new object[] { effect });
            ApplySkills.Invoke(null, new object[] { effect });
        }
        Refresh.Invoke(null, new object[] { player });
        if (!IsBound(player) || !ReferenceEquals(World, world))
            throw new InvalidOperationException("Effects replacement binding did not stick");
    }
}
