using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace ArenaMode.Bots;

/// <summary>
/// Optional SAIN 4.5.1 assignment adapter. Replace only passive assignments,
/// before SAIN derives difficulty/search settings. Never edit shared presets or
/// recalculate an existing follower's timers: PFT owns its forget-time override.
/// </summary>
internal static class ArenaSainPersonalities
{
    private const string HarmonyId = Plugin.PluginGuid + ".SainPersonalities";
    private static readonly Harmony Patches = new(HarmonyId);
    private static PropertyInfo _owner, _player, _loadedPreset, _manager;
    private static FieldInfo _dictionary;
    private static object _normal;
    private static bool _ready, _warned;
    private static int _replacements;
    private static WeakReference _loggedWorld;

    internal static void Enable()
    {
        if (_ready) return;
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => candidate.GetName().Name == "SAIN");
        if (assembly == null) return; // Soft dependency: no SAIN installation required.
        TryBind(assembly);
    }

    internal static bool TryBind(Assembly assembly)
    {
        try
        {
            if (assembly?.GetName().Name != "SAIN"
                || assembly.GetName().Version != new System.Version(4, 5, 1, 0))
                throw new InvalidOperationException("expected SAIN 4.5.1");
            var info = assembly.GetType("SAIN.SAINComponent.Classes.Info.SAINBotInfoClass", true);
            var plugin = assembly.GetType("SAIN.SAINPlugin", true);
            var select = info.GetMethod("GetPersonality", BindingFlags.Instance | BindingFlags.Public);
            var personality = select?.ReturnType;
            var parameters = select?.GetParameters();
            if (personality?.IsEnum != true || parameters?.Length != 1 || !parameters[0].IsOut)
                throw new InvalidOperationException("personality selection contract changed");
            var settings = parameters[0].ParameterType.GetElementType();
            if (settings?.FullName != "SAIN.Preset.Shared.Personalities.BasePersonality.PersonalitySettingsClass"
                || personality.FullName != "SAIN.Preset.Shared.Models.Preset.Personalities.EPersonality")
                throw new InvalidOperationException("personality result/settings types changed");
            var setter = info.GetMethod("SetPersonality", new[] { personality });
            _owner = info.GetProperty("BotOwner");
            _player = info.GetProperty("Player");
            _loadedPreset = plugin.GetProperty("LoadedPreset", BindingFlags.Public | BindingFlags.Static);
            _manager = _loadedPreset?.PropertyType.GetProperty("PersonalityManager");
            _dictionary = _manager?.PropertyType.GetField("PersonalityDictionary");
            if (setter?.ReturnType != typeof(void) || _owner?.PropertyType != typeof(BotOwner)
                || _player?.PropertyType != typeof(Player) || _dictionary == null
                || !typeof(IDictionary).IsAssignableFrom(_dictionary.FieldType))
                throw new InvalidOperationException("personality setter, owner or preset contract changed");
            foreach (var name in new[] { "Rat", "Timmy", "Coward", "Normal" })
                if (!Enum.IsDefined(personality, name))
                    throw new InvalidOperationException("personality enum changed");
            _normal = Enum.Parse(personality, "Normal");

            // Close the generic hooks over SAIN's actual runtime types. Harmony
            // receives exact enum/out-parameter signatures without a hard DLL
            // reference or an unsafe boxed ref-object enum result.
            var selectionPatch = typeof(ArenaSainPersonalities).GetMethod(nameof(SelectionPostfix),
                BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(personality, settings);
            var setterPatch = typeof(ArenaSainPersonalities).GetMethod(nameof(SetterPrefix),
                BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(personality);
            Patches.Patch(select, postfix: new HarmonyMethod(selectionPatch) { priority = Priority.Last });
            Patches.Patch(setter, prefix: new HarmonyMethod(setterPatch) { priority = Priority.Last });
            _ready = true;
            Plugin.ModLogger.LogInfo("Arena: optional SAIN 4.5.1 personality filter ready (Rat, Timmy, Coward -> Normal).");
            return true;
        }
        catch (Exception exception)
        {
            Shutdown(); // No partially installed adapter on signature/patch drift.
            Warn("SAIN personality integration unavailable; native assignments retained", exception);
            return false;
        }
    }

    internal static void Shutdown()
    {
        _ready = false;
        Patches.UnpatchSelf();
        _normal = null;
        _owner = _player = _loadedPreset = _manager = null;
        _dictionary = null;
        _loggedWorld = null;
        _replacements = 0;
    }

    private static bool IsPassive(object personality)
        => personality?.ToString() is "Rat" or "Timmy" or "Coward";

    private static bool IsArenaBot(object info)
    {
        if (!_ready || Settings.ExcludePassiveSainPersonalities?.Value != true
            || (!ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid)
            || !Singleton<GameWorld>.Instantiated)
            return false;
        var world = Singleton<GameWorld>.Instance;
        if (string.IsNullOrEmpty(ArenaSession.ArmedMap)
            || !string.Equals(world.LocationId, ArenaSession.ArmedMap, StringComparison.OrdinalIgnoreCase)
            || (!ArenaSession.ArmedForNextRaid
                && (!Singleton<AbstractGame>.Instantiated
                    || !ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, world))))
            return false;
        var owner = _owner.GetValue(info) as BotOwner;
        var player = _player.GetValue(info) as Player;
        return owner != null && player != null && player.IsAI
            && ReferenceEquals(owner.GetPlayer, player)
            && ReferenceEquals(world.GetEverExistedPlayerByID(player.ProfileId), player);
    }

    private static object NormalSettings()
    {
        // Resolve the CURRENT preset every assignment, including SAIN preset
        // reloads. Only read the dictionary: no enable flags or shared values
        // are changed, even temporarily.
        var preset = _loadedPreset.GetValue(null);
        var manager = preset == null ? null : _manager.GetValue(preset);
        var dictionary = manager == null ? null : _dictionary.GetValue(manager) as IDictionary;
        return dictionary != null && dictionary.Contains(_normal) ? dictionary[_normal] : null;
    }

    private static void SelectionPostfix<TPersonality, TSettings>(object __instance,
        ref TSettings settings, ref TPersonality __result)
    {
        try
        {
            if (!IsPassive(__result) || !IsArenaBot(__instance)) return;
            if (!(NormalSettings() is TSettings normalSettings))
            {
                Warn("SAIN preset has no usable Normal personality; native assignment retained", null);
                return;
            }
            var normal = (TPersonality)_normal;
            // Both values belong to one assignment. The caller installs them
            // before ConfigureBot computes difficulty and search/hold timers.
            var previous = __result;
            settings = normalSettings;
            __result = normal;
            LogReplacement(previous);
        }
        catch (Exception exception) { Warn("SAIN personality selection filter failed", exception); }
    }

    private static void SetterPrefix<TPersonality>(object __instance, ref TPersonality personality)
    {
        try
        {
            if (!IsPassive(personality) || !IsArenaBot(__instance) || NormalSettings() == null) return;
            var previous = personality;
            personality = (TPersonality)_normal;
            // Let SAIN's public setter install both enum and settings itself.
            // Preserve its timer semantics; do not overwrite PFT-owned values.
            LogReplacement(previous);
        }
        catch (Exception exception) { Warn("SAIN explicit personality filter failed", exception); }
    }

    private static void LogReplacement(object previous)
    {
        var world = Singleton<GameWorld>.Instance;
        if (!ReferenceEquals(world, _loggedWorld?.Target))
        {
            // Diagnostics must not keep an ended raid alive.
            _loggedWorld = new WeakReference(world);
            _replacements = 0;
        }
        if (++_replacements <= 3)
            Plugin.ModLogger.LogInfo($"Arena SAIN personality: {previous} -> Normal (assignment #{_replacements}).");
    }

    private static void Warn(string message, Exception exception)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning($"Arena: {message}{(exception == null ? "." : ": " + exception.GetBaseException().Message)}");
    }
}
