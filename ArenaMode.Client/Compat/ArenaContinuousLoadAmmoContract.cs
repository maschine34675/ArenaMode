using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using EFT;
using Comfort.Common;
using EFT.InventoryLogic;
using HarmonyLib;

using Version = System.Version;

namespace ArenaMode.Compat;

internal sealed class ArenaContinuousLoadAmmoContract
{
    internal const string PluginGuid = "com.ozen.continuousloadammo";
    internal static readonly Version ReviewedVersion = new(1, 2, 0, 0);
    internal Type ControllerType;
    internal FieldInfo Current, Body;
    internal PropertyInfo Inventory, Active, SpeedLimit, MultiSelect;
    internal MethodInfo Register, Dispose, OnDispose, State, LoadingEnd, Outside, EndPostfix;
    internal EventInfo EndEvent, CloseEvent;
    internal MethodInfo[] Starts;

    internal static ArenaContinuousLoadAmmoContract Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("Continuous Load Ammo 1.2.0 or newer is required");
        if (plugin == null || plugin.FullName != "ContinuousLoadAmmo.ContinuousLoadAmmo"
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin) || plugin.IsAbstract)
            throw new MissingMemberException("Continuous Load Ammo plugin identity changed");
        var asm = plugin.Assembly; var id = asm.GetName(); var attr = plugin.GetCustomAttribute<BepInPlugin>(false);
        if (id.Name != "ozen.ContinuousLoadAmmo" || Normalize(id.Version) != version
            || attr?.GUID != PluginGuid || Normalize(attr.Version) != version)
            throw new InvalidOperationException("Continuous Load Ammo plugin and assembly metadata disagree");
        var result = new ArenaContinuousLoadAmmoContract();
        var registration = asm.GetType("ContinuousLoadAmmo.Patches.RegisterPlayerPatch", true);
        result.ControllerType = asm.GetType("ContinuousLoadAmmo.Controllers.LoadAmmoController", true);
        var ends = asm.GetType("ContinuousLoadAmmo.Patches.LoadingEndPatches", true);
        result.Register = Method(registration, "Postfix", true, typeof(void), typeof(GameWorld), typeof(IPlayer));
        result.Dispose = Method(result.ControllerType, "Dispose", false, typeof(void));
        result.OnDispose = Method(registration, "OnDispose", true, typeof(void));
        result.State = Method(result.ControllerType, "SetPlayerStateAsync", false, typeof(Task), typeof(bool));
        result.LoadingEnd = Method(result.ControllerType, "LoadingEnd", false, typeof(void));
        result.Outside = Method(result.ControllerType, "LoadingOutsideInventory", false, typeof(void));
        result.EndPostfix = Method(ends, "Postfix", true, typeof(void), typeof(Task<IResult>));
        result.Current = Field(registration, "_loadAmmoController", true, result.ControllerType);
        result.Body = Field(result.ControllerType, "_player", false, typeof(Player));
        result.Inventory = Property(result.ControllerType, "PlayerInventoryController", false, typeof(Player.PlayerInventoryController));
        result.Active = Property(result.ControllerType, "IsActive", false, typeof(bool));
        result.SpeedLimit = Property(plugin, "SpeedLimit", true, typeof(ConfigEntry<float>));
        result.MultiSelect = Property(asm.GetType("ContinuousLoadAmmo.Utils.MultiSelectInterop", true), "MultiSelectLoadSerializerIsActive", true, typeof(bool));
        result.EndEvent = Event(ends, "OnLoadingEnd");
        result.CloseEvent = Event(asm.GetType("ContinuousLoadAmmo.Patches.InventoryScreenClosePatch", true), "OnInventoryClose");
        result.Starts = Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m)?.Postfixes.Any(p => p.PatchMethod == result.EndPostfix) == true)
            .OfType<MethodInfo>().ToArray();
        result.VerifyNativeBindings();
        return result;
    }

    internal void VerifyNativeBindings()
    {
        var register = typeof(GameWorld).GetMethod(nameof(GameWorld.RegisterPlayer), new[] { typeof(IPlayer) });
        Receipt(register, Register);
        var native = new[] { typeof(Player.PlayerInventoryController.LoadMagazineProcess), typeof(Player.PlayerInventoryController.UnloadMagazineProcess) };
        if (Starts == null || native.Any(t => !Starts.Any(s => s.DeclaringType == t && s.Name == "Start")))
            throw new MissingMethodException("Continuous Load Ammo load/unload completion hooks missing");
        var actual = Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m)?.Postfixes.Any(p => p.PatchMethod == EndPostfix) == true).ToArray();
        if (actual.Length != Starts.Length || actual.Any(m => !Starts.Contains(m)))
            throw new MissingMethodException("Continuous Load Ammo completion targets changed");
        foreach (var start in Starts)
        {
            if (start.IsStatic || start.Name != "Start" || start.GetParameters().Length != 0 || start.ReturnType != typeof(Task<IResult>)
                || start.DeclaringType.ContainsGenericParameters || !typeof(IMagazineLoadingProcess).IsAssignableFrom(start.DeclaringType))
                throw new MissingMethodException("Continuous Load Ammo completion API changed");
            var owner = start.DeclaringType.GetField("_inventoryController", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (owner == null || !typeof(InventoryController).IsAssignableFrom(owner.FieldType))
                throw new MissingFieldException("Continuous Load Ammo process inventory ownership changed");
            Receipt(start, EndPostfix);
        }
        if (SpeedLimit.GetValue(null) is not ConfigEntry<float>) throw new InvalidOperationException("Continuous Load Ammo speed configuration is unavailable");
    }
    private static void Receipt(MethodInfo native, MethodInfo foreign)
    {
        var info = native == null ? null : Harmony.GetPatchInfo(native);
        if (info == null || info.Postfixes.Count(p => p.PatchMethod == foreign) != 1
            || info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers).Concat(info.ILManipulators)
                .Count(p => p.PatchMethod.DeclaringType?.Assembly == foreign.DeclaringType.Assembly) != 1)
            throw new MissingMethodException("Continuous Load Ammo native hook changed: " + native?.Name);
    }
    private static MethodInfo Method(Type type, string name, bool isStatic, Type returns, params Type[] args)
    {
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.IsStatic != isStatic || method.ReturnType != returns || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }
    private static FieldInfo Field(Type type, string name, bool isStatic, Type value)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (field == null || field.IsStatic != isStatic || field.FieldType != value) throw new MissingFieldException(type.FullName, name);
        return field;
    }
    private static PropertyInfo Property(Type type, string name, bool isStatic, Type value)
    {
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (property?.GetGetMethod(true) == null || property.GetGetMethod(true).IsStatic != isStatic || property.PropertyType != value)
            throw new MissingMemberException(type.FullName, name);
        return property;
    }
    private static EventInfo Event(Type type, string name)
    {
        var e = type.GetEvent(name, BindingFlags.Public | BindingFlags.Static);
        if (e?.EventHandlerType != typeof(Action) || e.GetRemoveMethod()?.IsStatic != true)
            throw new MissingMemberException(type.FullName, name);
        return e;
    }
    internal static Version Normalize(Version v) => v == null || v.Build < 0 ? null : new(v.Major, v.Minor, v.Build, Math.Max(0, v.Revision));
}
