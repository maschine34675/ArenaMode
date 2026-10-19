using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Compat;
using ArenaMode.Session;
using ArenaMode.Shared;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using JsonType;
using Redeploy.Core.GameAccess;

public static class Program
{
    private static int _checks, _original;
    private const string Token = "b6c06414ec214e89a0bf16a7975d78b0";
    private const string Companion = "selected-companion";
    public static void Original() => _original++;
    public static readonly List<int> ReturnedTickets = new();
    public static bool FailReturn;
    public static void ReturnTicket(int ticket)
    {
        if (FailReturn) throw new InvalidOperationException("injected scheduler fault");
        ReturnedTickets.Add(ticket);
    }
    private static void Need(bool condition, string label) { _checks++; if (!condition) throw new InvalidOperationException("ASSERT: " + label); }
    private sealed record Fixture(Type Plugin, Type Layer, Type Brain, Type Finder, Type Inventory, BepInPlugin Metadata);

    private static Type Define(ModuleBuilder module, string name, Type parent, (string, Type)[] fields, params (string, Type, Type[])[] methods)
    {
        var type = module.DefineType(name, TypeAttributes.Public, parent);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        foreach (var (field, kind) in fields) type.DefineField(field, kind, FieldAttributes.Public);
        foreach (var (method, returns, args) in methods)
        {
            var body = type.DefineMethod(method, MethodAttributes.Public, returns, args).GetILGenerator();
            body.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(Original)));
            if (returns == typeof(bool)) body.Emit(OpCodes.Ldc_I4_1);
            else if (returns == typeof(Task)) body.Emit(OpCodes.Call, typeof(Task).GetProperty(nameof(Task.CompletedTask)).GetMethod);
            body.Emit(OpCodes.Ret);
        }
        return type.CreateType();
    }

    private static Fixture Make(string version = "1.8.0", bool spawn = false, bool cleanup = false,
        string guid = ArenaLootingBotsContract.Guid, string assemblyName = "Skwizzy.LootingBots", string assemblyVersion = null,
        bool badOwner = false, bool badSearch = false, bool wrongBase = false, bool missingInit = false,
        bool missingQueue = false, bool badReturn = false, bool asynchronousStart = false)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName) { Version = new(assemblyVersion ?? version) }, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("fixture");
        var plugin = module.DefineType("LootingBots.LootingBots", TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.DefineDefaultConstructor(MethodAttributes.Public);
        plugin.SetCustomAttribute(new CustomAttributeBuilder(typeof(BepInPlugin).GetConstructor([typeof(string), typeof(string), typeof(string)]), [guid, "fixture", version]));
        Type pluginType = plugin.CreateType();
        var layer = Define(module, "LootingBots.LootingLayer", wrongBase ? typeof(object) : typeof(DrakiaXYZ.BigBrain.Brains.CustomLayer), [], ("IsActive", typeof(bool), []));
        var brainMethods = new List<(string, Type, Type[])> { ("Init", typeof(void), [typeof(BotOwner)]), ("Start", typeof(void), []), ("StartLooting", typeof(void), []), ("StopLooting", typeof(void), []) };
        if (spawn) brainMethods.Add(("OnSpawnAsync", typeof(Task), []));
        var brainBuilder = module.DefineType("LootingBots.Components.LootingBrain", TypeAttributes.Public, typeof(UnityEngine.MonoBehaviour));
        brainBuilder.DefineDefaultConstructor(MethodAttributes.Public);
        brainBuilder.DefineField("BotOwner", badOwner ? typeof(object) : typeof(BotOwner), FieldAttributes.Public);
        foreach (var (name, returns, args) in brainMethods)
        {
            var method = brainBuilder.DefineMethod(name, MethodAttributes.Public, returns, args);
            if (name == "Start" && asynchronousStart)
                method.SetCustomAttribute(new CustomAttributeBuilder(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute)
                    .GetConstructor([typeof(Type)]), [typeof(Program)]));
            var il = method.GetILGenerator(); il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(Original)));
            if (returns == typeof(Task)) il.Emit(OpCodes.Call, typeof(Task).GetProperty(nameof(Task.CompletedTask)).GetMethod);
            il.Emit(OpCodes.Ret);
        }
        var brain = brainBuilder.CreateType();
        var finderBuilder = module.DefineType("LootingBots.Components.LootFinder", TypeAttributes.Public, typeof(UnityEngine.MonoBehaviour));
        finderBuilder.DefineDefaultConstructor(MethodAttributes.Public);
        finderBuilder.DefineField("_botOwner", typeof(BotOwner), FieldAttributes.Public);
        foreach (var (name, returns, args) in new (string, Type, Type[])[] {
            ("Init", typeof(void), [typeof(BotOwner)]), ("BeginSearch", badSearch ? typeof(bool) : typeof(void), [typeof(int)]),
            ("StopFindingLoot", typeof(void), []), (missingQueue ? "ChangedQueue" : "EnqueuePriorityCorpse", typeof(void), [typeof(string)]),
            ("OnAirdropLanded", typeof(void), [typeof(EFT.Interactive.LootableContainer)]) })
        {
            var il = finderBuilder.DefineMethod(name, name == "OnAirdropLanded" ? MethodAttributes.Private : MethodAttributes.Public, returns, args).GetILGenerator();
            il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(Original)));
            if (returns == typeof(bool)) il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }
        var finder = finderBuilder.CreateType();
        var scheduler = module.DefineType("LootingBots.Utilities.ScanScheduler", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var returnMethod = scheduler.DefineMethod("Return", MethodAttributes.Public | MethodAttributes.Static,
            badReturn ? typeof(bool) : typeof(void), [typeof(int)]).GetILGenerator();
        returnMethod.Emit(OpCodes.Ldarg_0); returnMethod.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(ReturnTicket)));
        if (badReturn) returnMethod.Emit(OpCodes.Ldc_I4_1);
        returnMethod.Emit(OpCodes.Ret); scheduler.CreateType();
        var inventoryMethods = new List<(string, Type, Type[])>();
        if (cleanup) inventoryMethods.Add(("CleanupInventoryAsync", typeof(Task), [typeof(CancellationToken)]));
        if (!missingInit) inventoryMethods.Add(("Init", typeof(void), []));
        var inventory = Define(module, "LootingBots.Components.LootingInventoryController", typeof(object), [("_botOwner", typeof(BotOwner)), ("_botInventoryController", typeof(EFT.InventoryLogic.InventoryController))], inventoryMethods.ToArray());
        return new(pluginType, layer, brain, finder, inventory, new(guid, "fixture", version));
    }
    private static void Reset()
    {
        Harmony.FailUnpatch = false; ArenaLootingBotsCompat.Shutdown();
        Harmony.Bound.Clear(); Harmony.FailAfter = -1; Chainloader.PluginInfos.Clear();
        AccessTools.MissingSettings = false; LocalGameAccessor.FailRead = false;
        Singleton<AbstractGame>.Instance = null; Singleton<GameWorld>.Instance = null;
        ArenaSession.ArmedForNextRaid = ArenaSession.IsActive = false;
        ArenaSession.ActiveGame = null; ArenaSession.ActiveWorld = null;
        ArenaSession.ArmedMap = ArenaSession.ArmedMode = ArenaSession.ActiveMode = null;
        ArenaServerClient.HasPendingArm = false; ArenaServerClient.PendingArmToken = null;
        PitFireTeamCompat.TeamArenaActive = false; PitFireTeamCompat.FrozenCompanionAccountIds = Array.Empty<string>();
        ArenaMode.Plugin.ModLogger.Info.Clear(); _original = 0;
        ReturnedTickets.Clear(); FailReturn = false;
    }
    private static void Install(Fixture f)
    {
        Chainloader.PluginInfos[ArenaLootingBotsContract.Guid] = new() { Instance = (BaseUnityPlugin)Activator.CreateInstance(f.Plugin), Metadata = f.Metadata };
        ArenaLootingBotsCompat.Initialize();
    }
    private static LocalGame World(bool arena = false)
    {
        var game = new LocalGame { GameWorld = new() { LocationId = "factory4_day" }, _raidSettings = new() { location = "factory4_day", playerSide = ESideType.Pmc, serverId = "normal" } };
        Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = game.GameWorld;
        if (arena)
        {
            ArenaSession.ArmedForNextRaid = true; ArenaSession.ArmedMap = "factory4_day"; ArenaSession.ArmedMode = "deathmatch";
            ArenaServerClient.HasPendingArm = true; ArenaServerClient.PendingArmToken = Token;
            game._raidSettings.serverId = Protocol.BuildNativeEndIdentity("native", Token);
        }
        return game;
    }
    private static BotOwner Bot(LocalGame game, string id = "bot", string account = "enemy")
    {
        var bot = new BotOwner { BotsController = game.BotsController, Profile = new() { AccountId = account }, GetPlayer = new() { ProfileId = id } };
        bot.GetPlayer.AIData.BotOwner = bot; game.GameWorld.Players[id] = bot.GetPlayer; return bot;
    }
    private static object Instance(Type type, BotOwner bot)
    {
        object value = Activator.CreateInstance(type);
        if (value is DrakiaXYZ.BigBrain.Brains.CustomLayer layer) layer.BotOwner = bot;
        type.GetField("BotOwner")?.SetValue(value, bot); type.GetField("_botOwner")?.SetValue(value, bot);
        type.GetField("_botInventoryController")?.SetValue(value, bot.GetPlayer.InventoryController);
        if (value is UnityEngine.Component component) component.gameObject = bot.GetPlayer.gameObject;
        return value;
    }
    // Calls the installed production prefix, then the fixture original only
    // when admitted. This is not a claim about Mono's detour implementation.
    private static object Call(object instance, string method, params object[] args)
    {
        var target = instance.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        bool run = true; object result = target.ReturnType == typeof(bool) ? false : null;
        foreach (var patch in Harmony.GetPatchInfo(target)?.Prefixes.ToArray() ?? [])
        {
            if (patch.PatchMethod == null) continue;
            var parameters = patch.PatchMethod.GetParameters();
            object[] input = parameters.Select(p => p.Name == "__instance" ? instance : p.Name == "__0" ? args[0] : result).ToArray();
            if (!(bool)patch.PatchMethod.Invoke(null, input)) run = false;
            int output = Array.FindIndex(parameters, p => p.Name == "__result");
            if (output >= 0) result = input[output];
        }
        return run ? target.Invoke(instance, args) : result;
    }
    private static void Path(Fixture f, BotOwner bot, bool suppressed, string label, bool? cleanupSuppressed = null)
    {
        int before = _original;
        Need((bool)Call(Instance(f.Layer, bot), "IsActive") == !suppressed, label + " layer");
        int tickets = ReturnedTickets.Count;
        Call(Instance(f.Finder, bot), "BeginSearch", 7);
        Need(ReturnedTickets.Count - tickets == (suppressed ? 1 : 0)
            && (!suppressed || ReturnedTickets.Last() == 7), label + " acquired scan ticket returned exactly once");
        Call(Instance(f.Finder, bot), "EnqueuePriorityCorpse", "dead-bot");
        Call(Instance(f.Finder, bot), "OnAirdropLanded", new EFT.Interactive.LootableContainer());
        Call(Instance(f.Brain, bot), "StartLooting");
        int expected = suppressed ? 0 : 5;
        if (f.Inventory.GetMethod("CleanupInventoryAsync") != null)
        {
            var task = (Task)Call(Instance(f.Inventory, bot), "CleanupInventoryAsync", CancellationToken.None);
            Need(task != null && task.IsCompletedSuccessfully, label + " cleanup task completes");
            if (!(cleanupSuppressed ?? suppressed)) expected++;
        }
        Need(_original - before == expected, label + " mutation entries");
    }
    private static void ScopeCases(Fixture f)
    {
        Reset(); Install(f); Need(ArenaLootingBotsCompat.GetRejectionReason() == null, "valid contract admitted");
        var game = World(); var bot = Bot(game); Path(f, bot, false, "ordinary PMC");
        game._raidSettings.playerSide = ESideType.Savage; Path(f, bot, false, "ordinary Scav");
        Singleton<AbstractGame>.Instance = new HideoutGame(); Path(f, bot, false, "hideout");
        game = World(true); bot = Bot(game); Path(f, bot, true, "confirmed loading before Active");
        Need(PitFireTeamCompat.RosterReads == 0, "ordinary and DM skip allocated PFT roster getter");
        Path(f, bot, true, "repeated loading"); Need(ArenaMode.Plugin.ModLogger.Info.Count == 1, "one player notice per world");
        game.Status = GameStatus.Starting; Path(f, bot, true, "confirmed native Starting");
        game.Status = GameStatus.Started; Path(f, bot, true, "confirmed native Started"); game.Status = GameStatus.Running;
        game._raidSettings.serverId = Protocol.BuildNativeStartIdentity(Token); Path(f, bot, false, "request only marker");
        game._raidSettings.serverId = Protocol.BuildNativeEndIdentity("native", Token);
        ArenaServerClient.HasPendingArm = false; Path(f, bot, false, "unconfirmed selection"); ArenaServerClient.HasPendingArm = true;
        ArenaServerClient.PendingArmToken = "other"; Path(f, bot, false, "malformed arm token"); ArenaServerClient.PendingArmToken = Token;
        ArenaSession.ArmedMap = "bigmap"; Path(f, bot, false, "armed map mismatch"); ArenaSession.ArmedMap = "factory4_day";
        game._raidSettings.playerSide = ESideType.Savage; Path(f, bot, false, "stale arm Scav"); game._raidSettings.playerSide = ESideType.Pmc;
        game._raidSettings.location = "bigmap"; Path(f, bot, false, "settings map mismatch"); game._raidSettings.location = "factory4_day";
        game.Status = GameStatus.Stopped; Path(f, bot, false, "stopped game"); game.Status = GameStatus.Running;
        Singleton<GameWorld>.Instance = new() { LocationId = "factory4_day", Players = new() { ["bot"] = bot.GetPlayer } };
        Path(f, bot, false, "foreign same-map world"); Singleton<GameWorld>.Instance = game.GameWorld;
        bot.BotsController = new(); Path(f, bot, false, "foreign controller"); bot.BotsController = game.BotsController;
        bot.GetPlayer.AIData.BotOwner = new(); Path(f, bot, false, "foreign body owner"); bot.GetPlayer.AIData.BotOwner = bot;
        game.GameWorld.Players.Remove("bot"); Path(f, bot, false, "unregistered body"); game.GameWorld.Players["bot"] = bot.GetPlayer;
        bot.GetPlayer.IsAI = false; Path(f, bot, false, "human body"); bot.GetPlayer.IsAI = true;
        var foreignComponent = (UnityEngine.Component)Instance(f.Brain, bot); foreignComponent.gameObject = new();
        int before = _original; Call(foreignComponent, "StartLooting"); Need(_original == before + 1, "foreign component object preserved");
        if (f.Inventory.GetMethod("CleanupInventoryAsync") != null)
        {
            var inventory = Instance(f.Inventory, bot); f.Inventory.GetField("_botInventoryController").SetValue(inventory, new EFT.InventoryLogic.InventoryController());
            before = _original; Call(inventory, "CleanupInventoryAsync", CancellationToken.None); Need(_original == before + 1, "foreign native inventory preserved");
        }
        PitFireTeamCompat.TeamArenaActive = true; PitFireTeamCompat.FrozenCompanionAccountIds = [Companion];
        bot.Profile.AccountId = Companion; ArenaSession.ArmedMode = "tactical"; Path(f, bot, false, "PFT loading companion", cleanupSuppressed: true);
        ArenaSession.ArmedMode = "zombie"; Path(f, bot, false, "PFT zombie companion", cleanupSuppressed: true);
        bot.Profile.AccountId = "enemy"; Path(f, bot, true, "PFT enemy");
        bot.Profile.AccountId = Companion; ArenaSession.ArmedMode = "teamdeathmatch";
        int rosterReads = PitFireTeamCompat.RosterReads; Path(f, bot, true, "TDM both sides despite stale companion id");
        Need(PitFireTeamCompat.RosterReads == rosterReads, "TDM skips allocated PFT roster getter");
        PitFireTeamCompat.TeamArenaActive = false; ArenaSession.ArmedMode = "tactical"; Path(f, bot, true, "historical roster not commandable");
        ArenaSession.IsActive = true; ArenaSession.ActiveGame = game; ArenaSession.ActiveWorld = game.GameWorld; ArenaSession.ActiveMode = "deathmatch";
        ArenaSession.ArmedForNextRaid = false; ArenaServerClient.HasPendingArm = false; ArenaServerClient.PendingArmToken = null;
        Path(f, bot, true, "active after token consumed");
        var replacement = Bot(game, "bot"); Path(f, replacement, true, "replacement body"); Path(f, bot, false, "retired same-profile body");
        ArenaSession.ActiveMode = "tactical"; PitFireTeamCompat.TeamArenaActive = true;
        replacement.Profile.AccountId = Companion; Path(f, replacement, false, "PFT replacement before roster rebind", cleanupSuppressed: true);
        game = World(); bot = Bot(game); Path(f, bot, false, "next ordinary same map stale state");
        ArenaLootingBotsCompat.Shutdown(); game = World(true); bot = Bot(game); Path(f, bot, false, "shutdown removes guards");
    }
    private static void Contracts()
    {
        foreach (var f in new[] { Make("1.7.0"), Make("1.8.1"), Make(spawn: true), Make(cleanup: true), Make(guid: "other"),
            Make(assemblyName: "foreign"), Make(assemblyVersion: "1.8.2"), Make(badOwner: true), Make(badSearch: true), Make(wrongBase: true),
            Make("1.8.1", true, true, missingInit: true), Make(missingQueue: true), Make(badReturn: true), Make(asynchronousStart: true) })
        {
            Reset(); Install(f); Need(ArenaLootingBotsCompat.GetRejectionReason() != null, "malformed contract rejects Arena");
            Need(Harmony.Bound.Values.All(p => p.Prefixes.Count == 0), "malformed contract no surviving hooks");
        }
        Reset(); Need(ArenaLootingBotsCompat.GetRejectionReason() == null, "absent optional plugin");
        var good = Make(); Install(good); Need(ArenaLootingBotsCompat.GetRejectionReason() == null, "late optional plugin resolved");
        ArenaLootingBotsCompat.Initialize(); Need(Harmony.Bound.Values.All(p => p.Prefixes.Count == 1), "idempotent hooks");
        Harmony.Bound[good.Brain.GetMethod("StartLooting")].Prefixes.Clear();
        Need(ArenaLootingBotsCompat.GetRejectionReason() != null, "removed hook closes admission");
        Reset(); Harmony.FailAfter = 1; Install(good); Need(ArenaLootingBotsCompat.GetRejectionReason() != null, "partial patch failure rejects Arena");
        Need(Harmony.Bound.Values.All(p => p.Prefixes.Count == 0), "partial patch failure rollback");
        Reset(); AccessTools.MissingSettings = true; Install(good); Need(ArenaLootingBotsCompat.GetRejectionReason() != null, "native field drift refuses Arena");
        Reset(); Install(good); var game = World(); var bot = Bot(game); LocalGameAccessor.FailRead = true;
        Path(good, bot, false, "identity exception ordinary original"); Need(ArenaLootingBotsCompat.GetRejectionReason() != null, "runtime fault closes next Arena");
        Reset(); Install(good); game = World(true); bot = Bot(game);
        int before = _original; Call(Instance(good.Finder, bot), "BeginSearch", 0);
        Need(_original == before && ReturnedTickets.SequenceEqual(new[] { 0 }), "unlimited scan ticket returned without invented ticket");
        FailReturn = true; before = _original; Call(Instance(good.Finder, bot), "BeginSearch", 19);
        Need(_original == before + 1, "scheduler fault leaves original responsible for acquired ticket");
        Need(ArenaLootingBotsCompat.GetRejectionReason() != null, "scheduler fault closes next Arena");
        Reset(); Install(good); Harmony.Bound[good.Layer.GetMethod("IsActive")].Prefixes.Add(new() { owner = "other" });
        ArenaLootingBotsCompat.Shutdown(); Need(Harmony.Bound.Values.Sum(p => p.Prefixes.Count) == 1, "shutdown keeps foreign patches");
        Reset(); Install(good); Harmony.FailUnpatch = true; ArenaLootingBotsCompat.Shutdown(); game = World(true); bot = Bot(game);
        Path(good, bot, false, "failed unpatch leaves guards inert"); Reset();
    }
    public static void Main(string[] args)
    {
        ScopeCases(Make()); ScopeCases(Make("1.8.1", true, true)); ScopeCases(Make("1.9.0", true, true)); Contracts();
        foreach (var path in args) PublishedMetadata.Verify(path, Need);
        Console.WriteLine($"LOOTING_BOTS_CHECKS_PASSED ({_checks})");
    }
}
