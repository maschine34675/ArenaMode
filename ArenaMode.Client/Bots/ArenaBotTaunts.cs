#nullable disable
using ArenaMode.Compat;
using ArenaMode.Diagnostics;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Makes Normal eligible at one native combat-taunt decision, for a live Arena
/// PMC only. Neither shared presets nor the cached CanTaunt field are written.
/// SAIN retains timing, chance, enemy checks, speaker admission and hearing.
/// </summary>
internal static class ArenaBotTaunts
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".BotTaunts");
    private static bool _ready, _warned;
    private static Type _botType;
    private static FieldInfo _canTaunt, _mind, _botTaunts;
    private static PropertyInfo _sourceBot, _owner, _player, _info, _personality, _settings,
        _talk, _enemyTalk;
    private static object _normal;
    private static WeakReference _loggedWorld;
    private static ConditionalWeakTable<object, object> _observed = new();
    private static int _diagnosticBodies;

    internal static void Enable()
    {
        if (_ready) return;
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SAIN");
        if (assembly != null) TryBind(assembly);
    }

    internal static bool TryBind(Assembly assembly)
    {
        if (_ready) return true;
        try
        {
            ArenaSainVersion.Require(assembly);
            _botType = assembly.GetType("SAIN.Components.BotComponent", true);
            var enemyTalk = assembly.GetType("SAIN.SAINComponent.Classes.Talk.EnemyTalk", true);
            var info = assembly.GetType("SAIN.SAINComponent.Classes.Info.SAINBotInfoClass", true);
            // Preset types live in SAIN.Preset.Shared.dll, not SAIN.dll.
            _personality = Property(info, "Personality");
            var personality = _personality.PropertyType;
            if (personality.FullName != "SAIN.Preset.Shared.Models.Preset.Personalities.EPersonality"
                || !personality.IsEnum || !Enum.IsDefined(personality, "Normal"))
                throw new InvalidOperationException("unsupported SAIN Normal personality");
            _normal = Enum.Parse(personality, "Normal");
            _sourceBot = Property(enemyTalk, "Bot", _botType);
            _owner = Property(_botType, "BotOwner", typeof(BotOwner));
            _player = Property(_botType, "Player", typeof(Player));
            _info = Property(_botType, "Info", info);
            _settings = Property(info, "FileSettings");
            _mind = Field(_settings.PropertyType, "Mind");
            _botTaunts = Field(_mind.FieldType, "BotTaunts", typeof(bool));
            _talk = Property(_botType, "Talk");
            _enemyTalk = Property(_talk.PropertyType, "EnemyTalk", enemyTalk);
            _canTaunt = Field(enemyTalk, "CanTaunt", typeof(bool));
            var update = enemyTalk.GetMethod("ManualUpdate", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null);
            if (update?.ReturnType != typeof(void)) throw new InvalidOperationException("unsupported SAIN EnemyTalk.ManualUpdate");
            Patches.Patch(update, transpiler: new HarmonyMethod(typeof(ArenaBotTaunts), nameof(Transpile)));
            _ready = true;
            Plugin.ModLogger.LogDebug($"Arena Normal combat taunts ready (SAIN {assembly.GetName().Version}); native conditions retained.");
            return true;
        }
        catch (Exception ex)
        {
            Shutdown();
            Warn(ex);
            return false;
        }
    }

    private static PropertyInfo Property(Type type, string name, Type expected = null)
    {
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetMethod == null || property.GetIndexParameters().Length != 0
            || (expected != null && property.PropertyType != expected))
            throw new InvalidOperationException("unsupported SAIN property " + type.Name + "." + name);
        return property;
    }

    private static FieldInfo Field(Type type, string name, Type expected = null)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (field == null || (expected != null && field.FieldType != expected))
            throw new InvalidOperationException("unsupported SAIN field " + type.Name + "." + name);
        return field;
    }

    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var sites = code.Select((instruction, index) => new { instruction, index })
            .Where(site => site.instruction.opcode == OpCodes.Ldfld && Equals(site.instruction.operand, _canTaunt)).ToArray();
        if (sites.Length != 1 || sites[0].index == 0 || sites[0].index + 1 >= code.Count
            || code[sites[0].index - 1].opcode != OpCodes.Ldarg_0
            || (code[sites[0].index + 1].opcode != OpCodes.Brfalse && code[sites[0].index + 1].opcode != OpCodes.Brfalse_S))
            throw new InvalidOperationException("SAIN combat CanTaunt operand/branch changed");
        // Keep the field read and every native branch/label. Only substitute its
        // boolean operand; enemyKilled and preset initialization remain native.
        code.InsertRange(sites[0].index + 1, new[]
        {
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Call, typeof(ArenaBotTaunts).GetMethod(nameof(Eligibility), BindingFlags.NonPublic | BindingFlags.Static))
        });
        return code;
    }

    private static bool Live(IArenaMode mode) => mode != null && !mode.IsComplete && (mode switch
    {
        RoundsMode rounds => rounds.AssistScoringOpen,
        TacticalMode tactical => tactical.AssistScoringOpen,
        TeamDeathmatchMode teams => teams.AssistScoringOpen,
        GunGameMode gunGame => gunGame.AssistScoringOpen,
        _ => false
    });

    private static bool Eligibility(bool native, object source)
    {
        if (native || !_ready) return native;
        try
        {
            if (!ArenaSession.IsActive || ArenaRespawnDriver.StandDown || Time.timeScale <= 0f
                || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated
                || !Live(ArenaMatchService.Current?.Mode) || ArenaMatchService.TeamStartAbortPending
                || Areas.ArenaAreaRuntime.IsPending) return native;
            var world = Singleton<GameWorld>.Instance;
            var game = Singleton<AbstractGame>.Instance as LocalGame;
            if (world == null || game == null || game.Status != GameStatus.Started
                || !ArenaSession.IsExactActiveRaid(game, world)) return native;
            var bot = source == null ? null : _sourceBot.GetValue(source);
            var owner = bot == null ? null : _owner.GetValue(bot) as BotOwner;
            var player = bot == null ? null : _player.GetValue(bot) as Player;
            if (owner == null || owner.BotState != EBotState.Active || player == null || !player.IsAI
                || player.IsYourPlayer || ReferenceEquals(world.MainPlayer, player) || player.HealthController?.IsAlive != true
                || !ReferenceEquals(owner.GetPlayer, player) || !ReferenceEquals(player.AIData?.BotOwner, owner)
                || !ReferenceEquals(owner.GetComponent(_botType), bot) || string.IsNullOrEmpty(player.ProfileId)
                || !ReferenceEquals(world.GetAlivePlayerByProfileID(player.ProfileId), player)
                || !ReferenceEquals(world.GetEverExistedPlayerByID(player.ProfileId), player)
                || owner.IsFollower() || owner.BotFollower?.HaveBoss == true || PitFireTeamCompat.IsTeamPlayer(player)) return native;
            var role = player.Profile?.Info?.Settings?.Role;
            if (role != WildSpawnType.pmcUSEC && role != WildSpawnType.pmcBEAR) return native;
            var talk = _talk.GetValue(bot);
            var info = _info.GetValue(bot);
            if (talk == null || !ReferenceEquals(_enemyTalk.GetValue(talk), source) || info == null
                || !_normal.Equals(_personality.GetValue(info))) return native;
            var settings = _settings.GetValue(info);
            var mind = settings == null ? null : _mind.GetValue(settings);
            if (mind == null || !(_botTaunts.GetValue(mind) is bool allowed) || !allowed) return native;
            Observe(world, source, player);
            return true;
        }
        catch (Exception ex) { Warn(ex); return native; }
    }

    private static void Observe(GameWorld world, object source, Player player)
    {
        if (!ArenaLog.DeveloperEnabled) return;
        if (!ReferenceEquals(_loggedWorld?.Target, world))
        {
            _loggedWorld = new WeakReference(world);
            _observed = new ConditionalWeakTable<object, object>();
            _diagnosticBodies = 0;
        }
        if (_diagnosticBodies >= 3 || _observed.TryGetValue(source, out _)) return;
        _observed.Add(source, new object());
        _diagnosticBodies++;
        ArenaLog.Developer($"Arena SAIN Normal combat taunt eligible: profile={player.ProfileId}, native=False, applied=True, mind.BotTaunts=True. Native cooldown/chance/enemy/CanTalk retained.");
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena Normal combat taunts unavailable; native eligibility retained: " + ex.GetBaseException().Message);
    }

    internal static void Shutdown()
    {
        _ready = false;
        Patches.UnpatchSelf();
        _botType = null;
        _canTaunt = _mind = _botTaunts = null;
        _sourceBot = _owner = _player = _info = _personality = _settings = _talk = _enemyTalk = null;
        _normal = null;
        _loggedWorld = null;
        _observed = new ConditionalWeakTable<object, object>();
        _diagnosticBodies = 0;
    }
}
