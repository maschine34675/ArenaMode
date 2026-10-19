using ArenaMode.Compat;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using EFT;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Shorten only the delay argument of SAIN's native search predicates for
/// adopted opponents in live independent TDM. All native predicates still run;
/// no shared Info, personality, forget timer, contact or movement state is written.
/// </summary>
internal static class TdmSearchTempo
{
    private const float MaximumDelay = 5f;
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".TdmSearchTempo");
    private static bool _ready, _failed;
    private static Type _botType;
    private static PropertyInfo _deciderBot, _enemyBot, _search, _decider, _owner,
        _player, _controller, _enemyPlayer, _enemyInfo;
    private sealed class Observation { internal float NextLog; }
    private static ConditionalWeakTable<BotOwner, Observation> _observations = new();

    internal static void Enable()
    {
        if (_ready) return;
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SAIN");
        if (assembly == null) return;
        try
        {
            Bind(assembly);
            _ready = true;
            Plugin.ModLogger.LogDebug($"Arena TDM: SAIN {assembly.GetName().Version} search delay argument capped at 5s for live adopted opponents; native decisions retained.");
        }
        catch (Exception ex)
        {
            Patches.UnpatchSelf();
            Plugin.ModLogger.LogWarning($"Arena TDM search tempo unavailable; native delays retained: {ex.Message}");
        }
    }

    private static PropertyInfo Property(Type type, string name, Type expected)
    {
        var property = type?.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property?.PropertyType != expected || property?.GetMethod == null
            || property.GetIndexParameters().Length != 0)
            throw new InvalidOperationException("unsupported SAIN property " + type?.Name + "." + name);
        return property;
    }

    private static void Bind(Assembly assembly)
    {
        ArenaSainVersion.Require(assembly);
        var bot = assembly.GetType("SAIN.Components.BotComponent", true);
        var search = assembly.GetType("SAIN.Classes.Bot.Search.SearchClass", true);
        var decider = assembly.GetType("SAIN.Classes.Bot.Search.SearchDecider", true);
        var enemy = assembly.GetType("SAIN.SAINComponent.Classes.EnemyClasses.Enemy", true);
        var controller = bot.GetProperty("EnemyController")?.PropertyType
            ?? throw new InvalidOperationException("unsupported SAIN enemy controller");
        var reason = assembly.GetType("SAIN.SAINComponent.Classes.Search.SearchReasonsStruct+EWantToSearchReason", true);
        if (!reason.IsEnum) throw new InvalidOperationException("unsupported SAIN search reason");
        _botType = bot;
        _deciderBot = Property(decider, "Bot", bot);
        _enemyBot = Property(enemy, "Bot", bot);
        _search = Property(bot, "Search", search);
        _decider = Property(search, "Decider", decider);
        _owner = Property(bot, "BotOwner", typeof(BotOwner));
        _player = Property(bot, "Player", typeof(Player));
        _controller = Property(bot, "EnemyController", controller);
        _enemyPlayer = Property(enemy, "EnemyPlayer", typeof(Player));
        _enemyInfo = Property(enemy, "EnemyInfo", typeof(EnemyInfo));
        var methods = new[] { "shallBeginSearch", "ShallContinueSearch" }.Select(name =>
        {
            var method = decider.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { enemy, typeof(float), reason.MakeByRefType() }, null);
            if (method == null || method.ReturnType != typeof(bool) || !method.GetParameters()[2].IsOut)
                throw new InvalidOperationException("unsupported SAIN predicate " + name);
            return method;
        }).ToArray();
        foreach (var method in methods)
            Patches.Patch(method, prefix: new HarmonyMethod(typeof(TdmSearchTempo), nameof(Prefix)));
    }

    private static bool Live(TeamDeathmatchMode mode)
        => mode != null && mode.IsSearchActive && !ArenaRespawnDriver.StandDown
            && !PitFireTeamCompat.HasFrozenTeamRoster && TeamDeathmatchRelations.Enabled
            && ReferenceEquals(ArenaMatchService.Current?.Mode, mode);

    private static void Prefix(object __instance, object __0, ref float __1, MethodBase __originalMethod)
    {
        if (!_ready || float.IsNaN(__1) || float.IsInfinity(__1) || __1 <= MaximumDelay) return;
        float incoming = __1;
        try
        {
            var mode = ArenaMatchService.Current?.Mode as TeamDeathmatchMode;
            if (!Live(mode)) return;
            var component = _deciderBot.GetValue(__instance);
            var source = component == null ? null : _owner.GetValue(component) as BotOwner;
            var target = __0 == null ? null : _enemyPlayer.GetValue(__0) as Player;
            if (source == null || source.BotState != EBotState.Active || source.GetPlayer?.IsAI != true
                || !mode.TryClassifyCurrentBody(source.GetPlayer, out bool sourceFriendly)
                || !mode.TryClassifyCurrentBody(target, out bool targetFriendly) || sourceFriendly == targetFriendly
                || !ReferenceEquals(_player.GetValue(component), source.GetPlayer)
                || !ReferenceEquals(source.GetComponent(_botType), component)
                || !ReferenceEquals(_enemyBot.GetValue(__0), component)) return;
            var search = _search.GetValue(component);
            if (search == null || !ReferenceEquals(_decider.GetValue(search), __instance)) return;
            var controller = _controller.GetValue(component);
            var native = _enemyInfo.GetValue(__0) as EnemyInfo;
            if (!TeamDeathmatchRelations.OwnsAwareness(source, target, controller, __0, native)
                || !Live(mode)
                || !mode.TryClassifyCurrentBody(source.GetPlayer, out sourceFriendly)
                || !mode.TryClassifyCurrentBody(target, out targetFriendly) || sourceFriendly == targetFriendly) return;

            float original = __1;
            __1 = MaximumDelay;
            // This is the supplied argument, not Info.TimeBeforeSearch (which is
            // intentionally untouched). SAIN divides it further when continuing.
            if (!ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled) return;
            var observation = _observations.GetValue(source, _ => new Observation());
            if (Time.time < observation.NextLog) return;
            observation.NextLog = Time.time + 30f;
            ArenaMode.Diagnostics.ArenaLog.Developer(FormattableString.Invariant($"Arena TDM search tempo: side={source.GetPlayer.Profile.Info.Side}, nativeArgument={original:0.0}s, appliedArgument={__1:0.0}s, predicate={__originalMethod?.Name}. Native search result and priorities retained."));
        }
        catch (Exception ex)
        {
            __1 = incoming;
            if (_failed) return;
            _failed = true;
            Plugin.ModLogger.LogWarning($"Arena TDM search tempo read failed (further messages silenced): {(ex.InnerException ?? ex).Message}");
        }
    }

    internal static void Shutdown()
    {
        _ready = false;
        Patches.UnpatchSelf();
        _observations = new ConditionalWeakTable<BotOwner, Observation>();
        _failed = false;
    }
}
