using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Compat;
using BepInEx.Bootstrap;
using Comfort.Common;
using HarmonyLib;
using QuestingBots;
using QuestingBots.Utils;

internal static class Program
{
    private static int _checks;
    private static readonly MethodInfo Target = typeof(Program).GetMethod(nameof(DummyTarget), BindingFlags.Static | BindingFlags.NonPublic);

    private static int Main()
    {
        try
        {
            CheckAbsentAndAdapters();
            CheckDisabledAtStartup();
            CheckPartialAndStickyActivation();
            CheckExistingPatches();
            CheckUnknownContracts();
            CheckLateOrbit();
            Require(ConfigUtil.CurrentConfigReads == 0, "No scenario invokes the foreign network-capable CurrentConfig getter");
            Console.WriteLine($"OPTIONAL_MOD_ADMISSION_CHECKS_PASSED ({_checks} assertions)");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"OPTIONAL_MOD_ADMISSION_CHECKS_FAILED after {_checks} assertions: {error}");
            return 1;
        }
    }

    private static void CheckAbsentAndAdapters()
    {
        Reset();
        Allowed("No optional mods leaves Arena available");
    }

    private static void CheckDisabledAtStartup()
    {
        foreach (var version in new[] { new Version(1, 1, 0), new Version(1, 1, 0, 0) })
        {
            Reset();
            InstallQuesting(new ModConfig { Enabled = false }, version);
            AddPatch(nameof(Patches.Postfixes), typeof(QuestingBots.Patches.MenuShowPatch));
            Allowed($"Known QB {version}, globally disabled at startup, may retain its menu notification patch");
            Allowed("Repeated menu query stays allowed without another whole-Harmony scan");
            Require(Harmony.EnumerationCount == 1, "Patch registry is scanned once per inspected plugin instance");
        }
        Reset();
        InstallQuesting(new ModConfig { Enabled = false });
        Allowed("Known QB disabled before patch registration is permitted with an empty registry");
    }

    private static void CheckPartialAndStickyActivation()
    {
        Reset();
        var config = new ModConfig { Enabled = true, BotSpawnsEnabled = false, QuestingEnabled = false };
        var plugin = InstallQuesting(config);
        plugin.enabled = false;
        Blocked("Questing Bots", "F12/subsystem flags and MonoBehaviour.enabled do not prove global startup disable");
        config.Enabled = false;
        Blocked("Questing Bots", "An observed active QB remains blocked after changing the cached whole-mod flag");

        Reset();
        config = new ModConfig { Enabled = false };
        InstallQuesting(config);
        Allowed("Initial proven startup-disabled QB admitted");
        config.Enabled = true;
        Blocked("Questing Bots", "Live whole-mod activation revokes admission");
        config.Enabled = false;
        Blocked("Questing Bots", "Admission stays revoked because turning the flag off does not remove installed systems");
    }

    private static void CheckExistingPatches()
    {
        foreach (var kind in new[] { nameof(Patches.Prefixes), nameof(Patches.Postfixes), nameof(Patches.Transpilers), nameof(Patches.Finalizers) })
        {
            Reset();
            InstallQuesting(new ModConfig { Enabled = false });
            AddPatch(kind, typeof(QuestingBots.Patches.BotsControllerInitPatch));
            Blocked("Questing Bots", $"Cached false cannot hide an installed QB gameplay {kind} patch");
        }
        Reset();
        InstallQuesting(new ModConfig { Enabled = false });
        var unrelatedAssembly = ForeignPluginType("UnrelatedForeignPatch");
        AddPatch(nameof(Patches.Prefixes), unrelatedAssembly);
        Allowed("Unrelated plugin patches do not invalidate a genuine QB startup disable");

        Reset();
        InstallQuesting(new ModConfig { Enabled = false });
        Harmony.ThrowOnEnumeration = true;
        Blocked("Questing Bots", "A failed patch audit is never treated as proof of startup disable");
    }

    private static void CheckUnknownContracts()
    {
        foreach (var version in new Version[] { null, new(1, 1), new(1, 0, 9), new(1, 1, 1), new(1, 1, 0, 1), new(2, 0, 0) })
        {
            Reset();
            InstallQuesting(new ModConfig { Enabled = false }, version, explicitVersion: true);
            Blocked("Questing Bots", $"Unreviewed/unknown QB version {version?.ToString() ?? "null"} cannot claim the startup exemption");
        }
        Reset();
        InstallQuesting(null);
        Blocked("Questing Bots", "Null cached server config blocks without a fresh HTTP lookup");
        Reset();
        InstallQuesting(new ModConfig { Enabled = false });
        Singleton<ConfigUtil>.Instance = null;
        Blocked("Questing Bots", "Missing config owner is unknown, not disabled");
        Reset();
        InstallQuesting(new WrongConfig());
        Blocked("Questing Bots", "A non-boolean Enabled contract is rejected");
        Reset();
        InstallQuesting(new MissingConfig());
        Blocked("Questing Bots", "Missing Enabled property is rejected");
        Reset();
        InstallQuesting(new ThrowingConfig());
        Blocked("Questing Bots", "Throwing enabled reader fails closed");
        Reset();
        InstallQuesting(new ModConfig { Enabled = false });
        Chainloader.PluginInfos[ArenaOptionalMods.QuestingBotsGuid].Metadata = null;
        Blocked("Questing Bots", "Missing plugin metadata is rejected");
        Reset();
        Chainloader.PluginInfos[ArenaOptionalMods.QuestingBotsGuid] = new PluginInfo
        {
            Instance = null, Metadata = new PluginMetadata { Version = new Version(1, 1, 0) }
        };
        Blocked("Questing Bots", "An incomplete plugin entry cannot prove disabled startup");
        Reset();
        Chainloader.PluginInfos[ArenaOptionalMods.QuestingBotsGuid] = new PluginInfo
        {
            Instance = Activator.CreateInstance(ForeignPluginType("MissingQBConfiguration")),
            Metadata = new PluginMetadata { Version = new Version(1, 1, 0) }
        };
        Blocked("Questing Bots", "An unknown plugin assembly cannot borrow another assembly's QB configuration");
    }

    private static void CheckLateOrbit()
    {
        Reset();
        Allowed("No ORBIT before its plugin registration");
        Chainloader.PluginInfos[ArenaOptionalMods.OrbitGuid] = new PluginInfo();
        Blocked("ORBIT", "ORBIT registered later is caught even before its delayed Awake work finishes");
        Require(ArenaMoreBotsCompatibility.Calls == 0 && ArenaAiLimitCompat.Calls == 0,
            "ORBIT presence does not consult unrelated readiness or config switches");
        Chainloader.PluginInfos.Remove(ArenaOptionalMods.OrbitGuid);
        Allowed("Plugin discovery is live rather than a cached initial absence");
    }

    private static QuestingBotsPlugin InstallQuesting(object config, Version version = null, bool explicitVersion = false)
    {
        var plugin = new QuestingBotsPlugin();
        Singleton<ConfigUtil>.Instance = new ConfigUtil(config);
        Chainloader.PluginInfos[ArenaOptionalMods.QuestingBotsGuid] = new PluginInfo
        {
            Instance = plugin,
            Metadata = new PluginMetadata { Version = explicitVersion ? version : version ?? new Version(1, 1, 0) }
        };
        return plugin;
    }

    private static void AddPatch(string list, Type declaringType)
    {
        if (!Harmony.Registry.TryGetValue(Target, out var patches))
            Harmony.Registry.Add(Target, patches = new Patches());
        var target = (List<Patch>)typeof(Patches).GetProperty(list).GetValue(patches);
        target.Add(new Patch { PatchMethod = declaringType.GetMethod("PatchPostfix", BindingFlags.Public | BindingFlags.Static) });
    }

    private static Type ForeignPluginType(string name)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var builder = assembly.DefineDynamicModule("Foreign").DefineType(name, TypeAttributes.Public);
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        var method = builder.DefineMethod("PatchPostfix", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        return builder.CreateType();
    }

    private static void Reset()
    {
        Chainloader.PluginInfos.Clear();
        Harmony.Reset();
        Singleton<ConfigUtil>.Instance = null;
        ArenaMoreBotsCompatibility.Reason = null;
        ArenaMoreBotsCompatibility.Calls = 0;
        ArenaAiLimitCompat.Reason = null;
        ArenaAiLimitCompat.Calls = 0;
        // Each installed fake plugin is a distinct process identity; no production
        // statics are reset by reflection, preserving the real sticky-state logic.
    }
    private static void Allowed(string message) => Require(ArenaOptionalMods.GetRejectionReason() == null, message);
    private static void Blocked(string name, string message) => Require(ArenaOptionalMods.GetRejectionReason()?.Contains(name, StringComparison.Ordinal) == true, message);
    private static void Require(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void DummyTarget() { }
    private sealed class ModConfig
    {
        public bool Enabled { get; set; }
        public bool BotSpawnsEnabled { get; set; } = true;
        public bool QuestingEnabled { get; set; } = true;
    }
    private sealed class WrongConfig { public string Enabled => "false"; }
    private sealed class MissingConfig { public bool SomeOtherSwitch => false; }
    private sealed class ThrowingConfig { public bool Enabled => throw new InvalidOperationException("Unreadable config"); }
}
