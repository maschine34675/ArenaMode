using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using HarmonyLib;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public sealed class TeamDeathmatchArmbandTests : IDisposable
{
    private static readonly MongoId Session = new("0123456789abcdef01234567");
    private static readonly MongoId OtherSession = new("89abcdef0123456701234567");
    private static readonly MongoId Root = new("aaaaaaaaaaaaaaaaaaaaaaaa");
    private readonly ArenaSessionService? _oldSessions = ArenaContext.Sessions;
    private readonly ArenaModeConfig _oldConfig = ArenaContext.Config;

    public TeamDeathmatchArmbandTests()
    {
        ArenaContext.Config = new ArenaModeConfig { RaidTimeMinutes = 1 };
        ArenaContext.Sessions = new ArenaSessionService(BotTierScalePatchTests.TestLogger.Create<ArenaSessionService>());
    }

    public void Dispose()
    {
        ArenaContext.Sessions = _oldSessions;
        ArenaContext.Config = _oldConfig;
    }

    [Theory]
    [InlineData("Usec", "5b3f3af486f774679e752c1f")]
    [InlineData("Bear", "5b3f3ade86f7746b6b790d8e")]
    public void FinalGeneratedBandOverridesApbsWithoutChangingSharedGear(string side, string expected)
    {
        Start();
        var details = Details(side);
        ArenaTeamDeathmatchArmbandPatch.Prefix(Session, details, out var state);
        // APBS can produce any band after our prefix; the final result wins.
        var source = Bot(side, band: "5b3f16c486f7747c327f55f7");
        var before = JsonSerializer.Serialize(source);
        var output = source;
        ArenaTeamDeathmatchArmbandPatch.Postfix(Session, details, state, ref output);

        Assert.Equal(expected, Band(output).Template.ToString());
        Assert.Equal(before, JsonSerializer.Serialize(source));
        Assert.NotSame(source, output);
        Assert.NotSame(source.Inventory, output.Inventory);
        Assert.NotSame(source.Inventory!.Items, output.Inventory!.Items);
        Assert.Same(source.Info, output.Info);
        Assert.Same(source.Inventory.Items![1], output.Inventory.Items![1]);
        Assert.Equal(source.Id, output.Id);
        Assert.Equal(Root, output.Inventory.Equipment);
        Assert.Null(Band(output).Upd);
        Assert.Null(Band(output).Location);
    }

    [Theory]
    [InlineData("Usec", TeamDeathmatchArmbandPolicy.UsecBlue)]
    [InlineData("Bear", TeamDeathmatchArmbandPolicy.BearRed)]
    public void MissingBandIsAddedOnceUnderTheEquipmentRoot(string side, string expected)
    {
        Start();
        var source = Bot(side);
        var result = Generate(source, Details(side));
        Assert.Equal(source.Inventory!.Items!.Count + 1, result.Inventory!.Items!.Count);
        Assert.Equal(expected, Band(result).Template.ToString());
        Assert.Equal(Root.ToString(), Band(result).ParentId);
        Assert.False(Band(result).Id.IsEmpty);
        Assert.Same(result, Generate(result, Details(side)));
    }

    [Fact]
    public void ModdedArmbandContainersLoseOnlyTheirOwnSubtreeAndDuplicates()
    {
        Start();
        var bot = Bot("Usec", "5b3f16c486f7747c327f55f7");
        var band = Band(bot);
        var child = Item("cccccccccccccccccccccccc", "mod", band.Id.ToString());
        var grandchild = Item("dddddddddddddddddddddddd", "slot", child.Id.ToString());
        var duplicate = Item("eeeeeeeeeeeeeeeeeeeeeeee", "ArmBand", Root.ToString());
        bot.Inventory!.Items!.AddRange([grandchild, child, duplicate]);
        var original = JsonSerializer.Serialize(bot);

        var result = Generate(bot, Details());
        Assert.Equal(3, result.Inventory!.Items!.Count);
        Assert.Equal(TeamDeathmatchArmbandPolicy.UsecBlue, Band(result).Template.ToString());
        Assert.Same(bot.Inventory.Items[1], result.Inventory.Items[1]);
        Assert.Equal(original, JsonSerializer.Serialize(bot));
    }

    [Theory]
    [InlineData("endless", false)]
    [InlineData("rounds", false)]
    [InlineData("gungame", false)]
    [InlineData("tactical", false)]
    [InlineData("tactical", true)]
    [InlineData("teamdeathmatch", true)]
    public void OtherModesAndProtectedPftLeaveEquipmentUntouched(string mode, bool protect)
    {
        Start(mode, protect);
        var bot = Bot();
        Assert.Same(bot, Generate(bot, Details()));
    }

    [Fact]
    public void AnotherSessionOnSameMapOrExactSessionOnWrongMapDoesNotInheritPolicy()
    {
        Start();
        var bot = Bot();
        Assert.Same(bot, Generate(bot, Details(), OtherSession));
        var details = Details();
        details.Location = "laboratory";
        Assert.Same(bot, Generate(bot, details));
        details.Location = null;
        Assert.Same(bot, Generate(bot, details));
    }

    [Fact]
    public void OrdinaryArmedAndExpiredSessionsLeaveEquipmentUntouched()
    {
        var bot = Bot();
        Assert.Same(bot, Generate(bot, Details()));
        ArenaContext.Sessions!.Arm(Session, "factory4_day", "teamdeathmatch");
        Assert.Same(bot, Generate(bot, Details()));
        Start();
        var entries = (IDictionary)typeof(ArenaSessionService).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ArenaContext.Sessions)!;
        var entry = entries[Session.ToString()]!;
        entry.GetType().GetField("InRaidAtUtc")!.SetValue(entry, DateTime.UtcNow.AddMinutes(-17));
        Assert.Same(bot, Generate(bot, Details()));
    }

    [Fact]
    public void OldGenerationCannotAdoptNewRaidIdentity()
    {
        Start();
        var details = Details();
        var bot = Bot();
        var original = bot;
        ArenaTeamDeathmatchArmbandPatch.Prefix(Session, details, out var state);
        Assert.NotNull(state);
        Start();
        ArenaTeamDeathmatchArmbandPatch.Postfix(Session, details, state, ref bot);
        Assert.Same(original, bot);
        Assert.NotSame(original, Generate(original, details));
    }

    [Fact]
    public void GenerationStartedOutsideArenaCannotAdoptLaterTeamDm()
    {
        var details = Details();
        var bot = Bot();
        var original = bot;
        ArenaTeamDeathmatchArmbandPatch.Prefix(Session, details, out var state);
        Assert.Null(state);
        Start();
        ArenaTeamDeathmatchArmbandPatch.Postfix(Session, details, state, ref bot);
        Assert.Same(original, bot);
    }

    [Theory]
    [InlineData("playerScav")]
    [InlineData("notPmc")]
    [InlineData("event")]
    [InlineData("assault")]
    [InlineData("wrongSide")]
    [InlineData("resultWrongRole")]
    [InlineData("resultWrongSide")]
    [InlineData("localProfile")]
    public void NonPmcPlayerOrMismatchedIdentityRemainsUnchanged(string variant)
    {
        Start();
        var details = Details();
        var bot = Bot();
        switch (variant)
        {
            case "playerScav": details.IsPlayerScav = true; break;
            case "notPmc": details.IsPmc = false; break;
            case "event": details.EventRole = "pmcUSEC"; break;
            case "assault": details.Role = "assault"; break;
            case "wrongSide": details.Side = "Bear"; break;
            case "resultWrongRole": bot.Info!.Settings!.Role = "pmcBEAR"; break;
            case "resultWrongSide": bot.Info!.Side = "Savage"; break;
            case "localProfile": bot.Id = Session; break;
        }
        Assert.Same(bot, Generate(bot, details));
    }

    [Theory]
    [InlineData("missingEquipment")]
    [InlineData("emptyEquipment")]
    [InlineData("missingRoot")]
    [InlineData("duplicateId")]
    [InlineData("missingInventory")]
    [InlineData("emptyBotId")]
    public void IncompleteGenerationDoesNotInventOrDamageInventory(string variant)
    {
        Start();
        var bot = Bot();
        switch (variant)
        {
            case "missingEquipment": bot.Inventory!.Equipment = null; break;
            case "emptyEquipment": bot.Inventory!.Equipment = default(MongoId); break;
            case "missingRoot": bot.Inventory!.Items!.RemoveAt(0); break;
            case "duplicateId": bot.Inventory!.Items!.Add(bot.Inventory.Items[0]); break;
            case "missingInventory": bot.Inventory = null; break;
            case "emptyBotId": bot.Id = default(MongoId); break;
        }
        Assert.Same(bot, Generate(bot, Details()));
    }

    [Fact]
    public void PatchTargetsExactSynchronousOuterGeneratorAndUsesPairedState()
    {
        var patch = new ArenaTeamDeathmatchArmbandPatch();
        var target = (MethodInfo)typeof(ArenaTeamDeathmatchArmbandPatch)
            .GetMethod("GetTargetMethod", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(patch, null)!;
        Assert.Equal(typeof(BotGenerator), target.DeclaringType);
        Assert.Equal("PrepareAndGenerateBot", target.Name);
        Assert.Equal(typeof(BotBase), target.ReturnType);
        Assert.Null(target.GetCustomAttribute<AsyncStateMachineAttribute>());
        Assert.Equal([typeof(MongoId), typeof(BotGenerationDetails)],
            target.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.NotNull(typeof(ArenaTeamDeathmatchArmbandPatch).GetMethod("Prefix")!.GetCustomAttribute<PatchPrefixAttribute>());
        Assert.NotNull(typeof(ArenaTeamDeathmatchArmbandPatch).GetMethod("Postfix")!.GetCustomAttribute<PatchPostfixAttribute>());
        // Compile the actual Harmony wrapper in this isolated test process.
        // AbstractPatch.Enable deliberately ignores other-assembly callers;
        // production registration is performed by the owning server assembly.
        var harmony = new Harmony("ArenaMode.Tests.TeamArmbandContract");
        try
        {
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(ArenaTeamDeathmatchArmbandPatch).GetMethod("Prefix")),
                postfix: new HarmonyMethod(typeof(ArenaTeamDeathmatchArmbandPatch).GetMethod("Postfix")));
            Assert.Contains(harmony.Id, Harmony.GetPatchInfo(target)!.Owners);
        }
        finally { harmony.UnpatchSelf(); }
    }

    private static BotBase Generate(BotBase bot, BotGenerationDetails details, MongoId? session = null)
    {
        ArenaTeamDeathmatchArmbandPatch.Prefix(session ?? Session, details, out var state);
        ArenaTeamDeathmatchArmbandPatch.Postfix(session ?? Session, details, state, ref bot);
        return bot;
    }

    private static void Start(string mode = "teamdeathmatch", bool protect = false)
    {
        string token = ArenaContext.Sessions!.Arm(Session, "factory4_day", mode, protect);
        Assert.Equal(ArenaLocalRaidStartDisposition.ArenaStarted,
            ArenaContext.Sessions.ProcessLocalRaidStart(Session, "factory4_day", Protocol.BuildNativeStartIdentity(token)));
    }

    private static BotGenerationDetails Details(string side = "Usec") => new()
    {
        IsPmc = true, Role = side == "Usec" ? "pmcUSEC" : "pmcBEAR", Side = side, Location = "factory4_day",
    };

    private static BotBase Bot(string side = "Usec", string? band = null)
    {
        var bot = new BotBase
        {
            Id = new MongoId("bbbbbbbbbbbbbbbbbbbbbbbb"),
            Info = new Info { Side = side, Settings = new BotInfoSettings { Role = side == "Usec" ? "pmcUSEC" : "pmcBEAR" } },
            Inventory = new BotBaseInventory
            {
                Equipment = Root,
                Items = [Item(Root.ToString(), null, null), Item("111111111111111111111111", "FirstPrimaryWeapon", Root.ToString())],
            },
        };
        if (band != null)
            bot.Inventory.Items.Add(Item("222222222222222222222222", "ArmBand", Root.ToString()) with
                { Template = new MongoId(band), Upd = new Upd { StackObjectsCount = 1 } });
        return bot;
    }

    private static Item Item(string id, string? slot, string? parent) => new()
    {
        Id = new MongoId(id), Template = new MongoId("333333333333333333333333"), SlotId = slot, ParentId = parent,
    };

    private static Item Band(BotBase bot)
        => Assert.Single(bot.Inventory!.Items!, item => item.SlotId == "ArmBand" && item.ParentId == Root.ToString());
}
