using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using EFT;
using EFT.Animations.NewRecoil;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
using Version = System.Version;

namespace ArenaMode.Compat;

internal sealed class ArenaRecoilLegacyContract
{
    internal const string PluginGuid = "com.pein.camerarecoilmod";
    internal static readonly Version ReviewedVersion = new(2, 0, 0, 0);
    internal Type RecoilType, CameraType;
    internal PropertyInfo RecoilInstance, CameraInstance;
    internal MethodInfo InitPostfix, RecalculatePostfix, RecoilUpdate, CameraUpdate;
    internal FieldInfo[] Cache;
    internal FieldInfo RecoilPlayer;
    internal PropertyInfo Spring;
    internal FieldInfo[] CameraSprings;

    internal static ArenaRecoilLegacyContract Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("Recoil Rework Legacy 2.0.0 or newer is required");
        if (plugin == null || plugin.FullName != "SPTRecoilRework.Plugin" || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin))
            throw new MissingMemberException("Recoil Rework Legacy plugin identity changed");
        var asm = plugin.Assembly; var attr = plugin.GetCustomAttribute<BepInPlugin>(false);
        if (asm.GetName().Name != "SPTRecoilReworkLegacyClient" || Normalize(asm.GetName().Version) != version
            || attr?.GUID != guid || Normalize(attr.Version) != version)
            throw new InvalidOperationException("Recoil Rework Legacy assembly/plugin versions disagree");
        var c = new ArenaRecoilLegacyContract();
        c.RecoilType = asm.GetType("SPTRecoilRework.Components.RealRecoilComponent", true);
        c.CameraType = asm.GetType("SPTRecoilRework.Components.CameraOffsetComponent", true);
        foreach (var t in new[] { c.RecoilType, c.CameraType })
        {
            if (!typeof(MonoBehaviour).IsAssignableFrom(t) || t.IsAbstract)
                throw new MissingMemberException("Recoil component type changed");
            Method(t, "Awake", false, typeof(void));
        }
        c.RecoilInstance = Singleton(c.RecoilType);
        c.CameraInstance = Singleton(c.CameraType);
        c.RecoilPlayer = Field(c.RecoilType, "_player", false, typeof(Player));
        var spring = asm.GetType("SPTRecoilRework.Helpers.RecoilSpring", true);
        c.Spring = c.RecoilType.GetProperty("RecoilSpring", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (c.Spring?.GetGetMethod() == null || c.Spring.PropertyType != spring) throw new MissingMemberException("Recoil spring changed");
        c.CameraSprings = new[] { "SlowShakeSpring", "FastShakeSpring", "CameraSpring" }
            .Select(n => Field(c.CameraType, n, false, spring)).ToArray();
        c.RecoilUpdate = Method(c.RecoilType, "FixedUpdate", false, typeof(void));
        c.CameraUpdate = Method(c.CameraType, "Update", false, typeof(void));
        c.InitPostfix = Method(asm.GetType("SPTRecoilRework.Patches.PlayerInitPatch", true), "PatchPostfix", true, typeof(void), typeof(Player));
        c.RecalculatePostfix = Method(asm.GetType("SPTRecoilRework.Patches.RecalculateRecoilOnWeaponSwitchPatch", true),
            "PatchPostfix", true, typeof(void), typeof(NewRecoilShotEffect), typeof(WeaponTemplate),
            typeof(GlobalConfiguration.AimingConfiguration), typeof(Player.FirearmController), typeof(float),
            typeof(float), typeof(float), typeof(float));
        var helper = asm.GetType("SPTRecoilRework.Helpers.WeaponHelper", true);
        c.Cache = new[] {
            Field(helper, "CurrentFirearmController", true, typeof(Player.FirearmController)),
            Field(helper, "CurrentTemplate", true, typeof(WeaponTemplate)),
            Field(helper, "CurrentRecoilMult", true, typeof(Vector2)),
            Field(helper, "CurrentRecoilVals", true, typeof(Vector2)),
            Field(helper, "IsPistolCurrentlyEquipped", true, typeof(bool))
        };
        c.VerifyNativeBindings();
        return c;
    }

    internal void VerifyNativeBindings()
    {
        Receipt(typeof(Player).GetMethods(BindingFlags.Public | BindingFlags.Instance).Single(m => m.Name == "Init"), InitPostfix);
        Receipt(typeof(NewRecoilShotEffect).GetMethod("RecalculateRecoilParamsOnChangeWeapon"), RecalculatePostfix);
    }
    private static void Receipt(MethodInfo target, MethodInfo foreign)
    {
        var info = target == null ? null : Harmony.GetPatchInfo(target);
        if (info == null || info.Postfixes.Count(p => p.PatchMethod == foreign) != 1
            || info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers).Concat(info.ILManipulators)
                .Count(p => p.PatchMethod.DeclaringType?.Assembly == foreign.DeclaringType.Assembly) != 1)
            throw new MissingMethodException("Recoil Rework Legacy native hook changed: " + target?.Name);
    }
    private static PropertyInfo Singleton(Type t)
    {
        var p = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (p?.PropertyType != t || p.GetGetMethod()?.IsStatic != true || p.GetSetMethod(true)?.IsStatic != true)
            throw new MissingMemberException(t.FullName, "Instance");
        return p;
    }
    private static MethodInfo Method(Type t, string n, bool stat, Type result, params Type[] args)
    {
        var m = t.GetMethod(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
        if (m == null || m.IsStatic != stat || m.ReturnType != result || m.ContainsGenericParameters || m.GetMethodBody() == null)
            throw new MissingMethodException(t.FullName, n);
        return m;
    }
    private static FieldInfo Field(Type t, string n, bool stat, Type value)
    {
        var f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (f == null || f.IsStatic != stat || f.IsInitOnly || f.FieldType != value) throw new MissingFieldException(t.FullName, n);
        return f;
    }
    internal object[] CaptureCache() => Cache.Select(f => f.GetValue(null)).ToArray();
    internal void RestoreCache(object[] state) { for (int i = 0; i < Cache.Length; ++i) Cache[i].SetValue(null, state[i]); }
    internal void ClearCache() => RestoreCache(new object[] { null, null, Vector2.one, Vector2.zero, false });
    internal static Version Normalize(Version v) => v == null || v.Build < 0 ? null : new(v.Major, v.Minor, v.Build, Math.Max(0, v.Revision));
}
