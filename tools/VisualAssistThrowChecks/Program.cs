using System.Reflection;
using ArenaMode.Compat;
using ArenaMode.Session;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using VisualAssist;

static class Program
{
    private static int _checks;
    private static readonly MethodInfo Prefix = typeof(ArenaVisualAssistCompat).GetMethod("BeforeForeignThrow", BindingFlags.Static | BindingFlags.NonPublic);
    private static void Check(bool value, string name) { _checks++; if (!value) throw new Exception("FAIL: " + name); }
    private static (bool foreign, bool result) Run(Player player, Player.BaseGrenadeHandsController hands, bool low = false)
    {
        object[] args = { hands, player, low, false };
        bool foreign = (bool)Prefix.Invoke(null, args);
        return (foreign, (bool)args[3]);
    }
    private static void Native(Player player, Player.BaseGrenadeHandsController hands, string why, bool low = false)
    {
        int before = GrenadeArc.Calculations;
        var actual = Run(player, hands, low);
        Check(!actual.foreign && actual.result, why);
        Check(GrenadeArc.Calculations == before, why + " does not calculate");
    }
    private static void Published(string path)
    {
        Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) ==
            "D0150D2FCC93639933F3075EF80CC303CA3E7E8B6C47539A20488C4FB6AF3EC9", "reviewed VisualAssist release bytes");
        using var dll = Mono.Cecil.AssemblyDefinition.ReadAssembly(path);
        var type = dll.MainModule.GetType("VisualAssist.Plugin");
        var meta = type.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        Check((string)meta.ConstructorArguments[0].Value == ArenaVisualAssistCompat.PluginGuid
            && (string)meta.ConstructorArguments[2].Value == "1.2.0", "published plugin identity");
        var target = dll.MainModule.GetType("VisualAssist.GrenadeThrowPrefixPatch").Methods.Single(m => m.Name == "Prefix");
        Check(target.IsStatic && target.ReturnType.FullName == "System.Boolean"
            && string.Join(",", target.Parameters.Select(p => p.ParameterType.FullName)) ==
            "EFT.Player/BaseGrenadeHandsController,EFT.Player,UnityEngine.Transform,System.Boolean,System.Single", "published throw contract");
        var arc = dll.MainModule.GetType("VisualAssist.GrenadeArc");
        var calculate = arc.Methods.Single(m => m.Name == "CalculateGrenadeThrow");
        Check(!calculate.IsStatic && calculate.IsPrivate && calculate.ReturnType.FullName == "VisualAssist.GrenadeThrow"
            && calculate.Parameters.Single().ParameterType.FullName == "System.Boolean", "published calculation contract");
        Check(arc.Fields.Single(f => f.Name == "localPlayer").FieldType.FullName == "EFT.Player"
            && arc.Fields.Single(f => f.Name == "GrenadeThrow").FieldType.FullName == "VisualAssist.GrenadeThrow", "published ownership and prediction fields");
        var prediction = dll.MainModule.GetType("VisualAssist.GrenadeThrow");
        Check(prediction.IsValueType && new[] { "ThrowPosition", "ThrowForce" }.All(n => prediction.Fields.Single(f => f.Name == n).FieldType.FullName == "UnityEngine.Vector3"), "published prediction vectors");
    }
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0) Published(args[0]);
            for (int i=0; i<10; i++) Check(ArenaVisualAssistCompat.GetRejectionReason() == null, "absent mod inert");
            Check(Harmony.Removes == 0, "absent polls never unpatch");
            Chainloader.PluginInfos[ArenaVisualAssistCompat.PluginGuid] = new() { Instance = new VisualAssist.Plugin() };
            Check(ArenaVisualAssistCompat.GetRejectionReason() == null, "reviewed contracts bind");
            var player = new Player();
            var hands = new Player.GrenadeHandsController(); player.HandsController = hands;
            Singleton<GameWorld>.Instance = new() { MainPlayer = player };
            Singleton<AbstractGame>.Instance = new LocalGame { LocalPlayer = player };
            var arc = new GrenadeArc { localPlayer = player }; Singleton<GrenadeArc>.Instance = arc;
            Check(Run(player, new Player.QuickGrenadeThrowHandsController()).foreign && GrenadeArc.Calculations == 0, "ordinary raid preserves foreign behavior");
            ArenaSession.IsActive = true;
            var quick = new Player.QuickGrenadeThrowHandsController(); player.HandsController = quick;
            Native(player, quick, "first quick throw uses native physics");
            player.HandsController = hands;
            Check(Run(player, hands).foreign && arc.GrenadeThrow.ThrowPosition.x == 17, "ordinary throw refreshes stale position");
            GrenadeArc.NextPosition.x = 72;
            Check(Run(player, hands).foreign && arc.GrenadeThrow.ThrowPosition.x == 72, "moved throw refreshes prediction again");
            player.HandsController = quick;
            Native(player, quick, "quick throw after preview uses native physics"); player.HandsController = hands;
            hands.CurrentOperation = new Player.GrenadeHandsController.LowThrowOperation();
            Native(player, hands, "mismatched low flag uses native physics");
            Check(Run(player, hands, true).foreign && GrenadeArc.LastLow, "low throw refreshed through foreign formula");
            hands.CurrentOperation = new Player.GrenadeHandsController.HighThrowOperation();
            arc.localPlayer = new Player(); Native(player, hands, "stale physical owner excluded"); arc.localPlayer = player;
            Singleton<GameWorld>.Instance.MainPlayer = new Player(); Native(player, hands, "foreign world player excluded"); Singleton<GameWorld>.Instance.MainPlayer = player;
            ((LocalGame)Singleton<AbstractGame>.Instance).LocalPlayer = new Player(); Native(player, hands, "foreign game player excluded"); ((LocalGame)Singleton<AbstractGame>.Instance).LocalPlayer = player;
            ArenaSession.Exact = false; Native(player, hands, "stale raid excluded"); ArenaSession.Exact = true;
            player.HandsController = new Player.GrenadeHandsController(); Native(player, hands, "stale hands excluded"); player.HandsController = hands;
            player.HealthController.IsAlive = false; Native(player, hands, "dead player excluded"); player.HealthController.IsAlive = true;
            player.IsAI = true; Native(player, hands, "AI excluded"); player.IsAI = false;
            player.IsYourPlayer = false; Native(player, hands, "remote player excluded"); player.IsYourPlayer = true;
            arc.isActiveAndEnabled = false; Native(player, hands, "disabled component excluded"); arc.isActiveAndEnabled = true;
            VisualAssist.Plugin.GrenadeArcEnabled.Value = false; Native(player, hands, "disabled feature uses native physics"); VisualAssist.Plugin.GrenadeArcEnabled.Value = true;
            Singleton<GrenadeArc>.Instance = null; Native(player, hands, "missing arc uses native physics"); Singleton<GrenadeArc>.Instance = arc;
            GrenadeArc.Fail = true; Check(Run(player, hands) == (false, true), "calculation failure preserves native throw"); GrenadeArc.Fail = false;
            GrenadeArc.NextPosition.x = float.NaN; Check(Run(player, hands) == (false, true), "invalid position preserves native throw"); GrenadeArc.NextPosition.x = 1;
            GrenadeArc.NextForce.y = float.PositiveInfinity; Check(Run(player, hands) == (false, true), "invalid force preserves native throw"); GrenadeArc.NextForce = default;
            Check(Run(player, hands) == (false, true), "zero force preserves native throw");
            ArenaVisualAssistCompat.Shutdown();
            Check(Harmony.Entries.Count == 0, "owned hook removed");
            Chainloader.PluginInfos.Clear(); int removes = Harmony.Removes;
            for (int i=0; i<10; i++) ArenaVisualAssistCompat.GetRejectionReason();
            Check(Harmony.Removes == removes, "absent after shutdown never unpatches");
            Console.WriteLine("VisualAssist throw checks passed: " + _checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
