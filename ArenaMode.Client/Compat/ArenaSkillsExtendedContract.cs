using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using BepInEx;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using UnityEngine;
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>Optional named-type binding. No Skills Extended reference or journal access.</summary>
internal sealed class ArenaSkillsExtendedContract
{
    internal const string PluginGuid = "com.cj.skillsextended";
    internal static readonly Version ReviewedVersion = new(3, 1, 4, 0);
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
    internal readonly MethodInfo Boot, ApplyXp, Started, Medical, Nato, Eastern;
    internal readonly FieldInfo BoundPlayer;
    internal readonly PropertyInfo RuntimeWorld;
    internal readonly Action<IHealthEffect> MedicalHandler;
    internal readonly Action<Mastering> NatoHandler, EasternHandler;
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)).ToDictionary(c => c.Value);

    private ArenaSkillsExtendedContract(Assembly assembly)
    {
        var runtime = assembly.GetType("SkillsExtended.Skills.Signals.SignalsRuntime", true);
        if (!typeof(MonoBehaviour).IsAssignableFrom(runtime) || runtime.IsAbstract)
            throw new MissingMemberException("Skills Extended Signals runtime changed");
        Boot = Method(runtime, "Boot", Static, typeof(Task), typeof(GameWorld));
        ApplyXp = Method(runtime, "ApplyXp", Instance, typeof(void));
        RuntimeWorld = runtime.GetProperty("World", Instance);
        if (RuntimeWorld?.PropertyType != typeof(GameWorld) || RuntimeWorld.GetGetMethod(true) == null
            || RuntimeWorld.GetIndexParameters().Length != 0)
            throw new MissingMemberException("Skills Extended Signals world changed");
        var start = assembly.GetType("SkillsExtended.Skills.Shared.Patches.OnGameStartedPatch", true);
        Started = Method(start, "Postfix", Static, typeof(void), typeof(GameWorld));
        Medical = Method(start, "ApplyMedicalXp", Static, typeof(void), typeof(IHealthEffect));
        Nato = Method(start, "ApplyNatoRifleXp", Static, typeof(void), typeof(Mastering));
        Eastern = Method(start, "ApplyEasternRifleXp", Static, typeof(void), typeof(Mastering));
        BoundPlayer = start.GetField("Player", Static);
        if (BoundPlayer?.FieldType != typeof(Player) || BoundPlayer.IsInitOnly)
            throw new MissingFieldException("Skills Extended event player changed");
        MedicalHandler = (Action<IHealthEffect>)Delegate.CreateDelegate(typeof(Action<IHealthEffect>), Medical);
        NatoHandler = (Action<Mastering>)Delegate.CreateDelegate(typeof(Action<Mastering>), Nato);
        EasternHandler = (Action<Mastering>)Delegate.CreateDelegate(typeof(Action<Mastering>), Eastern);
        VerifyNativeBinding();
        VerifyGraph(assembly);
    }

    internal static ArenaSkillsExtendedContract Resolve(Type plugin, string guid, Version metadata, out Version version)
    {
        version = Normalize(metadata);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("Skills Extended 3.1.4 or newer is required");
        if (plugin == null || plugin.FullName != "SkillsExtended.SkillsExtendedPlugin"
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin) || plugin.IsAbstract)
            throw new MissingMemberException("Skills Extended plugin identity changed");
        var name = plugin.Assembly.GetName();
        var attr = plugin.GetCustomAttribute<BepInPlugin>(false);
        if (name.Name != "SkillsExtended.Client.Skills" || Normalize(name.Version) != version
            || attr?.GUID != PluginGuid || Normalize(attr.Version) != version)
            throw new InvalidOperationException("Skills Extended plugin and assembly metadata disagree");
        var settings = AccessTools.Field(typeof(LocalGame), "_raidSettings");
        if (settings == null || settings.IsStatic || settings.FieldType != typeof(LocalRaidSettings))
            throw new MissingFieldException("Skills Extended native raid identity field changed");
        return new ArenaSkillsExtendedContract(plugin.Assembly);
    }

    internal void VerifyNativeBinding()
    {
        var native = typeof(GameWorld).GetMethod(nameof(GameWorld.OnGameStarted), Type.EmptyTypes);
        var patches = native == null ? null : Harmony.GetPatchInfo(native);
        if (native?.ReturnType != typeof(void) || patches?.Postfixes.Count(p => p.PatchMethod == Started) != 1
            || patches.Postfixes.Any(p => p.PatchMethod?.DeclaringType == Started.DeclaringType && p.PatchMethod != Started))
            throw new MissingMethodException("Skills Extended native world-start binding changed");
    }

    private void VerifyGraph(Assembly assembly)
    {
        var ledger = assembly.GetType("SkillsExtended.Skills.Signals.SignalsXpLedger", true);
        var reserve = Method(ledger, "Reserve", Static, typeof(float), typeof(string), typeof(float));
        var callbacks = new[] { Medical, Nato, Eastern };
        var rewardCallers = new HashSet<MethodBase>();
        var subscribed = new HashSet<MethodInfo>();
        foreach (var type in assembly.GetTypes())
        foreach (var method in type.GetMethods(Static | Instance).Cast<MethodBase>()
            .Concat(type.GetConstructors(Static | Instance)))
        {
            if (method.GetMethodBody() == null) continue;
            foreach (var (op, member) in Members(method))
            {
                if (member == reserve) rewardCallers.Add(method);
                if (member?.DeclaringType == ledger && method.DeclaringType != ledger
                    && !(method == ApplyXp && member == reserve))
                    throw new MissingMethodException("Skills Extended receipt access bypasses reward delivery");
                if (member == BoundPlayer && method != Started && !callbacks.Contains(method))
                    throw new MissingMemberException("Skills Extended has another static event-player consumer");
                if (member is MethodInfo callback && callbacks.Contains(callback))
                {
                    if (method != Started || op != OpCodes.Ldftn)
                        throw new MissingMethodException("Skills Extended callback registration changed");
                    subscribed.Add(callback);
                }
            }
        }
        if (!rewardCallers.SetEquals(new[] { ApplyXp }) || !subscribed.SetEquals(callbacks))
            throw new MissingMethodException("Skills Extended reward/subscription graph changed");
        // Detaching only our exact three foreign delegates must match their
        // actual native event types; do not clear another mod's subscribers.
        var medical = typeof(ActiveHealthController).GetEvent("EffectStartedEvent");
        var mastery = typeof(SkillManager).GetEvent("OnMasteringExperienceChanged");
        if (medical?.EventHandlerType != typeof(Action<IHealthEffect>) || medical.GetRemoveMethod() == null
            || mastery?.EventHandlerType != typeof(Action<Mastering>) || mastery.GetRemoveMethod() == null)
            throw new MissingMemberException("Skills Extended native XP event types changed");
        var registrations = Members(Started).Where(p => p.Member is MethodInfo m
            && m.Name.StartsWith("add_", StringComparison.Ordinal)).Select(p => (MethodInfo)p.Member).ToArray();
        if (registrations.Length != 3 || registrations.Count(m => m.GetBaseDefinition() == medical.GetAddMethod().GetBaseDefinition()) != 1
            || registrations.Count(m => m.GetBaseDefinition() == mastery.GetAddMethod().GetBaseDefinition()) != 2)
            throw new MissingMemberException("Skills Extended registers different native XP events");
    }

    private static MethodInfo Method(Type type, string name, BindingFlags flags, Type result, params Type[] parameters)
    {
        var method = type.GetMethod(name, flags, null, parameters, null);
        if (method?.ReturnType != result || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static IEnumerable<(OpCode Op, MemberInfo Member)> Members(MethodBase method)
    {
        var il = method.GetMethodBody().GetILAsByteArray();
        int at = 0;
        while (at < il.Length)
        {
            short code = il[at++];
            if (code == 0xfe)
            {
                if (at == il.Length) throw new InvalidOperationException("Truncated Skills Extended IL");
                code = (short)(0xfe00 | il[at++]);
            }
            if (!Codes.TryGetValue(code, out var op)) throw new InvalidOperationException("Unknown Skills Extended IL");
            int size;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: size = 0; break;
                case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                case OperandType.InlineVar: size = 2; break;
                case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                case OperandType.InlineSwitch:
                    if (at + 4 > il.Length) throw new InvalidOperationException("Truncated Skills Extended switch");
                    int count = BitConverter.ToInt32(il, at);
                    if (count < 0 || count > (il.Length - at - 4) / 4) throw new InvalidOperationException("Invalid Skills Extended switch");
                    size = 4 + count * 4; break;
                default: size = 4; break;
            }
            if (at + size > il.Length) throw new InvalidOperationException("Truncated Skills Extended operand");
            if (op.OperandType == OperandType.InlineField || op.OperandType == OperandType.InlineMethod)
                yield return (op, method.Module.ResolveMember(BitConverter.ToInt32(il, at),
                    method.DeclaringType?.GetGenericArguments(), (method as MethodInfo)?.GetGenericArguments()));
            at += size;
        }
    }

    internal static Version Normalize(Version value)
        => value == null || value.Build < 0 ? null : new Version(value.Major, value.Minor, value.Build, Math.Max(0, value.Revision));
}
