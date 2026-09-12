using System.Reflection;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;

namespace ArenaMode.Tests;

/// <summary>
/// The pool generator's one promise that is not about weapons: a broken
/// database must not take the server down. The exclusion index is the first
/// thing at load that forces the English locale, and the first version of it
/// ran outside the fail-open blocks, so a locale that threw would have stopped
/// the server where the old code logged and fell back to the shipped pool.
/// </summary>
[Collection(ArenaContextSerialCollection.Name)]
public sealed class LadderGeneratorTests
{
    [Fact]
    public async Task AFaultyLocaleLeavesTheShippedPoolAndTheServerUp()
    {
        var ladderBefore = ArenaContext.LadderJson;
        var configBefore = ArenaContext.Config;
        try
        {
            ArenaContext.LadderJson = "[{\"id\":\"shipped\"}]";
            ArenaContext.Config = new ArenaModeConfig();

            // One template, so the index has a name to ask for, and a locale
            // whose lazy load throws the moment it is asked.
            var tpl = new MongoId("5e85aac65505fa48730d8af2");
            var templates = new TemplateTable
            {
                Items = new Dictionary<MongoId, TemplateItem>
                {
                    [tpl] = new TemplateItem { Id = tpl, Name = "patron_23x75_cheremukha_7m", Parent = new MongoId("5485a8684bdc2da71d8b4567") },
                },
                Character = null!, CustomisationStorage = null!, Prestige = null!, Quests = null!,
                RepeatableQuests = null!, Handbook = null!, Customization = null!, Dialogue = null!,
                Profiles = null!, Prices = null!, DefaultEquipmentPresets = null!, Achievements = null!,
                CustomAchievements = null!, LocationServices = null!,
            };
            var locales = new LocaleTable
            {
                Global = new Dictionary<string, LazyLoad<GlobalLocaleDictionary>>
                {
                    ["en"] = new LazyLoad<GlobalLocaleDictionary>(() => throw new InvalidOperationException("corrupt locale")),
                },
                Menu = new Dictionary<string, Dictionary<string, object>>(),
                Languages = new Dictionary<string, string>(),
            };
            var globals = (GlobalTable)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GlobalTable));
            var logger = QuietLogger.Create<LadderGenerator>();

            var generator = new LadderGenerator(templates, globals, locales, logger);

            // Must not throw: SPT awaits every IOnLoad without a catch of its
            // own, and an exception here is a server that does not start.
            await generator.OnLoadAsync();

            Assert.Equal("[{\"id\":\"shipped\"}]", ArenaContext.LadderJson);
            Assert.Contains(QuietLogger.Lines, line => line.Contains("Exclusion index failed", StringComparison.Ordinal));
        }
        finally
        {
            ArenaContext.LadderJson = ladderBefore;
            ArenaContext.Config = configBefore;
            ArenaContext.WeaponVariants = null;
        }
    }

    /// <summary>A logger that keeps what it was told and otherwise does nothing.</summary>
    public class QuietLogger : DispatchProxy
    {
        public static readonly List<string> Lines = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (args is { Length: > 0 } && args[0] is string text)
                Lines.Add(text);

            return targetMethod?.ReturnType == typeof(void)
                ? null
                : targetMethod?.ReturnType.IsValueType == true
                    ? Activator.CreateInstance(targetMethod.ReturnType)
                    : null;
        }

        public static ISptLogger<T> Create<T>() => DispatchProxy.Create<ISptLogger<T>, QuietLogger>();
    }
}
