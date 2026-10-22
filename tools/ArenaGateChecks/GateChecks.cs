// Offline checks of the Arena session gate (ArenaMode.Gate, docs/ARENA-SESSION-20261021.md section 5)
// against the REAL BepInEx 5.4.23.5 core of the game, under the same Mono the game runs on.
//
// One scenario per process (BepInEx and Harmony keep process-wide state):
//   GateChecks.exe <scenario> <workDir> <managedDir> <policyJson> <sptRoot> [-arena-session]
//
// What is real here: BepInEx.dll and BepInEx.Preloader.dll (the patcher discovery, the sorted
// initializer order, PatchAndLoad, the entry-point patch of UnityEngine.CoreModule, TypeLoader with
// its cache file), 0Harmony (HarmonyX 2.9.0) and the Gate assembly exactly as `dotnet build`
// produced it. What is generated: dummy plugins (Cecil, with real BepInPlugin/BepInDependency
// metadata) and dummy preloader patchers (one compiled template, copied under many names).
// What cannot be done here: starting the chainloader (needs a running Unity); the driver calls the
// same TypeLoader.FindPluginTypes<PluginInfo> that Chainloader.Start() calls, with the same
// arguments.
//
// The driver never names BepInEx.PluginInfo, BaseUnityPlugin or the Chainloader in code that runs
// during the preloader phase: they reach UnityEngine.CoreModule, and the point of several checks is
// to prove that nothing loads that assembly before the preloader has patched and loaded it.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ArenaMode.Shared.Session;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Preloader.Patching;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class GateChecks
{
    private const string CallsKey = "arena-gate-checks.calls";
    private const string GateSource = "maschine-ArenaMode.Gate";
    private const string GateHarmonyId = "com.maschine.ArenaMode.Gate";
    private const string GateAssemblyName = "maschine-ArenaMode.Gate";

    private static string _scenario;
    private static string _work;
    private static string _managedSource;
    private static string _policySource;
    private static string _sptRoot;
    private static string _driverDir;
    private static bool _flag;
    private static int _passed;
    private static int _failed;
    private static int _counter;

    private static readonly List<string> Calls = new List<string>();
    private static readonly Capture Log = new Capture();

    private const string EarlyDeniedAssembly = "AAA-early-denied";
    private static bool _omitEarlyPatcher;

    /// <summary>The files the report has to list as patchersRanBeforeGate in the running scenario (see ExpectedRanBeforeGate).</summary>
    private static List<string> _expectedRanBeforeGate = new List<string>();

    // ------------------------------------------------------------------ entry point

    public static int Main(string[] args)
    {
        List<string> positional = args.Where(a => !a.StartsWith("-")).ToList();
        if (positional.Count < 5)
        {
            Console.WriteLine("usage: GateChecks.exe <scenario> <workDir> <managedDir> <policyJson> <sptRoot> [-arena-session]");
            return 2;
        }

        _scenario = positional[0];
        _work = positional[1];
        _managedSource = positional[2];
        _policySource = positional[3];
        _sptRoot = positional[4];
        _driverDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        _flag = ArenaSessionContract.HasSessionFlag(Environment.GetCommandLineArgs());
        Console.WriteLine("scenario " + _scenario + " (flag " + (_flag ? "present" : "absent") + ")");

        try
        {
            switch (_scenario)
            {
                case "inert": ScenarioWorld("inert", new WorldOptions { Kind = WorldKind.Inert }); break;
                case "full": ScenarioWorld("full", new WorldOptions { Kind = WorldKind.Full }); break;
                case "no-early-patcher": ScenarioWorld("no-early-patcher", new WorldOptions { Kind = WorldKind.Full, OmitEarlyPatcher = true }); break;
                case "unidentified-patcher": ScenarioUnidentifiedPatchers(); break;
                case "gate-unlocated": ScenarioGateUnlocated(); break;
                case "user-file": ScenarioWorld("user-file", new WorldOptions { Kind = WorldKind.UserFile }); break;
                case "user-garbage": ScenarioWorld("user-garbage", new WorldOptions { Kind = WorldKind.UserGarbage }); break;
                case "user-oversize": ScenarioWorld("user-oversize", new WorldOptions { Kind = WorldKind.UserOversize }); break;
                case "user-blacklist": ScenarioWorld("user-blacklist", new WorldOptions { Kind = WorldKind.UserBlacklist }); break;
                case "default-deny": ScenarioWorld("default-deny", new WorldOptions { Kind = WorldKind.DefaultDeny }); break;
                case "bad-default": ScenarioWorld("bad-default", new WorldOptions { Kind = WorldKind.BadDefault }); break;
                case "missing-default": ScenarioWorld("missing-default", new WorldOptions { Kind = WorldKind.MissingDefault }); break;
                case "no-core-module": ScenarioWorld("no-core-module", new WorldOptions { Kind = WorldKind.NoCoreModule }); break;
                case "postfix-failure": ScenarioWorld("postfix-failure", new WorldOptions { Kind = WorldKind.PostfixFailure }); break;
                case "control-early-install": ScenarioControlEarlyInstall(); break;
                case "baseline-real": ScenarioBaselineReal(); break;
                case "real-install": ScenarioRealInstall(); break;
                default:
                    Bad("known scenario", "unknown scenario '" + _scenario + "'");
                    break;
            }
        }
        catch (Exception ex)
        {
            Bad("scenario ran to the end", ex.ToString());
        }

        Console.WriteLine("RESULT " + _scenario + " passed=" + _passed + " failed=" + _failed);
        Console.Out.Flush();
        Environment.Exit(_failed == 0 ? 0 : 1);
        return _failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ check helpers

    private static void Ok(string name)
    {
        _passed++;
        Console.WriteLine("  ok   " + name);
    }

    private static void Bad(string name, string detail)
    {
        _failed++;
        Console.WriteLine("  FAIL " + name + " :: " + detail);
    }

    private static void Check(bool condition, string name, string detail)
    {
        if (condition) Ok(name);
        else Bad(name, detail);
    }

    private static void Check(bool condition, string name)
    {
        Check(condition, name, "condition is false");
    }

    private static void SameSet(string name, IEnumerable<string> expected, IEnumerable<string> actual)
    {
        List<string> e = expected.OrderBy(x => x, StringComparer.Ordinal).ToList();
        List<string> a = actual.OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (e.SequenceEqual(a, StringComparer.Ordinal))
        {
            Ok(name);
            return;
        }

        var sb = new StringBuilder();
        foreach (string x in e.Except(a, StringComparer.Ordinal)) sb.Append(" missing:").Append(x);
        foreach (string x in a.Except(e, StringComparer.Ordinal)) sb.Append(" unexpected:").Append(x);
        if (sb.Length == 0) sb.Append(" (same elements, different multiplicity)");
        Bad(name, sb.ToString().Trim());
    }

    private static void Mark(string what)
    {
        Calls.Add(what);
    }

    // ------------------------------------------------------------------ world description

    private enum WorldKind
    {
        Inert,
        Full,
        UserFile,
        UserBlacklist,
        DefaultDeny,
        UserGarbage,
        UserOversize,
        BadDefault,
        MissingDefault,
        NoCoreModule,
        PostfixFailure,
    }

    private sealed class WorldOptions
    {
        public WorldKind Kind;

        /// <summary>Leave out the one denied dummy patcher that sorts before the gate (nothing is then denied and sorts before it).</summary>
        public bool OmitEarlyPatcher;
    }

    private sealed class Dep
    {
        public string Guid;
        public bool Hard;
    }

    private sealed class PluginSpec
    {
        public string Path;
        public string Guid;
        public string Name;
        public string Version;
        public List<Dep> Deps = new List<Dep>();

        public PluginSpec Hard(string guid)
        {
            Deps.Add(new Dep { Guid = guid, Hard = true });
            return this;
        }

        public PluginSpec Soft(string guid)
        {
            Deps.Add(new Dep { Guid = guid, Hard = false });
            return this;
        }
    }

    private static PluginSpec P(string path, string guid, string name, string version)
    {
        return new PluginSpec { Path = path, Guid = guid, Name = name, Version = version };
    }

    /// <summary>The dummy plugin set. Expectations per policy are kept in <see cref="Expected"/>.</summary>
    private static List<PluginSpec> WorldPlugins()
    {
        return new List<PluginSpec>
        {
            P("spt/spt-core.dll", "com.SPT.core", "SPT Core", "4.1.6"),
            P("spt/spt-custom.dll", "com.SPT.custom", "SPT Custom", "4.1.6"),
            P("spt/spt-debugging.dll", "com.SPT.debugging", "SPT Debugging", "4.1.6"),
            P("spt/spt-singleplayer.dll", "com.SPT.singleplayer", "SPT SinglePlayer", "4.1.6"),
            P("spt/ConfigurationManager.dll", "com.bepis.bepinex.configurationmanager", "Configuration Manager", "18.4.1"),
            P("fika/fika.dll", "com.fika.core", "Fika Core", "2.0.0"),
            P("maschine-ArenaMode/ArenaMode.Client.dll", "com.maschine.ArenaMode", "ArenaMode", "0.1.3").Soft("x.softonly"),
            P("SAIN/SAIN.dll", "me.sol.sain", "SAIN", "4.5.2").Hard("xyz.drakia.bigbrain").Hard("x.helperlib").Soft("x.softonly"),
            P("old/SAIN.dll", "me.sol.sain", "SAIN", "4.0.0"),
            P("BigBrain.dll", "xyz.drakia.bigbrain", "BigBrain", "1.5.0"),
            P("Waypoints.dll", "xyz.drakia.waypoints", "Waypoints", "1.9.0").Hard("xyz.drakia.bigbrain"),
            P("ABPS/abps.dll", "com.acidphantasm.botplacementsystem", "ABPS", "2.1.0"),
            P("tools/UiBridge.dll", "com.maschine.UiBridge", "UiBridge", "0.9.4"),
            P("Foreign.dll", "com.example.foreign", "Foreign Mod", "1.0.0"),
            P("orbit/ORBIT.dll", "com.chazut.orbit", "ORBIT", "1.0.0"),
            P("helper/helperlib.dll", "x.helperlib", "Helper Library", "1.0.0").Hard("x.deeper"),
            P("helper/deeper.dll", "x.deeper", "Deeper Library", "1.0.0"),
            P("soft/softonly.dll", "x.softonly", "Soft Only", "1.0.0"),
        };
    }

    /// <summary>
    /// What the shipped default policy decides for the dummy plugins, by relative path:
    /// "allowed:&lt;reason&gt;" or "excluded:&lt;reason&gt;". Written by hand, not derived from the
    /// shared policy code, so that a wrong policy cannot hide behind itself.
    /// </summary>
    private static Dictionary<string, string> Expected(WorldKind kind)
    {
        var d = new Dictionary<string, string>
        {
            { "spt/spt-core.dll", "allowed:platform" },
            { "spt/spt-custom.dll", "allowed:platform" },
            { "spt/spt-debugging.dll", "allowed:platform" },
            { "spt/spt-singleplayer.dll", "allowed:platform" },
            { "spt/ConfigurationManager.dll", "allowed:platform" },
            { "fika/fika.dll", "allowed:platform" },
            { "maschine-ArenaMode/ArenaMode.Client.dll", "allowed:arena" },
            { "SAIN/SAIN.dll", "allowed:whitelist" },
            { "old/SAIN.dll", "allowed:whitelist" },
            { "BigBrain.dll", "allowed:whitelist" },
            { "Waypoints.dll", "allowed:whitelist" },
            { "ABPS/abps.dll", "allowed:whitelist" },
            { "tools/UiBridge.dll", "allowed:tool" },
            { "Foreign.dll", "excluded:not-on-whitelist" },
            { "orbit/ORBIT.dll", "excluded:denied" },
            { "helper/helperlib.dll", "allowed:dependency-of:me.sol.sain" },
            { "helper/deeper.dll", "allowed:dependency-of:x.helperlib" },
            { "soft/softonly.dll", "excluded:not-on-whitelist" },
        };

        if (kind == WorldKind.DefaultDeny)
        {
            // The default list denies com.example.foreign on top of the shipped denies (ORBIT): a unit nothing
            // else lists, so the only thing that changes is the reason.
            d["Foreign.dll"] = "excluded:denied";
        }
        else if (kind == WorldKind.UserFile)
        {
            // allow: Foreign Mod, ORBIT (a user allow lifts a default deny); deny: SAIN, SPT core, three patchers
            d["Foreign.dll"] = "allowed:whitelist";
            d["orbit/ORBIT.dll"] = "allowed:whitelist";
            d["SAIN/SAIN.dll"] = "excluded:denied-by-user";
            d["old/SAIN.dll"] = "excluded:denied-by-user";
            d["helper/helperlib.dll"] = "excluded:not-on-whitelist";
            d["helper/deeper.dll"] = "excluded:not-on-whitelist";
        }
        else if (kind == WorldKind.UserBlacklist)
        {
            // "unlisted": "allow"; deny: SAIN, Foreign Mod, Deeper Library, three patchers, SPT core.
            // Everything nobody names is allowed, except what the shipped default denies (ORBIT keeps its reason
            // "denied" in blacklist mode). Deeper Library is denied and a deny wins over hard dependencies: it is
            // not pulled in, and Helper Library (unlisted, would be allowed) needs it, so it is excluded as well.
            d["SAIN/SAIN.dll"] = "excluded:denied-by-user";
            d["old/SAIN.dll"] = "excluded:denied-by-user";
            d["Foreign.dll"] = "excluded:denied-by-user";
            d["helper/helperlib.dll"] = "excluded:requires-denied:x.deeper";
            d["helper/deeper.dll"] = "excluded:denied-by-user";
            d["soft/softonly.dll"] = "allowed:unlisted-allowed";
        }

        return d;
    }

    /// <summary>The patchers the user file of the user-file scenario denies (whitelist mode).</summary>
    private static readonly string[] UserFileDeniedPatchers = { "maschine-LogTools.dll", "WTT-ContentBackportPatcher.dll", "AAA-early-denied.dll" };

    /// <summary>The patchers the user file of the user-blacklist scenario denies (blacklist mode).</summary>
    private static readonly string[] UserBlacklistDeniedPatchers = { "maschine-LogTools.dll", "WTT-ContentBackportPatcher.dll", "AAA-early-denied.dll" };

    /// <summary>The patchers the default-deny scenario adds to the shipped deny list (the shipped list allows the WTT one).</summary>
    private static readonly string[] DefaultDeniedPatchers = { "WTT-ContentBackportPatcher.dll", "AAA-early-denied.dll" };

    private sealed class PatcherSpec
    {
        public string AssemblyName;
        public string RelativePath;
        public bool AllowedByDefault;
    }

    /// <summary>The dummy patchers of the generated worlds. The names decide the sort order.</summary>
    private static List<PatcherSpec> WorldPatchers()
    {
        var patchers = new List<PatcherSpec>
        {
            new PatcherSpec { AssemblyName = EarlyDeniedAssembly, RelativePath = EarlyDeniedAssembly + ".dll", AllowedByDefault = false },
            new PatcherSpec { AssemblyName = "FixPluginTypesSerialization", RelativePath = "FixPluginTypesSerialization.dll", AllowedByDefault = true },
            new PatcherSpec { AssemblyName = "maschine-LogTools", RelativePath = "maschine-LogTools.dll", AllowedByDefault = true },
            new PatcherSpec { AssemblyName = "spt-prepatch", RelativePath = "spt-prepatch.dll", AllowedByDefault = true },
            new PatcherSpec { AssemblyName = "WTT-ContentBackportPatcher", RelativePath = "WTT-ContentBackportPatcher/WTT-ContentBackportPatcher.dll", AllowedByDefault = true },
            new PatcherSpec { AssemblyName = "ZZZ-nested-denied", RelativePath = "nested/ZZZ-nested-denied.dll", AllowedByDefault = false },
            new PatcherSpec { AssemblyName = "zzz-late-denied", RelativePath = "zzz-late-denied.dll", AllowedByDefault = false },
        };
        if (_omitEarlyPatcher) patchers.RemoveAll(p => p.AssemblyName == EarlyDeniedAssembly);
        return patchers;
    }

    /// <summary>
    /// Whether the policy of a world lets the dummy patcher run and, when it does not, the words the gate's warning uses
    /// for the list that keeps it off. Written from the world definitions above, not from anything the gate says.
    /// </summary>
    private static bool PatcherAllowedInWorld(WorldKind kind, PatcherSpec p, out string cause)
    {
        bool allowed = p.AllowedByDefault;
        string leaf = Path.GetFileName(p.RelativePath);
        cause = "is not on the whitelist";
        if (kind == WorldKind.UserFile)
        {
            if (UserFileDeniedPatchers.Contains(leaf))
            {
                allowed = false;
                cause = "is denied in the user's policy file";
            }

            if (leaf == "ZZZ-nested-denied.dll") allowed = true;
        }
        else if (kind == WorldKind.UserBlacklist)
        {
            // Blacklist: every patcher runs except the ones the user denied.
            allowed = !UserBlacklistDeniedPatchers.Contains(leaf);
            cause = "is denied in the user's policy file";
        }
        else if (kind == WorldKind.DefaultDeny && DefaultDeniedPatchers.Contains(leaf))
        {
            allowed = false;
            cause = "is denied by the default policy list";
        }

        return allowed;
    }

    /// <summary>
    /// The file names the report has to list as patchersRanBeforeGate: the patchers the world's policy denies that sort
    /// before the gate in the preloader's real patcher order, because their initializer has run when the gate gets to
    /// them. A policy the gate could not load neutralises nothing, so nothing is listed there either.
    /// </summary>
    private static List<string> ExpectedRanBeforeGate(WorldKind kind, List<PatcherSpec> patchers, List<string> sortedAssemblies, int gateIndex)
    {
        var files = new List<string>();
        if (kind == WorldKind.Inert || kind == WorldKind.BadDefault || kind == WorldKind.MissingDefault) return files;
        foreach (PatcherSpec p in patchers)
        {
            string cause;
            if (PatcherAllowedInWorld(kind, p, out cause)) continue;
            int index = sortedAssemblies.IndexOf(p.AssemblyName);
            if (index >= 0 && index < gateIndex) files.Add(Path.GetFileName(p.RelativePath));
        }

        return files;
    }

    /// <summary>
    /// The published JSON carries "patchersRanBeforeGate" as an array of exactly the expected file names, and every one of
    /// them is also a neutralised patcher. Read from the raw text and not only through ArenaGateReport.TryParse, which
    /// reads a missing member as an empty list: the member is present in every report, empty when nothing ran early.
    /// </summary>
    private static void CheckRanBeforeGateJson(string when, string json, List<string> expected)
    {
        object root = null;
        string error = null;
        Dictionary<string, object> obj = json != null && ArenaMiniJson.TryParse(json, out root, out error) ? ArenaMiniJson.AsObject(root) : null;
        object member = null;
        List<object> array = obj != null && obj.TryGetValue("patchersRanBeforeGate", out member) ? ArenaMiniJson.AsArray(member) : null;
        Check(array != null, "the report " + when + " has the array member patchersRanBeforeGate (also when it is empty)",
            json == null ? "no report" : "json: " + json);
        if (array == null) return;

        Check(array.All(x => x is string), "every entry of patchersRanBeforeGate " + when + " is a file name");
        SameSet("the report " + when + " lists exactly the denied patchers that sort before the gate as ran before the gate",
            expected, array.OfType<string>());
        ArenaGateReport parsed;
        string parseError;
        if (ArenaGateReport.TryParse(json, out parsed, out parseError))
        {
            Check(parsed.PatchersRanBeforeGate.All(f => parsed.PatchersNeutralised.Contains(f)),
                "every patcher that ran before the gate " + when + " is also listed as neutralised",
                "ran before: " + string.Join(", ", parsed.PatchersRanBeforeGate.ToArray()) + " / neutralised: " + string.Join(", ", parsed.PatchersNeutralised.ToArray()));
        }
        else
        {
            Bad("the report " + when + " parses", parseError);
        }
    }

    // ------------------------------------------------------------------ the fake game

    private sealed class Fake
    {
        public string Root;
        public string Exe;
        public string Plugins;
        public string Patchers;
        public string Config;
        public string Core;
        public string Managed;
        public string PolicyFile;
        public string UserFile;
        public string GateFile;

        public static Fake Create(string name, bool coreModule)
        {
            var f = new Fake { Root = Path.Combine(_work, "game-" + name) };
            if (Directory.Exists(f.Root)) Directory.Delete(f.Root, true);

            f.Exe = Path.Combine(f.Root, "Game.exe");
            string bepinex = Path.Combine(f.Root, "BepInEx");
            f.Plugins = Path.Combine(bepinex, "plugins");
            f.Patchers = Path.Combine(bepinex, "patchers");
            f.Config = Path.Combine(bepinex, "config");
            f.Core = Path.Combine(bepinex, "core");
            f.Managed = Path.Combine(Path.Combine(f.Root, "Game_Data"), "Managed");
            foreach (string dir in new[] { f.Plugins, f.Patchers, f.Config, f.Core, f.Managed, Path.Combine(bepinex, "cache") })
            {
                Directory.CreateDirectory(dir);
            }

            foreach (string file in Directory.GetFiles(_driverDir, "*.dll"))
            {
                string leaf = Path.GetFileName(file);
                if (leaf.StartsWith("BepInEx", StringComparison.Ordinal) || leaf.StartsWith("Mono", StringComparison.Ordinal)
                    || leaf.StartsWith("0Harmony", StringComparison.Ordinal) || leaf.StartsWith("HarmonyX", StringComparison.Ordinal))
                {
                    File.Copy(file, Path.Combine(f.Core, leaf), true);
                }
            }

            WriteTargetAssembly(Path.Combine(f.Managed, "Target.dll"));
            if (coreModule) File.Copy(Path.Combine(_managedSource, "UnityEngine.CoreModule.dll"), Path.Combine(f.Managed, "UnityEngine.CoreModule.dll"), true);

            f.PolicyFile = Path.Combine(Path.Combine(f.Plugins, ArenaSessionContract.PluginFolderName), ArenaSessionContract.DefaultPolicyFileName);
            f.UserFile = Path.Combine(f.Config, ArenaSessionContract.UserPolicyFileName);
            f.GateFile = Path.Combine(Path.Combine(f.Patchers, ArenaSessionContract.PluginFolderName), ArenaSessionContract.GateFileName);

            typeof(Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { f.Exe, null, f.Managed, null });
            return f;
        }
    }

    private static void WriteTargetAssembly(string path)
    {
        AssemblyDefinition asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Target", new Version(1, 0, 0, 0)), "Target", ModuleKind.Dll);
        asm.MainModule.Types.Add(new TypeDefinition("Target", "Marker", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, asm.MainModule.TypeSystem.Object));
        asm.Write(path);
    }

    private static void WritePlugin(string path, PluginSpec s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string assemblyName = "Dummy" + (_counter++) + "_" + Path.GetFileNameWithoutExtension(path);

        // Writing an enum-typed attribute argument makes Cecil resolve the enum, hence BepInEx.dll.
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(_driverDir);
        AssemblyDefinition asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(assemblyName, new Version(1, 0, 0, 0)), assemblyName,
            new ModuleParameters { Kind = ModuleKind.Dll, AssemblyResolver = resolver });
        ModuleDefinition module = asm.MainModule;

        var bepinex = new AssemblyNameReference("BepInEx", new Version(5, 4, 23, 5));
        module.AssemblyReferences.Add(bepinex);
        var baseType = new TypeReference("BepInEx", "BaseUnityPlugin", module, bepinex);
        var pluginAttribute = new TypeReference("BepInEx", "BepInPlugin", module, bepinex);
        var dependencyAttribute = new TypeReference("BepInEx", "BepInDependency", module, bepinex);
        var flagsEnum = new TypeReference(string.Empty, "DependencyFlags", module, null) { DeclaringType = dependencyAttribute, IsValueType = true };
        TypeReference str = module.TypeSystem.String;

        var type = new TypeDefinition("Dummy", "Plugin", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, baseType);
        module.Types.Add(type);

        var baseCtor = new MethodReference(".ctor", module.TypeSystem.Void, baseType) { HasThis = true };
        var ctor = new MethodDefinition(".ctor",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ILProcessor il = ctor.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, baseCtor);
        il.Emit(OpCodes.Ret);
        type.Methods.Add(ctor);

        var pluginCtor = new MethodReference(".ctor", module.TypeSystem.Void, pluginAttribute) { HasThis = true };
        pluginCtor.Parameters.Add(new ParameterDefinition(str));
        pluginCtor.Parameters.Add(new ParameterDefinition(str));
        pluginCtor.Parameters.Add(new ParameterDefinition(str));
        var plugin = new CustomAttribute(pluginCtor);
        plugin.ConstructorArguments.Add(new CustomAttributeArgument(str, s.Guid));
        plugin.ConstructorArguments.Add(new CustomAttributeArgument(str, s.Name));
        plugin.ConstructorArguments.Add(new CustomAttributeArgument(str, s.Version));
        type.CustomAttributes.Add(plugin);

        foreach (Dep dep in s.Deps)
        {
            var dependencyCtor = new MethodReference(".ctor", module.TypeSystem.Void, dependencyAttribute) { HasThis = true };
            dependencyCtor.Parameters.Add(new ParameterDefinition(str));
            dependencyCtor.Parameters.Add(new ParameterDefinition(flagsEnum));
            var attribute = new CustomAttribute(dependencyCtor);
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(str, dep.Guid));
            // DependencyFlags.HardDependency = 1, SoftDependency = 2
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(flagsEnum, dep.Hard ? 1 : 2));
            type.CustomAttributes.Add(attribute);
        }

        asm.Write(path);
    }

    private static void WriteLibrary(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string assemblyName = "Library" + (_counter++);
        AssemblyDefinition asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(assemblyName, new Version(1, 0, 0, 0)), assemblyName, ModuleKind.Dll);
        asm.MainModule.Types.Add(new TypeDefinition("Library", "Helper", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, asm.MainModule.TypeSystem.Object));
        asm.Write(path);
    }

    private static void WriteDummyPatcher(string path, string assemblyName)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        AssemblyDefinition template = AssemblyDefinition.ReadAssembly(Path.Combine(_driverDir, "DummyPatcher.template.dll"));
        template.Name.Name = assemblyName;
        template.MainModule.Name = Path.GetFileName(path);
        template.Write(path);
        template.Dispose();
    }

    // ------------------------------------------------------------------ probes: log, assembly loads

    private sealed class LogEntry
    {
        public LogLevel Level;
        public string Source;
        public string Message;
    }

    private sealed class Capture : ILogListener
    {
        public readonly List<LogEntry> Entries = new List<LogEntry>();

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            Entries.Add(new LogEntry
            {
                Level = eventArgs.Level,
                Source = eventArgs.Source == null ? null : eventArgs.Source.SourceName,
                Message = Convert.ToString(eventArgs.Data),
            });
        }

        public void Dispose()
        {
        }

        public List<LogEntry> From(string source)
        {
            return Entries.Where(e => e.Source == source).ToList();
        }
    }

    private static void InstallProbes()
    {
        AppDomain.CurrentDomain.SetData(CallsKey, Calls);
        AppDomain.CurrentDomain.AssemblyLoad += (sender, e) =>
        {
            string name;
            string kind;
            try
            {
                name = e.LoadedAssembly.GetName().Name;
                kind = string.IsNullOrEmpty(e.LoadedAssembly.Location) ? "bytes" : "disk";
            }
            catch
            {
                return;
            }

            Calls.Add("load:" + name + ":" + kind);
        };
        Logger.Listeners.Add(Log);
    }

    /// <summary>
    /// Everything that violates "no Unity assembly before the preloader has loaded the patched
    /// UnityEngine.CoreModule": a Unity assembly loaded before that module, the module loaded from
    /// disk, or the module loaded twice. Only the preloader phase counts.
    /// </summary>
    private static List<string> UnityViolations()
    {
        var violations = new List<string>();
        int end = Calls.IndexOf("phase:chainloader-start");
        int limit = end < 0 ? Calls.Count : end;
        int patched = Calls.IndexOf("load:UnityEngine.CoreModule:bytes");
        int coreLoads = 0;
        for (int i = 0; i < limit; i++)
        {
            string call = Calls[i];
            if (!call.StartsWith("load:UnityEngine", StringComparison.Ordinal)) continue;

            if (call.StartsWith("load:UnityEngine.CoreModule:", StringComparison.Ordinal)) coreLoads++;
            if (call == "load:UnityEngine.CoreModule:disk") violations.Add("the unpatched UnityEngine.CoreModule was loaded from disk (call #" + i + ")");
            else if (patched < 0 || i < patched) violations.Add(call + " was loaded before the patched UnityEngine.CoreModule (call #" + i + ")");
        }

        if (coreLoads > 1) violations.Add("UnityEngine.CoreModule was loaded " + coreLoads + " times");
        return violations;
    }

    private static int Count(string callPrefix)
    {
        return Calls.Count(c => c.StartsWith(callPrefix, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ BepInEx access (by name)

    /// <summary>
    /// The chainloader's view of BepInEx, reached by reflection: PluginInfo, TypeLoader and the
    /// Chainloader name types that need UnityEngine.CoreModule, so none of this may run before the
    /// "phase:chainloader-start" mark.
    /// </summary>
    private static class Bep
    {
        public static Assembly Asm
        {
            get { return typeof(Paths).Assembly; }
        }

        public static Type PluginInfo
        {
            get { return Asm.GetType("BepInEx.PluginInfo", true); }
        }

        public static MethodInfo FindPluginTypes(Type element)
        {
            Type typeLoader = Asm.GetType("BepInEx.Bootstrap.TypeLoader", true);
            return typeLoader.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "FindPluginTypes" && m.IsGenericMethodDefinition)
                .MakeGenericMethod(element);
        }

        /// <summary>The call Chainloader.Start() makes: same selector, same assembly filter, same cache name.</summary>
        public static object FindPlugins(string directory, string cacheName)
        {
            Type chainloader = Asm.GetType("BepInEx.Bootstrap.Chainloader", true);
            Type element = PluginInfo;
            Delegate selector = Delegate.CreateDelegate(
                typeof(Func<,>).MakeGenericType(typeof(TypeDefinition), element),
                chainloader.GetMethod("ToPluginInfo", BindingFlags.Public | BindingFlags.Static));
            Delegate filter = Delegate.CreateDelegate(
                typeof(Func<AssemblyDefinition, bool>),
                chainloader.GetMethod("HasBepinPlugins", BindingFlags.NonPublic | BindingFlags.Static));
            return FindPluginTypes(element).Invoke(null, new object[] { directory, selector, filter, cacheName });
        }

        public static IDictionary LoadCache(string cacheName)
        {
            Type typeLoader = Asm.GetType("BepInEx.Bootstrap.TypeLoader", true);
            MethodInfo load = typeLoader.GetMethod("LoadAssemblyCache", BindingFlags.Public | BindingFlags.Static).MakeGenericMethod(PluginInfo);
            return (IDictionary)load.Invoke(null, new object[] { cacheName });
        }

        public static object Prop(object o, string name)
        {
            return o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance).GetValue(o, null);
        }
    }

    private sealed class Found
    {
        public string File;
        public string Guid;
        public string Name;
        public string Version;
        public readonly List<string> HardDeps = new List<string>();
        public readonly List<string> SoftDeps = new List<string>();
    }

    private sealed class Scan
    {
        public readonly List<string> Keys = new List<string>();
        public readonly List<Found> Items = new List<Found>();
        public object Raw;
    }

    private static Found ReadInfo(object info, string file)
    {
        object metadata = Bep.Prop(info, "Metadata");
        var found = new Found
        {
            File = file,
            Guid = (string)Bep.Prop(metadata, "GUID"),
            Name = (string)Bep.Prop(metadata, "Name"),
            Version = Convert.ToString(Bep.Prop(metadata, "Version")),
        };
        foreach (object dependency in (IEnumerable)Bep.Prop(info, "Dependencies"))
        {
            int flags = Convert.ToInt32(Bep.Prop(dependency, "Flags"));
            string guid = (string)Bep.Prop(dependency, "DependencyGUID");
            if ((flags & 1) != 0) found.HardDeps.Add(guid);
            else found.SoftDeps.Add(guid);
        }

        return found;
    }

    private static Scan ScanPlugins(string directory, string cacheName)
    {
        object raw = Bep.FindPlugins(directory, cacheName);
        var scan = new Scan { Raw = raw };
        foreach (DictionaryEntry entry in (IDictionary)raw)
        {
            string key = (string)entry.Key;
            scan.Keys.Add(key);
            foreach (object info in (IList)entry.Value) scan.Items.Add(ReadInfo(info, key));
        }

        return scan;
    }

    private static string Relative(string root, string path)
    {
        string full = Path.GetFullPath(path);
        string prefix = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        string relative = full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full.Substring(prefix.Length) : full;
        return relative.Replace('\\', '/');
    }

    // ------------------------------------------------------------------ preloader phase

    private sealed class PatcherLine
    {
        public string Assembly;
        public string TypeName;
    }

    private static List<PatcherLine> DescribePatchers()
    {
        var lines = new List<PatcherLine>();
        foreach (PatcherPlugin p in AssemblyPatcher.PatcherPlugins)
        {
            Delegate d = (Delegate)p.TargetDLLs ?? (Delegate)p.Initializer;
            lines.Add(new PatcherLine
            {
                Assembly = d == null || d.Method.DeclaringType == null ? "?" : d.Method.DeclaringType.Assembly.GetName().Name,
                TypeName = p.TypeName,
            });
        }

        return lines;
    }

    /// <summary>The entry-point patcher Preloader.Run() registers first, built from the same members it uses.</summary>
    private static PatcherPlugin MakeBuiltInPatcher()
    {
        Assembly preloader = typeof(AssemblyPatcher).Assembly;
        Type owner = preloader.GetType("BepInEx.Preloader.Preloader", true);
        Type closure = owner.GetNestedType("<>c", BindingFlags.NonPublic | BindingFlags.Public);
        object singleton = closure.GetField("<>9", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
        MethodInfo targets = closure.GetMethod("<Run>b__7_1", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return new PatcherPlugin
        {
            TargetDLLs = (Func<IEnumerable<string>>)Delegate.CreateDelegate(typeof(Func<IEnumerable<string>>), singleton, targets),
            Patcher = (AssemblyPatcherDelegate)Delegate.CreateDelegate(typeof(AssemblyPatcherDelegate),
                owner.GetMethod("PatchEntrypoint", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)),
            TypeName = "BepInEx.Chainloader",
        };
    }

    private static List<PatcherLine> _orderBeforePatching = new List<PatcherLine>();

    // Patchers a scenario registers by hand: before the patchers of the directory (they then sort before the gate, which
    // is one of them) or after them.
    private static Action _beforeDirectory;
    private static Action _afterDirectory;

    private static void RunPreloaderPhase(bool builtIn)
    {
        Mark("phase:preloader-start");
        if (builtIn) AssemblyPatcher.AddPatcher(MakeBuiltInPatcher());
        if (_beforeDirectory != null) _beforeDirectory();
        AssemblyPatcher.AddPatchersFromDirectory(Paths.PatcherPluginPath);
        if (_afterDirectory != null) _afterDirectory();
        _orderBeforePatching = DescribePatchers();
        Console.WriteLine("  patcher order: " + string.Join(" > ", _orderBeforePatching.Select(l => l.Assembly).ToArray()));
        AssemblyPatcher.PatchAndLoad(Paths.DllSearchPaths);
        AssemblyPatcher.DisposePatchers();
        Mark("phase:preloader-end");
    }

    // ------------------------------------------------------------------ the generated worlds

    private static void BuildWorld(Fake g, WorldKind kind)
    {
        foreach (PluginSpec spec in WorldPlugins()) WritePlugin(Path.Combine(g.Plugins, spec.Path), spec);
        WriteLibrary(Path.Combine(g.Plugins, "lib/plain.dll"));
        Directory.CreateDirectory(Path.Combine(g.Plugins, "junk"));
        File.WriteAllBytes(Path.Combine(g.Plugins, "junk/native.dll"), new byte[] { 0x4D, 0x5A, 0x00, 0x01, 0x02, 0x03 });

        Directory.CreateDirectory(Path.GetDirectoryName(g.PolicyFile));
        switch (kind)
        {
            case WorldKind.BadDefault:
                File.WriteAllText(g.PolicyFile, "{ \"schema\": 1, \"mods\": [ this is not json");
                break;
            case WorldKind.MissingDefault:
                break;
            case WorldKind.Inert:
                // Unreadable on purpose: a gate that reads it in a normal start would show.
                Directory.CreateDirectory(g.PolicyFile);
                break;
            case WorldKind.DefaultDeny:
                File.WriteAllText(g.PolicyFile, ShippedPolicyWithExtraDenies());
                break;
            default:
                File.Copy(_policySource, g.PolicyFile, true);
                break;
        }

        if (kind == WorldKind.UserFile)
        {
            File.WriteAllText(g.UserFile,
                "{ \"schema\": 1,\n"
                + "  \"allow\": [ { \"guid\": \"com.example.foreign\", \"name\": \"Foreign Mod\", \"kind\": \"mod\", \"clientVersion\": \"1.0.1\" },\n"
                + "              { \"guid\": \"com.chazut.orbit\", \"name\": \"ORBIT\", \"kind\": \"mod\" },\n"
                + "              { \"patcher\": \"ZZZ-nested-denied.dll\", \"name\": \"Nested\", \"kind\": \"tool\" } ],\n"
                + "  \"deny\": [ \"me.sol.sain\", " + Quoted(UserFileDeniedPatchers) + ", \"com.SPT.core\" ] }");
        }
        else if (kind == WorldKind.UserBlacklist)
        {
            File.WriteAllText(g.UserFile,
                "{ \"schema\": 1,\n"
                + "  \"unlisted\": \"allow\",\n"
                + "  \"deny\": [ \"me.sol.sain\", \"com.example.foreign\", \"x.deeper\", " + Quoted(UserBlacklistDeniedPatchers) + ", \"com.SPT.core\" ] }");
        }
        else if (kind == WorldKind.UserGarbage)
        {
            File.WriteAllText(g.UserFile, "{ \"schema\": 1, \"allow\": [ nope");
        }
        else if (kind == WorldKind.UserOversize)
        {
            // A valid user file that would deny SAIN, one byte above the limit of 256 KB: the gate must not apply any of it.
            const string deny = "{ \"schema\": 1, \"deny\": [ \"me.sol.sain\" ] }";
            File.WriteAllText(g.UserFile, deny + new string(' ', 256 * 1024 + 1 - deny.Length));
        }

        foreach (PatcherSpec patcher in WorldPatchers()) WriteDummyPatcher(Path.Combine(g.Patchers, patcher.RelativePath), patcher.AssemblyName);

        Directory.CreateDirectory(Path.GetDirectoryName(g.GateFile));
        File.Copy(Path.Combine(Path.Combine(_driverDir, "gate"), ArenaSessionContract.GateFileName), g.GateFile, true);
    }

    private static string Quoted(string[] names)
    {
        return string.Join(", ", names.Select(n => "\"" + n + "\"").ToArray());
    }

    /// <summary>
    /// The shipped default file with a deny list that also names com.example.foreign and the default-denied
    /// patchers: written by editing the file's own text, so that its real deny list stays in force.
    /// </summary>
    private static string ShippedPolicyWithExtraDenies()
    {
        string text = File.ReadAllText(_policySource);
        const string anchor = "\"deny\": [";
        int first = text.IndexOf(anchor, StringComparison.Ordinal);
        if (first < 0 || text.IndexOf(anchor, first + anchor.Length, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException("the shipped default file must carry exactly one \"deny\": [ list");
        }

        return text.Insert(first + anchor.Length, " \"com.example.foreign\", " + Quoted(DefaultDeniedPatchers) + ",");
    }

    private static ArenaGateReport ReadReport(out string rawJson)
    {
        rawJson = AppDomain.CurrentDomain.GetData(ArenaSessionContract.GateReportDataKey) as string;
        if (rawJson == null) return null;

        ArenaGateReport report;
        string error;
        if (!ArenaGateReport.TryParse(rawJson, out report, out error)) throw new InvalidOperationException("the published report does not parse: " + error);
        return report;
    }

    private static string Describe(ArenaSessionUnit u)
    {
        return u.Guid + " [" + u.Reason + "]";
    }

    private static ArenaWhitelistPolicy LoadPolicyLikeTheGate(Fake g)
    {
        // Written by hand, not through ArenaPolicyFiles: a user file above 256 KB is not a policy file and is ignored as a whole.
        string user = File.Exists(g.UserFile) && new FileInfo(g.UserFile).Length <= 256 * 1024 ? File.ReadAllText(g.UserFile) : null;
        ArenaWhitelistPolicy policy;
        List<string> diagnostics;
        if (!ArenaWhitelistPolicy.TryLoad(File.ReadAllText(g.PolicyFile), user, out policy, out diagnostics))
        {
            throw new InvalidOperationException("the checks' own policy load failed: " + string.Join("; ", diagnostics.ToArray()));
        }

        return policy;
    }

    private static void ScenarioWorld(string name, WorldOptions options)
    {
        WorldKind kind = options.Kind;
        _omitEarlyPatcher = options.OmitEarlyPatcher;
        bool coreModule = kind != WorldKind.NoCoreModule;
        bool builtIn = kind != WorldKind.NoCoreModule;
        Fake g = Fake.Create(name, coreModule);
        BuildWorld(g, kind);
        InstallProbes();

        bool inert = kind == WorldKind.Inert;
        Check(_flag == !inert, "the command line carries -arena-session exactly when the scenario needs it");

        int patchedBefore = Harmony.GetAllPatchedMethods().Count();
        List<PluginSpec> plugins = WorldPlugins();

        RunPreloaderPhase(builtIn);

        // ---- preloader phase ----
        string rawJson;
        ArenaGateReport afterPreloader = ReadReport(out rawJson);
        List<PatcherSpec> patchers = WorldPatchers();
        List<string> sortedAssemblies = _orderBeforePatching.Select(l => l.Assembly).ToList();
        int gateIndex = sortedAssemblies.IndexOf(GateAssemblyName);
        Check(gateIndex >= 0, "the gate was found as a patcher", "patcher order: " + string.Join(",", sortedAssemblies.ToArray()));
        _expectedRanBeforeGate = ExpectedRanBeforeGate(kind, patchers, sortedAssemblies, gateIndex);
        if (!inert) CheckRanBeforeGateJson("after the preloader phase", rawJson, _expectedRanBeforeGate);

        CheckBuiltIn(builtIn);
        List<string> violations = UnityViolations();
        if (inert || kind != WorldKind.NoCoreModule)
        {
            Check(violations.Count == 0, "no Unity assembly was loaded before the patched UnityEngine.CoreModule",
                string.Join("; ", violations.ToArray()));
        }

        if (inert)
        {
            CheckInertPreloader(patchedBefore, afterPreloader, patchers);
        }
        else if (kind == WorldKind.BadDefault || kind == WorldKind.MissingDefault)
        {
            CheckBadPolicyPreloader(kind, patchedBefore, afterPreloader, patchers);
        }
        else if (kind == WorldKind.NoCoreModule)
        {
            CheckNoCoreModulePreloader(patchedBefore, afterPreloader, patchers, violations);
        }
        else
        {
            CheckActivePreloader(g, kind, patchedBefore, afterPreloader, patchers, sortedAssemblies, gateIndex);
        }

        // ---- chainloader phase ----
        Mark("phase:chainloader-start");
        Scan cold = ScanPlugins(g.Plugins, "chainloader");
        string coldJson;
        ArenaGateReport afterScan = ReadReport(out coldJson);
        int totalPlugins = plugins.Count;

        if (inert || kind == WorldKind.BadDefault || kind == WorldKind.MissingDefault || kind == WorldKind.NoCoreModule)
        {
            CheckUnfilteredScan(g, kind, cold, totalPlugins, afterScan);
        }
        else if (kind == WorldKind.PostfixFailure)
        {
            CheckPostfixFailure(g, cold, afterScan, totalPlugins);
        }
        else
        {
            CheckFilteredScan(g, kind, cold, coldJson, afterScan, plugins);
        }

        // For the human reading the evidence log: what the gate said.
        foreach (LogEntry entry in Log.From(GateSource)) Console.WriteLine("  log: [" + entry.Level + "] " + entry.Message);
    }

    // ------------------------------------------------------------------ checks: preloader phase

    private static void CheckBuiltIn(bool builtIn)
    {
        if (!builtIn) return;
        bool patched = Log.Entries.Any(e => e.Message != null && e.Message.Contains("Patching [UnityEngine.CoreModule] with [BepInEx.Chainloader]"));
        Check(patched, "BepInEx's own entry-point patcher still patched UnityEngine.CoreModule",
            "no 'Patching [UnityEngine.CoreModule] with [BepInEx.Chainloader]' line");
        Check(Calls.Contains("load:UnityEngine.CoreModule:bytes"), "the patched UnityEngine.CoreModule was loaded from memory");
    }

    private static void CheckInertPreloader(int patchedBefore, ArenaGateReport report, List<PatcherSpec> patchers)
    {
        Check(report == null, "a start without -arena-session publishes no gate report");
        Check(Harmony.GetAllPatchedMethods().Count() == patchedBefore, "a start without -arena-session adds no Harmony patch");
        Check(!Logger.Sources.Any(s => s.SourceName == GateSource), "a start without -arena-session creates no log source");
        Check(Log.From(GateSource).Count == 0, "a start without -arena-session logs nothing");
        foreach (PatcherSpec p in patchers)
        {
            bool all = Calls.Contains("init:" + p.AssemblyName) && Calls.Contains("targets:" + p.AssemblyName)
                && Calls.Contains("patch:" + p.AssemblyName) && Calls.Contains("finish:" + p.AssemblyName);
            Check(all, "a start without -arena-session runs patcher " + p.AssemblyName + " untouched");
        }
    }

    private static void CheckBadPolicyPreloader(WorldKind kind, int patchedBefore, ArenaGateReport report, List<PatcherSpec> patchers)
    {
        Check(report != null && !report.Active, "an unusable default policy publishes an inactive report",
            report == null ? "no report" : "active=" + report.Active);
        if (report != null)
        {
            string expected = kind == WorldKind.BadDefault ? "default whitelist" : "cannot read the default whitelist";
            Check(report.Error != null && report.Error.Contains(expected), "the report names the policy as the reason",
                "error: " + report.Error);
            Check(report.PatchersNeutralised.Count == 0, "no patcher is neutralised when the policy failed");
        }

        Check(Harmony.GetAllPatchedMethods().Count() == patchedBefore, "no Harmony patch is installed when the policy failed");
        Check(Log.From(GateSource).Any(e => e.Level == LogLevel.Error), "the failure is logged at Error level");
        foreach (PatcherSpec p in patchers)
        {
            bool all = Calls.Contains("init:" + p.AssemblyName) && Calls.Contains("targets:" + p.AssemblyName)
                && Calls.Contains("patch:" + p.AssemblyName) && Calls.Contains("finish:" + p.AssemblyName);
            Check(all, "patcher " + p.AssemblyName + " runs untouched when the policy failed");
        }
    }

    private static void CheckNoCoreModulePreloader(int patchedBefore, ArenaGateReport report, List<PatcherSpec> patchers, List<string> violations)
    {
        Check(report != null && !report.Active, "without a loaded UnityEngine.CoreModule the report is inactive",
            report == null ? "no report" : "active=" + report.Active);
        if (report != null)
        {
            Check(report.Error != null && report.Error.Contains("UnityEngine.CoreModule is not loaded"),
                "the report names the missing UnityEngine.CoreModule as the reason", "error: " + report.Error);
        }

        Check(Harmony.GetAllPatchedMethods().Count() == patchedBefore, "the filter is not installed when UnityEngine.CoreModule is missing");
        Check(!Calls.Any(c => c.StartsWith("load:UnityEngine", StringComparison.Ordinal)),
            "the gate loads no Unity assembly itself when UnityEngine.CoreModule is missing",
            string.Join("; ", Calls.Where(c => c.StartsWith("load:UnityEngine", StringComparison.Ordinal)).ToArray()));
        Check(violations.Count == 0, "no Unity violation recorded", string.Join("; ", violations.ToArray()));
    }

    private static void CheckActivePreloader(Fake g, WorldKind kind, int patchedBefore, ArenaGateReport report, List<PatcherSpec> patchers,
        List<string> sortedAssemblies, int gateIndex)
    {
        // The report between Initialize and the first scan is the provisional one.
        Check(report != null && !report.Active, "before the plugin list is filtered the published report is inactive",
            report == null ? "no report" : "active=" + report.Active);
        if (report != null)
        {
            Check(report.Error != null && report.Error.Contains("not been filtered yet"), "the provisional report says so", "error: " + report.Error);
            ArenaWhitelistPolicy policy = LoadPolicyLikeTheGate(g);
            Check(report.PolicyHash == policy.Hash, "the report carries the hash of the effective policy",
                report.PolicyHash + " vs " + policy.Hash);
        }

        // Harmony footprint: exactly one postfix, ours, on FindPluginTypes<PluginInfo>.
        List<MethodBase> patched = Harmony.GetAllPatchedMethods().ToList();
        Check(patched.Count == patchedBefore + 1, "exactly one method is patched", "patched methods: " + patched.Count + " (before " + patchedBefore + ")");
        MethodBase target = patched.FirstOrDefault(m => m.Name == "FindPluginTypes");
        Check(target != null && target.IsGenericMethod && target.GetGenericArguments()[0].FullName == "BepInEx.PluginInfo",
            "the patched method is FindPluginTypes<PluginInfo>");
        if (target != null)
        {
            Patches info = Harmony.GetPatchInfo(target);
            Check(info.Postfixes.Count == 1 && info.Postfixes[0].owner == GateHarmonyId, "the patch is one postfix owned by " + GateHarmonyId);
            Check(info.Prefixes.Count == 0 && info.Transpilers.Count == 0 && info.Finalizers.Count == 0, "the patch has no prefix, transpiler or finalizer");
        }

        // Which patchers are denied for this policy, and where do they sort relative to the gate?
        var deniedFiles = new List<string>();
        var deniedBeforeGate = new List<string>();
        foreach (PatcherSpec p in patchers)
        {
            string leaf = Path.GetFileName(p.RelativePath);
            string cause;
            bool allowed = PatcherAllowedInWorld(kind, p, out cause);

            int index = sortedAssemblies.IndexOf(p.AssemblyName);
            bool before = index >= 0 && index < gateIndex;
            bool init = Calls.Contains("init:" + p.AssemblyName);
            bool targets = Calls.Contains("targets:" + p.AssemblyName);
            bool patch = Calls.Contains("patch:" + p.AssemblyName);
            bool finish = Calls.Contains("finish:" + p.AssemblyName);
            if (allowed)
            {
                Check(init && targets && patch && finish, "allowed patcher " + p.AssemblyName + " ran all four callbacks",
                    "init=" + init + " targets=" + targets + " patch=" + patch + " finish=" + finish);
                continue;
            }

            deniedFiles.Add(leaf);
            if (before)
            {
                deniedBeforeGate.Add(p.AssemblyName);
                Check(init && !targets && !patch && !finish, "denied patcher " + p.AssemblyName + " (sorts before the gate) only ran its initializer",
                    "init=" + init + " targets=" + targets + " patch=" + patch + " finish=" + finish);
                bool warned = report != null && report.Warnings.Any(w => w.Contains(leaf) && w.Contains("initializer ran before the gate"));
                Check(warned, "the report warns that the initializer of " + p.AssemblyName + " could not be stopped");
                bool named = report != null && report.Warnings.Any(w => w.Contains(leaf) && w.Contains(" " + cause + ", but its initializer ran before the gate"));
                Check(named, "the warning for " + p.AssemblyName + " names the list that keeps it off: it " + cause,
                    "warnings: " + (report == null ? "no report" : string.Join(" | ", report.Warnings.Where(w => w.Contains(leaf)).ToArray())));
            }
            else
            {
                Check(!init && !targets && !patch && !finish, "denied patcher " + p.AssemblyName + " (sorts after the gate) ran nothing",
                    "init=" + init + " targets=" + targets + " patch=" + patch + " finish=" + finish);
                bool warned = report != null && report.Warnings.Any(w => w.Contains(leaf) && w.Contains("initializer ran before the gate"));
                Check(!warned, "no initializer warning for " + p.AssemblyName);
            }
        }

        if (report != null)
        {
            SameSet("the report lists exactly the denied patchers as neutralised", deniedFiles, report.PatchersNeutralised);

            // Debug lines never reach the log under BepInEx's default configuration (the preloader keeps
            // Fatal to Info); this one Info line is what names the patchers that were switched off.
            CheckPatchersNamedAtInfo(report.PatchersNeutralised,
                Log.From(GateSource).Where(e => e.Level == LogLevel.Info && e.Message.Contains(" preloader patcher")).ToList());
        }

        Check(Count("init:") >= 1, "dummy patchers were initialised at all");
        Console.WriteLine("  info: gate position " + gateIndex + " of " + sortedAssemblies.Count + "; denied before the gate: "
            + string.Join(",", deniedBeforeGate.ToArray()));
    }

    private static void CheckPatchersNamedAtInfo(List<string> neutralised, List<LogEntry> patcherInfo)
    {
        if (neutralised.Count == 0)
        {
            Check(patcherInfo.Count == 0, "no patcher was switched off, so no Info line says so");
            return;
        }

        string count = neutralised.Count + " preloader patcher" + (neutralised.Count == 1 ? string.Empty : "s") + " switched off: ";
        Check(patcherInfo.Count == 1 && patcherInfo[0].Message.StartsWith("Arena session: " + count, StringComparison.Ordinal)
            && neutralised.All(file => patcherInfo[0].Message.Contains(file)),
            "one Info line names the " + neutralised.Count + " patchers that were switched off",
            "info lines: " + string.Join(" | ", patcherInfo.Select(e => e.Message).ToArray()));
    }

    // ------------------------------------------------------------------ checks: chainloader phase

    private static void CheckUnfilteredScan(Fake g, WorldKind kind, Scan scan, int totalPlugins, ArenaGateReport report)
    {
        Check(scan.Items.Count == totalPlugins, "every plugin is found", "found " + scan.Items.Count + " of " + totalPlugins);
        Check(scan.Keys.Count >= totalPlugins + 1, "the dictionary has an entry for every DLL, library included", "keys: " + scan.Keys.Count);
        if (kind == WorldKind.Inert) Check(report == null, "the scan of a normal start publishes no report");
        else Check(report != null && !report.Active, "the report stays inactive after the scan", report == null ? "no report" : "active=" + report.Active);
        Check(!Log.From(GateSource).Any(e => e.Level == LogLevel.Info && e.Message.Contains(" plugins allowed, ")), "an inactive gate writes no Info summary");
    }

    private static void CheckFilteredScan(Fake g, WorldKind kind, Scan cold, string coldJson, ArenaGateReport report, List<PluginSpec> plugins)
    {
        Dictionary<string, string> expected = Expected(kind);
        int total = plugins.Count;
        string failureDetail = report == null ? "no report" : "active=" + report.Active + " error=" + report.Error;
        Check(report != null && report.Active && report.Error == null, "the report is active after the scan", failureDetail);
        if (report == null || !report.Active) return;

        // Report content, by relative path
        var actual = new Dictionary<string, string>();
        foreach (ArenaSessionUnit u in report.Allowed) actual[Relative(g.Plugins, u.File)] = "allowed:" + u.Reason;
        foreach (ArenaSessionUnit u in report.Excluded) actual[Relative(g.Plugins, u.File)] = "excluded:" + u.Reason;
        SameSet("the report decides every plugin as expected",
            expected.Select(kv => kv.Key + "=" + kv.Value), actual.Select(kv => kv.Key + "=" + kv.Value));

        // What the chainloader would see
        var remaining = cold.Items.Select(i => Relative(g.Plugins, i.File)).ToList();
        SameSet("only the allowed plugins are left in the plugin list",
            expected.Where(kv => kv.Value.StartsWith("allowed:", StringComparison.Ordinal)).Select(kv => kv.Key), remaining);
        Check(cold.Keys.Count >= total + 1, "the dictionary keeps an entry for every DLL, excluded ones with an empty list",
            "keys: " + cold.Keys.Count);

        // Dependencies were read the way BepInEx defines them
        Found sain = cold.Items.FirstOrDefault(i => i.Guid == "me.sol.sain" && i.Version == "4.5.2");
        if (kind != WorldKind.UserFile && kind != WorldKind.UserBlacklist)
        {
            Check(sain != null && sain.HardDeps.Contains("xyz.drakia.bigbrain") && sain.HardDeps.Contains("x.helperlib") && sain.SoftDeps.Contains("x.softonly"),
                "the generated plugins carry the intended hard and soft dependencies");
        }

        // Warnings
        if (kind == WorldKind.Full || kind == WorldKind.UserGarbage || kind == WorldKind.UserOversize || kind == WorldKind.DefaultDeny)
        {
            Check(report.Warnings.Any(w => w.StartsWith("SAIN: version 4.5.2 differs from reviewed 4.5.1", StringComparison.Ordinal)),
                "a version that differs from the reviewed one is a warning");
            Check(report.Warnings.Any(w => w.StartsWith("SAIN: version 4.0.0 differs from reviewed 4.5.1", StringComparison.Ordinal)),
                "the second copy of the plugin gets its own warning");
            Check(report.Warnings.Any(w => w == "Helper Library: pulled in as hard dependency of SAIN"), "a hard dependency pulled in is a warning");
            Check(report.Warnings.Any(w => w == "Deeper Library: pulled in as hard dependency of Helper Library"), "a transitive hard dependency is pulled in too");
            Check(!report.Warnings.Any(w => w.Contains("Soft Only")), "a soft dependency is not pulled in");
        }

        if (kind == WorldKind.UserGarbage)
        {
            Check(report.Warnings.Any(w => w.StartsWith("user whitelist: cannot parse", StringComparison.Ordinal)),
                "a user file that does not parse is ignored with a warning");
            Check(Log.From(GateSource).Any(e => e.Level == LogLevel.Warning && e.Message.Contains("user whitelist: cannot parse")),
                "the ignored user file is also logged as a warning");
        }

        if (kind == WorldKind.UserOversize)
        {
            // The client reads the same file through the same reader and ignores it too, so both end up with the policy of
            // the default list alone (and the same hash): the plugin SAIN, which the oversized file denies, is allowed.
            Check(report.Warnings.Any(w => w.StartsWith("user whitelist: cannot read", StringComparison.Ordinal) && w.Contains("the file is ignored: larger than 256 KB")),
                "a user file above 256 KB is ignored with a warning", "warnings: " + string.Join(" | ", report.Warnings.ToArray()));
            Check(Log.From(GateSource).Any(e => e.Level == LogLevel.Warning && e.Message.Contains("larger than 256 KB")),
                "the ignored user file is also logged as a warning");
            Check(report.Allowed.Any(u => u.Guid == "me.sol.sain") && report.Excluded.All(u => u.Reason != "denied-by-user"),
                "nothing of the oversized file's deny list was applied");
            Check(!report.UnlistedAllowed, "the oversized file did not turn the whitelist into a blacklist");
        }

        if (kind == WorldKind.Full)
        {
            Check(report.Excluded.Single(u => u.Guid == "com.chazut.orbit").Reason == "denied",
                "the shipped default's deny list excludes ORBIT with the reason 'denied'");
            Check(report.Excluded.Single(u => u.Guid == "com.example.foreign").Reason == "not-on-whitelist",
                "a plugin nothing names is 'not-on-whitelist', not 'denied'");
            Check(!report.Warnings.Any(w => w.Contains("both allowed and denied")), "the shipped default has no allow-and-deny contradiction");
        }

        if (kind == WorldKind.DefaultDeny)
        {
            Check(report.Excluded.Single(u => u.Guid == "com.example.foreign").Reason == "denied", "a default deny on top of the shipped ones excludes with 'denied'");
            Check(report.Excluded.Single(u => u.Guid == "com.chazut.orbit").Reason == "denied", "the shipped deny still holds beside it");
            Check(report.Warnings.Any(w => w.StartsWith("default whitelist:", StringComparison.Ordinal) && w.Contains("WTT-ContentBackportPatcher.dll")
                    && w.Contains("both allowed and denied")),
                "a patcher the default list allows and denies is reported, and the deny wins");
            Check(Log.From(GateSource).Any(e => e.Level == LogLevel.Warning && e.Message.Contains("both allowed and denied")),
                "the contradiction is also logged as a warning");
        }

        if (kind == WorldKind.UserBlacklist)
        {
            const string requiresDenied = "Helper Library: excluded because it requires Deeper Library (denied by the user's policy file)";
            Check(report.Warnings.Contains(requiresDenied),
                "a plugin that needs a denied hard dependency is excluded, and the warning names the dependency and the list that denied it");
            Check(!report.Warnings.Any(w => w.Contains("pulled in as hard dependency")), "a denied hard dependency is not pulled in");
            Check(!report.Warnings.Any(w => w.StartsWith("Soft Only:", StringComparison.Ordinal)),
                "a plugin that nothing names gets no warning of its own");
            Check(report.Warnings.Any(w => w.Contains("deny of 'com.SPT.core' is ignored")), "a deny of a platform unit is reported and ignored");
            Check(report.Excluded.All(u => u.Guid != "com.SPT.core"), "a platform plugin cannot be denied");
            Check(report.Excluded.All(u => u.Reason == "denied-by-user" || u.Reason == "denied" || u.Reason == "requires-denied:x.deeper"),
                "in blacklist mode only denied plugins and the ones that need a denied plugin are excluded");
            Check(report.Excluded.Single(u => u.Guid == "com.chazut.orbit").Reason == "denied", "a default deny holds in blacklist mode, with its own reason");
            Check(report.Excluded.Single(u => u.Guid == "com.example.foreign").Reason == "denied-by-user", "the user's deny names itself");
            Check(report.Allowed.Any(u => u.Reason == "unlisted-allowed"), "the report carries the unlisted-allowed reason");

            // The override is visible: ONE warning names the plugins that only got in because nothing names them
            // (written by hand: only Soft Only; Helper Library would have been unlisted but needs the denied Deeper Library).
            List<string> overrides = report.Warnings.Where(w => w.StartsWith("policy override: ", StringComparison.Ordinal)).ToList();
            Check(overrides.Count == 1 && overrides[0] == "policy override: 1 unlisted mod(s) allowed by your policy file: Soft Only",
                "one warning says which unlisted plugins the user's policy file let through",
                "policy override warnings: " + string.Join(" | ", overrides.ToArray()));
            Check(overrides.Count == 1 && report.Warnings.IndexOf(overrides[0]) > report.Warnings.IndexOf(requiresDenied),
                "the policy-override warning comes after the per-plugin warnings of the decision");
        }

        // The patchers whose initializer ran before the gate are in the final report as well, and the summary line counts them.
        CheckRanBeforeGateJson("after the plugin scan", coldJson, _expectedRanBeforeGate);

        // The mode itself is in the report and in its JSON: true exactly when the user file made the policy a blacklist.
        bool blacklistMode = kind == WorldKind.UserBlacklist;
        Check(report.UnlistedAllowed == blacklistMode, "the report's unlistedAllowed flag is " + (blacklistMode ? "true" : "false"),
            "flag: " + report.UnlistedAllowed);
        Check(coldJson.Contains("\"unlistedAllowed\":" + (blacklistMode ? "true" : "false")), "the published JSON carries unlistedAllowed:" + (blacklistMode ? "true" : "false"));
        Check(report.Warnings.Count(w => w.StartsWith("policy override: ", StringComparison.Ordinal)) == (blacklistMode ? 1 : 0),
            blacklistMode ? "the blacklist has its policy-override warning" : "a whitelist has no policy-override warning");

        if (kind == WorldKind.UserFile)
        {
            Check(report.Warnings.Any(w => w.StartsWith("Foreign Mod: version 1.0.0 differs from reviewed 1.0.1", StringComparison.Ordinal)),
                "the user's reviewed version is checked like the shipped ones");
            Check(report.Allowed.Single(u => u.Guid == "com.chazut.orbit").Reason == "whitelist", "a user allow lifts a default deny");
            Check(report.Warnings.Any(w => w.Contains("deny of 'com.SPT.core' is ignored")), "a deny of a platform unit is reported and ignored");
            Check(report.Excluded.All(u => u.Guid != "com.SPT.core"), "a platform plugin cannot be denied");
        }

        // Log lines
        List<LogEntry> gateLog = Log.From(GateSource);
        List<LogEntry> info = gateLog.Where(e => e.Level == LogLevel.Info).ToList();
        // Written by hand (not from ArenaGateReport.SummaryLine): the blacklist's line ends with the override suffix, no other does.
        string summary = "Arena session: " + report.Allowed.Count + " plugins allowed, " + report.Excluded.Count + " excluded, " + report.Warnings.Count + " warnings"
            + (blacklistMode ? " (policy override: unlisted mods allowed)" : string.Empty)
            + (_expectedRanBeforeGate.Count > 0 ? " (" + _expectedRanBeforeGate.Count + " patcher(s) ran before the gate)" : string.Empty);
        List<LogEntry> patcherInfo = info.Where(e => e.Message.Contains(" preloader patcher")).ToList();
        Check(info.Count(e => e.Message == summary) == 1 && info.Count == 1 + patcherInfo.Count, "exactly one Info line carries the summary, the only other Info line names the neutralised patchers",
            "info lines: " + string.Join(" | ", info.Select(e => e.Message).ToArray()) + " / expected: " + summary);
        CheckPatchersNamedAtInfo(report.PatchersNeutralised, patcherInfo);
        List<LogEntry> debugExcluded = gateLog.Where(e => e.Level == LogLevel.Debug && e.Message.StartsWith("Excluded ", StringComparison.Ordinal)).ToList();
        Check(debugExcluded.Count == report.Excluded.Count
            && report.Excluded.All(u => debugExcluded.Any(e => e.Message.Contains("(" + u.Guid + "): " + u.Reason + ","))),
            "one Debug line per excluded plugin, with the reason that excluded it",
            "debug lines: " + string.Join(" | ", debugExcluded.Select(e => e.Message).ToArray()) + " for " + report.Excluded.Count + " excluded");

        // The policy hash is the hash of the policy this driver loads on its own.
        Check(report.PolicyHash == LoadPolicyLikeTheGate(g).Hash, "the final report carries the hash of the effective policy");

        // The type cache is complete: filtering happens after it is written.
        IDictionary cache = Bep.LoadCache("chainloader");
        var cached = new List<string>();
        if (cache != null)
        {
            foreach (DictionaryEntry entry in cache)
            {
                foreach (object item in (IList)Bep.Prop(entry.Value, "CacheItems")) cached.Add(Relative(g.Plugins, (string)entry.Key) + "=" + ((string)Bep.Prop(Bep.Prop(item, "Metadata"), "GUID")));
            }
        }

        SameSet("the plugin type cache still lists every plugin, excluded ones included",
            plugins.Select(p => p.Path + "=" + p.Guid), cached);

        // A second, cache-warm scan filters identically and does not repeat the Info line.
        Scan warm = ScanPlugins(g.Plugins, "chainloader");
        SameSet("a scan that is served from the cache is filtered the same way",
            remaining, warm.Items.Select(i => Relative(g.Plugins, i.File)));
        string warmJson;
        ReadReport(out warmJson);
        Check(warmJson == coldJson, "the report of the second scan is identical");
        Check(Log.From(GateSource).Count(e => e.Level == LogLevel.Info && e.Message == summary) == 1, "the Info summary is written once");

        // Other scans are left alone: another directory, and the other instantiation of the generic method.
        string other = Path.Combine(g.Root, "other-plugins");
        WritePlugin(Path.Combine(other, "stranger.dll"), P("stranger.dll", "x.stranger", "Stranger", "1.0.0"));
        Scan otherScan = ScanPlugins(other, "other");
        Check(otherScan.Items.Count == 1 && otherScan.Items[0].Guid == "x.stranger", "a scan of another directory is not filtered");
        string otherJson;
        ReadReport(out otherJson);
        Check(otherJson == coldJson, "a scan of another directory does not touch the report");
        CheckForeignInstantiation(g, coldJson);
    }

    /// <summary>
    /// Known limitation, measured: Harmony on Mono detours the SHARED code of every reference-type
    /// instantiation of FindPluginTypes&lt;T&gt;, so a later call for another T runs the PluginInfo body
    /// and returns a Dictionary of PluginInfo lists. The only other caller in BepInEx is the patcher
    /// scan, which is over before the gate installs the filter. The gate has to ignore such a call
    /// (its directory is not the plugin directory) and must not be thrown by it.
    /// </summary>
    private static void CheckForeignInstantiation(Fake g, string reportBefore)
    {
        MethodInfo selector = typeof(AssemblyPatcher).GetMethod("ToPatcherPlugin", BindingFlags.NonPublic | BindingFlags.Static);
        Type typeLoader = Bep.Asm.GetType("BepInEx.Bootstrap.TypeLoader", true);
        MethodInfo find = typeLoader.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "FindPluginTypes" && m.IsGenericMethodDefinition).MakeGenericMethod(typeof(PatcherPlugin));
        Delegate func = Delegate.CreateDelegate(typeof(Func<TypeDefinition, PatcherPlugin>), selector);

        object result = null;
        Exception thrown = null;
        try
        {
            result = find.Invoke(null, new object[] { g.Patchers, func, null, null });
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Check(thrown == null, "a scan of another instantiation of the patched method does not make the postfix throw",
            thrown == null ? null : thrown.ToString());
        Console.WriteLine("  info: FindPluginTypes<PatcherPlugin> after the patch returned "
            + (result == null ? "null" : result.GetType().ToString()) + " (the PluginInfo body serves every reference-type instantiation)");
        string after;
        ReadReport(out after);
        Check(after == reportBefore, "a scan of another instantiation does not touch the report");
    }

    private static void CheckPostfixFailure(Fake g, Scan scan, ArenaGateReport reportAfterScan, int totalPlugins)
    {
        // Phase 1: the first real scan is filtered normally.
        Check(reportAfterScan != null && reportAfterScan.Active, "before the failure the gate is active", reportAfterScan == null ? "no report" : reportAfterScan.Error);

        // Phase 2: the postfix gets a malformed result (a PluginInfo without metadata).
        Assembly gate = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == GateAssemblyName);
        MethodInfo postfix = gate.GetType("ArenaMode.Gate.ArenaGatePluginFilter", true).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);
        Type infoType = Bep.PluginInfo;
        Type listType = typeof(List<>).MakeGenericType(infoType);
        Type dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), listType);
        IDictionary malformed = (IDictionary)Activator.CreateInstance(dictionaryType);
        IList list = (IList)Activator.CreateInstance(listType);
        list.Add(Activator.CreateInstance(infoType));
        malformed[Path.Combine(g.Plugins, "broken.dll")] = list;

        Exception thrown = null;
        try
        {
            postfix.Invoke(null, new object[] { g.Plugins, malformed });
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Check(thrown == null, "the postfix never throws, whatever the plugin list looks like", thrown == null ? null : thrown.ToString());
        Check(list.Count == 1, "a list the gate could not handle is left exactly as it was");
        string json;
        ArenaGateReport report = ReadReport(out json);
        Check(report != null && !report.Active && report.Error != null && report.Error.Contains("could not be filtered"),
            "the report turns inactive and says the plugin list could not be filtered", report == null ? "no report" : "active=" + report.Active + " error=" + report.Error);
        Check(Log.From(GateSource).Any(e => e.Level == LogLevel.Error && e.Message.Contains("every plugin loads")), "the failure is logged at Error level");

        // The patchers were switched off in Initialize and the preloader has consumed their lists: a failure
        // now does not bring them back, so the Error line and the inactive report both say so.
        List<string> stayOff = reportAfterScan == null ? new List<string>() : reportAfterScan.PatchersNeutralised;
        Check(stayOff.Count > 0, "the postfix-failure world has neutralised patchers to lose");
        Check(report != null && stayOff.SequenceEqual(report.PatchersNeutralised, StringComparer.Ordinal), "the inactive report still lists the neutralised patchers");
        Check(Log.From(GateSource).Any(e => e.Level == LogLevel.Error && e.Message.Contains("already switched off stay off") && stayOff.All(file => e.Message.Contains(file))),
            "the Error line says that the neutralised patchers stay off");

        // Their initializers ran before the gate and that stays true: the inactive report keeps saying so.
        Check(_expectedRanBeforeGate.Count > 0, "the postfix-failure world has a denied patcher that sorts before the gate");
        CheckRanBeforeGateJson("after the failure", json, _expectedRanBeforeGate);

        // Phase 3: a failed gate stays failed and filters nothing from then on.
        Scan again = ScanPlugins(g.Plugins, "chainloader");
        Check(again.Items.Count == totalPlugins, "after a failure every plugin is passed on to the chainloader", "found " + again.Items.Count + " of " + totalPlugins);
        string finalJson;
        ArenaGateReport final = ReadReport(out finalJson);
        Check(final != null && !final.Active, "the report stays inactive");
    }


    // ------------------------------------------------------------------ patchers the gate cannot place

    /// <summary>Called by the emitted callbacks of the dynamic patchers below (public: emitted code in another assembly calls it).</summary>
    public static void DynamicRecord(string what)
    {
        Calls.Add(what);
    }

    private static void EmitRecord(System.Reflection.Emit.ILGenerator il, string what)
    {
        il.Emit(System.Reflection.Emit.OpCodes.Ldstr, what);
        il.Emit(System.Reflection.Emit.OpCodes.Call, typeof(GateChecks).GetMethod("DynamicRecord", BindingFlags.Public | BindingFlags.Static));
    }

    /// <summary>
    /// A patcher whose delegates come from a dynamic assembly: it has no file, so the gate cannot name it. The callbacks
    /// record "targets:", "init:" and "fin:" plus <paramref name="callName"/>.
    /// </summary>
    private static PatcherPlugin MakeDynamicAssemblyPatcher(string typeName, string callName)
    {
        System.Reflection.Emit.AssemblyBuilder asm = AppDomain.CurrentDomain.DefineDynamicAssembly(
            new AssemblyName("Dynamic_" + callName), System.Reflection.Emit.AssemblyBuilderAccess.Run);
        System.Reflection.Emit.ModuleBuilder module = asm.DefineDynamicModule("Dynamic_" + callName);
        System.Reflection.Emit.TypeBuilder type = module.DefineType("Dynamic.Patcher_" + callName,
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class | System.Reflection.TypeAttributes.Abstract | System.Reflection.TypeAttributes.Sealed);
        System.Reflection.MethodAttributes attributes = System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static;

        System.Reflection.Emit.ILGenerator il = type.DefineMethod("Targets", attributes, typeof(IEnumerable<string>), Type.EmptyTypes).GetILGenerator();
        EmitRecord(il, "targets:" + callName);
        il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_0);
        il.Emit(System.Reflection.Emit.OpCodes.Newarr, typeof(string));
        il.Emit(System.Reflection.Emit.OpCodes.Ret);

        il = type.DefineMethod("Init", attributes, typeof(void), Type.EmptyTypes).GetILGenerator();
        EmitRecord(il, "init:" + callName);
        il.Emit(System.Reflection.Emit.OpCodes.Ret);

        il = type.DefineMethod("Fin", attributes, typeof(void), Type.EmptyTypes).GetILGenerator();
        EmitRecord(il, "fin:" + callName);
        il.Emit(System.Reflection.Emit.OpCodes.Ret);

        Type created = type.CreateType();
        return new PatcherPlugin
        {
            TypeName = typeName,
            TargetDLLs = (Func<IEnumerable<string>>)Delegate.CreateDelegate(typeof(Func<IEnumerable<string>>), created.GetMethod("Targets")),
            Initializer = (Action)Delegate.CreateDelegate(typeof(Action), created.GetMethod("Init")),
            Finalizer = (Action)Delegate.CreateDelegate(typeof(Action), created.GetMethod("Fin")),
        };
    }

    private static void NoPatchAtAll(ref AssemblyDefinition assembly)
    {
    }

    /// <summary>
    /// A patcher with a patch method and nothing else: no target list, no initializer, no finalizer. The gate looks for the
    /// assembly of a patcher in those three delegates, so it finds none. (Mono's dynamic methods do not make such a patcher:
    /// their delegates report an assembly of the runtime, which no list names, so that case is switched off as "mscorlib.dll".)
    /// BepInEx itself would fail on the missing target list if the gate left the patcher alone.
    /// </summary>
    private static PatcherPlugin MakeNullOriginPatcher(string typeName)
    {
        return new PatcherPlugin { TypeName = typeName, Patcher = NoPatchAtAll };
    }

    private static bool RanAny(string callName)
    {
        return Calls.Contains("init:" + callName) || Calls.Contains("targets:" + callName) || Calls.Contains("fin:" + callName);
    }

    /// <summary>
    /// Fail closed: a patcher the gate cannot trace to a file cannot be on the whitelist, so the gate switches it off and
    /// names it as "(unidentified) type name" instead of leaving it active in a session that calls itself filtered. One that
    /// sorts before the gate has run its initializer, which cannot be undone: it is listed in patchersRanBeforeGate like any
    /// other denied patcher that ran early. Two kinds of patchers the gate cannot place, one before and one after the gate each:
    /// delegates from a dynamic assembly (an assembly without a file) and a patcher with no delegate to look at (no assembly).
    /// The second kind has no initializer, so it cannot have run early; BepInEx would fail on its missing target list if the
    /// gate left it active, so a preloader phase that gets to its end shows that it was switched off.
    /// </summary>
    private static void ScenarioUnidentifiedPatchers()
    {
        _omitEarlyPatcher = false;
        Fake g = Fake.Create("unidentified", true);
        BuildWorld(g, WorldKind.Full);
        InstallProbes();
        _beforeDirectory = () =>
        {
            AssemblyPatcher.AddPatcher(MakeDynamicAssemblyPatcher("Early.Dynamic", "dyn-early"));
            AssemblyPatcher.AddPatcher(MakeNullOriginPatcher("Early.Null"));
        };
        _afterDirectory = () =>
        {
            AssemblyPatcher.AddPatcher(MakeDynamicAssemblyPatcher("Late.Dynamic", "dyn-late"));
            AssemblyPatcher.AddPatcher(MakeNullOriginPatcher("Late.Null"));
        };

        RunPreloaderPhase(true);
        string json;
        ArenaGateReport report = ReadReport(out json);
        Check(report != null, "the gate published a report", "no report");
        if (report == null) return;

        List<PatcherSpec> patchers = WorldPatchers();
        List<string> sorted = _orderBeforePatching.Select(l => l.Assembly).ToList();
        int gateIndex = sorted.IndexOf(GateAssemblyName);
        Check(gateIndex >= 0, "the gate was found as a patcher", "patcher order: " + string.Join(",", sorted.ToArray()));
        List<string> order = _orderBeforePatching.Select(l => l.TypeName).ToList();
        int gateTypeIndex = _orderBeforePatching.FindIndex(l => l.Assembly == GateAssemblyName);
        Check(order.IndexOf("Early.Dynamic") >= 0 && order.IndexOf("Early.Dynamic") < gateTypeIndex && order.IndexOf("Early.Null") >= 0 && order.IndexOf("Early.Null") < gateTypeIndex
                && order.IndexOf("Late.Dynamic") > gateTypeIndex && order.IndexOf("Late.Null") > gateTypeIndex,
            "the setup puts two patchers the gate cannot place before the gate and two after it", "patcher type order: " + string.Join(" > ", order.ToArray()));

        const string earlyDynamic = "(unidentified) Early.Dynamic";
        const string earlyNull = "(unidentified) Early.Null";
        const string lateDynamic = "(unidentified) Late.Dynamic";
        const string lateNull = "(unidentified) Late.Null";
        var deniedFiles = new List<string>();
        foreach (PatcherSpec p in patchers)
        {
            string cause;
            if (!PatcherAllowedInWorld(WorldKind.Full, p, out cause)) deniedFiles.Add(Path.GetFileName(p.RelativePath));
        }

        SameSet("every patcher that is not allowed is neutralised, the ones the gate cannot place under their type names",
            deniedFiles.Concat(new[] { earlyDynamic, earlyNull, lateDynamic, lateNull }), report.PatchersNeutralised);
        List<string> ranEarly = ExpectedRanBeforeGate(WorldKind.Full, patchers, sorted, gateIndex);
        ranEarly.Add(earlyDynamic); // the one with an initializer that sorts before the gate; the others have none, or sort after it
        CheckRanBeforeGateJson("with patchers the gate cannot place", json, ranEarly);

        // What actually ran: the early ones their initializer (nothing can be done about that), the late ones nothing at all.
        Check(Calls.Contains("init:dyn-early"), "the file-less patcher dyn-early sorts before the gate, so its initializer ran");
        Check(!Calls.Contains("targets:dyn-early") && !Calls.Contains("fin:dyn-early"), "its target list and finalizer were switched off");
        Check(!RanAny("dyn-late"), "the file-less patcher dyn-late sorts after the gate and ran nothing", string.Join(",", Calls.Where(c => c.EndsWith("dyn-late")).ToArray()));

        const string cannotBeOn = "could not be determined, so it cannot be on the whitelist and was neutralised";
        foreach (string name in new[] { earlyDynamic, earlyNull, lateDynamic, lateNull })
        {
            Check(report.Warnings.Any(w => w.StartsWith("preloader patcher " + name + ": ", StringComparison.Ordinal) && w.EndsWith(cannotBeOn, StringComparison.Ordinal)),
                "the report warns that " + name + " cannot be placed and was neutralised", "warnings: " + string.Join(" | ", report.Warnings.ToArray()));
        }

        Check(report.Warnings.Any(w => w.StartsWith("preloader patcher " + earlyDynamic + " could not be identified, but its initializer ran before the gate and cannot be undone", StringComparison.Ordinal)),
            "the report warns that the initializer of " + earlyDynamic + " could not be stopped");
        Check(!report.Warnings.Any(w => (w.Contains(lateDynamic) || w.Contains(earlyNull) || w.Contains(lateNull)) && w.Contains("initializer ran")),
            "no initializer warning for a patcher that has no initializer or sorts after the gate");
        CheckPatchersNamedAtInfo(report.PatchersNeutralised,
            Log.From(GateSource).Where(e => e.Level == LogLevel.Info && e.Message.Contains(" preloader patcher")).ToList());
        Console.WriteLine("  info: patcher order " + string.Join(" > ", order.ToArray()));
    }

    /// <summary>
    /// Fail closed: when the gate cannot find its own place in the preloader's patcher list it cannot tell which patchers ran
    /// before it, so every neutralised patcher that has an initializer is listed in patchersRanBeforeGate and one warning
    /// says why. Here the gate is called through a wrapper patcher (a file of its own is not in the list), which sorts first.
    /// </summary>
    private static void ScenarioGateUnlocated()
    {
        _omitEarlyPatcher = false;
        Fake g = Fake.Create("unlocated", true);
        BuildWorld(g, WorldKind.Full);
        File.Delete(g.GateFile); // the gate is not a patcher of this game: only the wrapper calls it
        InstallProbes();
        string gatePath = Path.Combine(Path.Combine(_driverDir, "gate"), ArenaSessionContract.GateFileName);
        _beforeDirectory = () =>
        {
            AssemblyPatcher.AddPatcher(new PatcherPlugin
            {
                TypeName = "Wrapper.Gate",
                TargetDLLs = EmptyTargets,
                Initializer = () =>
                {
                    Assembly gate = Assembly.LoadFrom(gatePath);
                    gate.GetType("ArenaMode.Gate.Patcher", true).GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                },
            });
        };

        RunPreloaderPhase(true);
        string json;
        ArenaGateReport report = ReadReport(out json);
        Check(report != null, "the gate published a report", "no report");
        if (report == null) return;

        Check(!_orderBeforePatching.Any(l => l.Assembly == GateAssemblyName), "the gate is not in the patcher list of this game",
            string.Join(" > ", _orderBeforePatching.Select(l => l.Assembly).ToArray()));
        List<PatcherSpec> patchers = WorldPatchers();
        var denied = new List<string>();
        foreach (PatcherSpec p in patchers)
        {
            string cause;
            if (!PatcherAllowedInWorld(WorldKind.Full, p, out cause)) denied.Add(Path.GetFileName(p.RelativePath));
        }

        // The wrapper is a patcher of the driver's own file (GateChecks.exe), which no list names: switched off like the rest.
        string wrapper = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
        List<string> expected = denied.Concat(new[] { wrapper }).ToList();
        SameSet("every patcher that is not allowed is neutralised", expected, report.PatchersNeutralised);
        CheckRanBeforeGateJson("with an unknown gate position", json, expected);
        Check(report.Warnings.Any(w => w.StartsWith("the position of the gate in the preloader's patcher list is unknown", StringComparison.Ordinal)
                && w.Contains("reported as having run before the gate")),
            "one warning says that the position of the gate is unknown", "warnings: " + string.Join(" | ", report.Warnings.ToArray()));
        Check(report.Warnings.Count(w => w.StartsWith("the position of the gate", StringComparison.Ordinal)) == 1, "the warning is given once");
        Check(!report.Warnings.Any(w => w.Contains("but its initializer ran before the gate")), "no per-patcher initializer warning without a known position");
        Console.WriteLine("  info: patcher order " + string.Join(" > ", _orderBeforePatching.Select(l => l.Assembly).ToArray()));
    }

    // ------------------------------------------------------------------ control: installing too early

    private static void ScenarioControlEarlyInstall()
    {
        Fake.Create("control", true);
        InstallProbes();

        AssemblyPatcher.AddPatcher(MakeBuiltInPatcher());
        AssemblyPatcher.AddPatcher(new PatcherPlugin
        {
            TypeName = "Control.EarlyInstall",
            TargetDLLs = EmptyTargets,
            Initializer = EarlyInstall,
        });

        Mark("phase:preloader-start");
        AssemblyPatcher.PatchAndLoad(Paths.DllSearchPaths);
        AssemblyPatcher.DisposePatchers();
        Mark("phase:preloader-end");
        Mark("phase:chainloader-start");

        List<string> violations = UnityViolations();
        Check(violations.Count > 0, "negative control: patching FindPluginTypes<PluginInfo> during an Initialize loads UnityEngine early",
            "the detector found nothing; calls: " + string.Join(", ", Calls.Where(c => c.StartsWith("load:Unity", StringComparison.Ordinal)).ToArray()));
        foreach (string v in violations) Console.WriteLine("  info: control violation: " + v);
    }

    private static IEnumerable<string> EmptyTargets()
    {
        return new string[0];
    }

    private static void EarlyInstall()
    {
        MethodInfo closed = Bep.FindPluginTypes(Bep.PluginInfo);
        new Harmony("control.early").Patch(closed, postfix: new HarmonyMethod(typeof(GateChecks).GetMethod("ControlPostfix", BindingFlags.Public | BindingFlags.Static)));
    }

    public static void ControlPostfix(object __result)
    {
    }

    // ------------------------------------------------------------------ the installed game's plugins

    private static string BaselineFile()
    {
        return Path.Combine(_work, "baseline-real.tsv");
    }

    private static void CopyRealPlugins(Fake g)
    {
        string source = Path.GetFullPath(Path.Combine(_sptRoot, "BepInEx", "plugins"));
        foreach (string file in Directory.GetFiles(source, "*.dll", SearchOption.AllDirectories))
        {
            string target = Path.Combine(g.Plugins, Relative(source, file).Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(file, target, true);
        }
    }

    private static void ScenarioBaselineReal()
    {
        // Same environment as the gated run (UnityEngine.CoreModule present in Managed), so that
        // Cecil resolves the same base types in both.
        Fake g = Fake.Create("baseline-real", true);
        CopyRealPlugins(g);
        InstallProbes();
        Scan scan = ScanPlugins(g.Plugins, "chainloader");
        var lines = new List<string>();
        foreach (Found f in scan.Items)
        {
            lines.Add(string.Join("\t", new[] { f.Guid, f.Name, f.Version, Relative(g.Plugins, f.File), string.Join(",", f.HardDeps.ToArray()) }));
        }

        File.WriteAllLines(BaselineFile(), lines.ToArray());
        Check(scan.Items.Count > 10, "the installed game's plugins were scanned without the gate", "found " + scan.Items.Count);
        Console.WriteLine("  info: " + scan.Items.Count + " plugins in the installed game");
    }

    private static void ScenarioRealInstall()
    {
        Fake g = Fake.Create("real-install", true);
        CopyRealPlugins(g);
        Directory.CreateDirectory(Path.GetDirectoryName(g.PolicyFile));
        File.Copy(_policySource, g.PolicyFile, true);

        // Dummy patchers under the names of the patchers the game really has.
        WriteDummyPatcher(Path.Combine(g.Patchers, "FixPluginTypesSerialization.dll"), "FixPluginTypesSerialization");
        WriteDummyPatcher(Path.Combine(g.Patchers, "maschine-LogTools.dll"), "maschine-LogTools");
        WriteDummyPatcher(Path.Combine(g.Patchers, "spt-prepatch.dll"), "spt-prepatch");
        WriteDummyPatcher(Path.Combine(g.Patchers, "WTT-ContentBackportPatcher/WTT-ContentBackportPatcher.dll"), "WTT-ContentBackportPatcher");
        Directory.CreateDirectory(Path.GetDirectoryName(g.GateFile));
        File.Copy(Path.Combine(Path.Combine(_driverDir, "gate"), ArenaSessionContract.GateFileName), g.GateFile, true);
        InstallProbes();

        RunPreloaderPhase(true);
        List<string> sorted = _orderBeforePatching.Select(l => l.Assembly).ToList();
        int gateIndex = sorted.IndexOf(GateAssemblyName);
        bool wttBefore = sorted.IndexOf("WTT-ContentBackportPatcher") < gateIndex;
        Console.WriteLine("  info: WTT-ContentBackportPatcher sorts " + (wttBefore ? "BEFORE" : "AFTER") + " the gate under this Mono");
        Check(UnityViolations().Count == 0, "no Unity assembly was loaded before the patched UnityEngine.CoreModule", string.Join("; ", UnityViolations().ToArray()));
        // The shipped default list allows the WTT patcher (user decision 2026-10-22), so all four run untouched.
        foreach (string allowed in new[] { "FixPluginTypesSerialization", "maschine-LogTools", "spt-prepatch", "WTT-ContentBackportPatcher" })
        {
            Check(Calls.Contains("init:" + allowed) && Calls.Contains("targets:" + allowed) && Calls.Contains("patch:" + allowed) && Calls.Contains("finish:" + allowed),
                "the real patcher name " + allowed + " is allowed and runs untouched");
        }

        CheckBuiltIn(true);

        Mark("phase:chainloader-start");
        Scan scan = ScanPlugins(g.Plugins, "chainloader");
        string json;
        ArenaGateReport report = ReadReport(out json);
        Check(report != null && report.Active, "the gate is active on the installed game's plugins", report == null ? "no report" : report.Error);
        if (report == null || !report.Active) return;

        // Every real patcher is allowed, so none is neutralised and none counts as having run before the gate, wherever it sorts.
        Check(report.PatchersNeutralised.Count == 0, "no patcher of the installed game is neutralised", string.Join(", ", report.PatchersNeutralised.ToArray()));
        CheckRanBeforeGateJson("on the installed game's patchers", json, new List<string>());

        // The oracle: the units as the baseline process read them, decided by the shared code.
        var units = new List<ArenaSessionUnit>();
        foreach (string line in File.ReadAllLines(BaselineFile()))
        {
            string[] cells = line.Split('\t');
            units.Add(new ArenaSessionUnit(cells[0], cells[1], cells[2], cells[3], cells[4].Length == 0 ? new string[0] : cells[4].Split(',')));
        }

        ArenaWhitelistDecision oracle = ArenaWhitelistDecision.Evaluate(LoadPolicyLikeTheGate(g), ArenaWhitelistSide.Client, units);
        SameSet("the allowed plugins are those the policy decides on the baseline",
            oracle.Allowed.Select(u => u.Guid + "@" + u.File + "=" + u.Reason),
            report.Allowed.Select(u => u.Guid + "@" + Relative(g.Plugins, u.File) + "=" + u.Reason));
        SameSet("the excluded plugins are those the policy decides on the baseline",
            oracle.Excluded.Select(u => u.Guid + "@" + u.File + "=" + u.Reason),
            report.Excluded.Select(u => u.Guid + "@" + Relative(g.Plugins, u.File) + "=" + u.Reason));
        SameSet("the plugin list holds exactly the allowed plugins",
            oracle.Allowed.Select(u => u.Guid + "@" + u.File), scan.Items.Select(i => i.Guid + "@" + Relative(g.Plugins, i.File)));
        SameSet("the warnings are those of the baseline decision", oracle.Warnings, report.Warnings.Where(w => !w.StartsWith("preloader patcher", StringComparison.Ordinal)));

        foreach (string must in new[] { "com.SPT.core", "com.SPT.custom", "com.SPT.debugging", "com.SPT.singleplayer", "com.maschine.ArenaMode", "me.sol.sain", "xyz.drakia.bigbrain", "xyz.drakia.waypoints" })
        {
            ArenaSessionUnit unit = report.Allowed.FirstOrDefault(u => u.Guid == must);
            Check(unit != null, "the installed " + must + " is allowed", "not in the allowed list");
        }

        Console.WriteLine("  info: allowed " + report.Allowed.Count + " (" + string.Join(", ", report.Allowed.Select(Describe).ToArray()) + ")");
        Console.WriteLine("  info: excluded " + report.Excluded.Count + " (" + string.Join(", ", report.Excluded.Select(u => u.Guid).ToArray()) + ")");
        Console.WriteLine("  info: warnings " + string.Join(" | ", report.Warnings.ToArray()));

        IDictionary cache = Bep.LoadCache("chainloader");
        int cachedPlugins = 0;
        foreach (DictionaryEntry entry in cache) cachedPlugins += ((IList)Bep.Prop(entry.Value, "CacheItems")).Count;
        Check(cachedPlugins == units.Count, "the type cache lists every installed plugin", "cache has " + cachedPlugins + ", baseline " + units.Count);
    }
}
