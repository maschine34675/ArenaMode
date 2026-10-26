using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using ArenaMode.Compat;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string why) { if (!value) throw new Exception(why); _checks++; }
    private static MethodInfo _throw;
    private static void Build(string mode)
    {
        var version = mode == "newer" ? "2.1.0" : mode == "older" ? "1.9.0" : "2.0.0";
        var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("FastGrenadeThrow"), AssemblyBuilderAccess.Run);
        var type = asm.DefineDynamicModule("test").DefineType("FastGrenadeThrow.FastGrenadeThrowPlugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        type.SetCustomAttribute(new CustomAttributeBuilder(typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }),
            new object[] { FastGrenadeThrowCompat.PluginGuid, "Fast Grenade Throw", version }));
        var find = type.DefineMethod(mode == "drift" ? "FindGrenadeChanged" : "FindGrenade", MethodAttributes.Private | MethodAttributes.Static, typeof(ThrowWeap), new[] { typeof(Player) });
        find.SetImplementationFlags(MethodImplAttributes.NoInlining); var il = find.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, typeof(ForeignCalls).GetMethod("Find")); il.Emit(OpCodes.Ret);
        var toss = type.DefineMethod("TryQuickThrow", MethodAttributes.Private | MethodAttributes.Static, typeof(void), new[] { typeof(Player), typeof(bool) });
        toss.SetImplementationFlags(MethodImplAttributes.NoInlining); il = toss.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, find); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, typeof(ForeignCalls).GetMethod("Record")); il.Emit(OpCodes.Ret);
        var runtime = type.CreateType(); _throw = runtime.GetMethod("TryQuickThrow", BindingFlags.Static | BindingFlags.NonPublic);
        Chainloader.PluginInfos[FastGrenadeThrowCompat.PluginGuid] = new PluginInfo { Instance = (BaseUnityPlugin)Activator.CreateInstance(runtime), Metadata = runtime.GetCustomAttribute<BepInPlugin>() };
    }
    private static void Invoke(Player player, bool low = false) => _throw.Invoke(null, new object[] { player, low });
    private static void Published(string path)
    {
        using var dll = Mono.Cecil.AssemblyDefinition.ReadAssembly(path);
        Check(dll.Name.Name == "FastGrenadeThrow", "release assembly identity");
        var plugin = dll.MainModule.GetType("FastGrenadeThrow.FastGrenadeThrowPlugin");
        var info = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        Check((string)info.ConstructorArguments[0].Value == FastGrenadeThrowCompat.PluginGuid && (string)info.ConstructorArguments[2].Value == "2.0.0", "release plugin identity/version");
        var find = plugin.Methods.Single(m => m.Name == "FindGrenade"); var toss = plugin.Methods.Single(m => m.Name == "TryQuickThrow");
        Check(find.IsPrivate && find.IsStatic && find.ReturnType.FullName == "EFT.InventoryLogic.ThrowWeap" && find.Parameters.Single().ParameterType.FullName == "EFT.Player", "release selection API");
        Check(toss.IsPrivate && toss.IsStatic && toss.ReturnType.FullName == "System.Void" && string.Join(",", toss.Parameters.Select(p => p.ParameterType.FullName)) == "EFT.Player,System.Boolean", "release throw API");
        Check(find.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "GetThrowablePriorityGrenadesList"), "release selection uses effective query");
        Check(plugin.Methods.Single(m => m.Name == "Update").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "GetKeyDown"), "release directly polls Unity keys");
        using var hash = SHA256.Create(); var actual = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        Check(actual == "1CF1FA49DDA94E9936053043E171A5EE1C2AAB6A9C6A696A7B532235A8D83AE4", "reviewed release bytes");
    }
    public static int Main(string[] args)
    {
        try
        {
            string mode = args.Length == 0 ? "normal" : args[0];
            if (mode == "published") { Published(args[1]); Console.WriteLine("PASS released metadata " + _checks); return 0; }
            Check(FastGrenadeThrowCompat.GetRejectionReason() == null, "absent plugin no-op");
            Build(mode); var issue = FastGrenadeThrowCompat.GetRejectionReason();
            if (mode == "drift" || mode == "older") { Check(issue != null, "unsupported contract rejected"); Check(FastGrenadeThrowCompat.GetRejectionReason() == issue && ArenaMode.Plugin.ModLogger.Warnings == 1, "bounded contract warning"); Console.WriteLine("PASS " + mode + " " + _checks); return 0; }
            Check(issue == null, "contract admitted");
            Check(ArenaMode.Plugin.ModLogger.Warnings == (mode == "newer" ? 1 : 0), "forward gate bounded warning");
            var player = new Player(); var rgd = new ThrowWeap { Id = "rgd" }; var molotov = new ThrowWeap { Id = "molotov" };
            var inv = player.InventoryController; inv.Candidates.AddRange(new[] { rgd, molotov }); inv.Inventory.Equipment.Contents.AddRange(inv.Candidates); inv.Inventory.Equipment.TopPriorityGrenade = molotov;
            var world = new GameWorld { MainPlayer = player }; var game = new LocalGame { LocalPlayer = player }; Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
            Invoke(player); Check(ReferenceEquals(ForeignCalls.Last, rgd), "ordinary raid preserves foreign behavior");
            ArenaSession.IsActive = true; Invoke(player, true); Check(ReferenceEquals(ForeignCalls.Last, molotov) && ForeignCalls.Low, "Arena priority composes with underhand throw");
            inv.Inventory.Equipment.TopPriorityGrenade = rgd; Invoke(player); Check(ReferenceEquals(ForeignCalls.Last, rgd), "user native selection remains authoritative");
            inv.Inventory.Equipment.TopPriorityGrenade = molotov;
            foreach (var invalid in new[] { "address", "outside", "unexamined", "excluded", "clone" })
            {
                if (invalid == "address") molotov.CurrentAddress = null;
                if (invalid == "outside") inv.Inventory.Equipment.Contents.Remove(molotov);
                if (invalid == "unexamined") molotov.Examined = false;
                if (invalid == "excluded") inv.Candidates.Remove(molotov);
                if (invalid == "clone") inv.Inventory.Equipment.TopPriorityGrenade = new ThrowWeap { Id = "molotov" };
                Invoke(player); Check(ReferenceEquals(ForeignCalls.Last, rgd), "invalid priority leaves foreign result: " + invalid);
                molotov.CurrentAddress = new(); molotov.Examined = true; if (!inv.Candidates.Contains(molotov)) inv.Candidates.Add(molotov); if (!inv.Inventory.Equipment.Contents.Contains(molotov)) inv.Inventory.Equipment.Contents.Add(molotov); inv.Inventory.Equipment.TopPriorityGrenade = molotov;
            }
            var gates = new Action<bool>[] { v => ArenaMode.Hud.ArenaInputBlocker.Blocked = v, v => ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress = v, v => ArenaMode.Respawn.ArenaRespawnDriver.StandDown = v, v => ArenaMode.Spectator.ArenaSpectatorService.IsActive = v, v => ArenaMode.Combat.WeaponSwapService.IsSwapping = v, v => player.HealthController.IsAlive = !v, v => player.IsAI = v, v => player.IsYourPlayer = !v, v => ArenaSession.Exact = !v, v => game.LocalPlayer = v ? new Player() : player, v => world.MainPlayer = v ? new Player() : player };
            foreach (var gate in gates) { gate(true); int before = ForeignCalls.Throws; Invoke(player); Check(ForeignCalls.Throws == before, "blocked transition cannot enter foreign throw"); ArenaSession.IsActive = false; Invoke(player); Check(ForeignCalls.Throws == before + 1, "same guard does not change ordinary raid"); ArenaSession.IsActive = true; gate(false); }
            int count = ForeignCalls.Throws; Invoke(new Player()); Check(ForeignCalls.Throws == count, "old physical body denied even in same raid");
            Invoke(player); Check(ForeignCalls.Throws == count + 1 && ReferenceEquals(ForeignCalls.Last, molotov), "live body recovers without foreign flag mutation");
            new Harmony("com.maschine.ArenaMode.FastGrenadeThrowCompat").UnpatchSelf(); Check(FastGrenadeThrowCompat.GetRejectionReason() != null, "removed hooks refuse admission");
            Chainloader.PluginInfos.Clear(); Check(FastGrenadeThrowCompat.GetRejectionReason() == null, "removed optional plugin no-op");
            Console.WriteLine("PASS " + mode + " " + _checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
