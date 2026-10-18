using System;
using System.IO;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;

namespace ArenaMode.Compat;

/// <summary>
/// Owns the optional typed library without a compile-time reference or extra
/// plugin registration. Start runs after BepInEx has completed plugin Awake.
/// </summary>
internal static class ArenaBigBrainLibrary
{
    internal const string BigBrainGuid = "xyz.drakia.bigbrain";
    private const string AssemblyName = "maschine-ArenaMode.BigBrainCompat";
    private const string EntryType = "ArenaMode.BigBrainCompat.Library";
    private const string LegacyPluginGuid = "com.maschine.ArenaMode.BigBrainCompat";
    private static bool _attempted;
    private static Action _tick;
    private static Action _shutdown;
    private static ManualLogSource _logger;

    internal static void Initialize(ManualLogSource logger, string pluginFile)
    {
        if (_attempted) return;
        _attempted = true;
        _logger = logger;
        // Do not even load the library when the optional plugin is absent.
        if (!Chainloader.PluginInfos.TryGetValue(BigBrainGuid, out var bigBrain)
            || bigBrain.Instance == null)
        {
            logger.LogDebug("Arena BigBrain library is inactive: optional BigBrain is not loaded.");
            return;
        }

        try
        {
            // A leftover old plugin may already own layers. Never initialize a
            // second owner; the complete package replaces that same DLL path.
            if (Chainloader.PluginInfos.ContainsKey(LegacyPluginGuid))
                throw new InvalidOperationException("An old Arena BigBrain plugin is loaded. Replace both Arena DLLs from the same package and remove duplicate copies before restarting.");

            string path = Path.Combine(Path.GetDirectoryName(pluginFile), AssemblyName + ".dll");
            var identity = System.Reflection.AssemblyName.GetAssemblyName(path);
            if (identity.Name != AssemblyName || identity.Version != typeof(Plugin).Assembly.GetName().Version)
                throw new InvalidOperationException("The Arena BigBrain library does not match the client assembly version.");

            // Named-type lookup only: GetTypes would resolve every optional layer.
            Assembly assembly = Assembly.LoadFrom(path);
            Type entry = assembly.GetType(EntryType, throwOnError: true);
            if ((int?)entry.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() != 1)
                throw new InvalidOperationException("The Arena BigBrain library API is not supported by this client.");

            var initialize = (Func<ManualLogSource, bool>)Delegate.CreateDelegate(typeof(Func<ManualLogSource, bool>), entry.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static));
            var tick = (Action)Delegate.CreateDelegate(typeof(Action), entry.GetMethod("Tick", BindingFlags.Public | BindingFlags.Static));
            _shutdown = (Action)Delegate.CreateDelegate(typeof(Action), entry.GetMethod("Shutdown", BindingFlags.Public | BindingFlags.Static));
            if (initialize(logger)) _tick = tick;
            else Shutdown();
        }
        catch (Exception ex)
        {
            Shutdown();
            logger.LogError($"Arena BigBrain library could not start; optional bot movement support is unavailable: {ex.GetBaseException().Message}");
        }
    }

    internal static void Tick()
    {
        if (_tick == null) return;
        try { _tick(); }
        catch (Exception ex)
        {
            Shutdown();
            _logger?.LogError($"Arena BigBrain library stopped after a maintenance failure: {ex.GetBaseException().Message}");
        }
    }

    internal static void Shutdown()
    {
        Action shutdown = _shutdown;
        _tick = null;
        _shutdown = null;
        try { shutdown?.Invoke(); }
        catch (Exception ex) { _logger?.LogError($"Arena BigBrain library cleanup failed: {ex.GetBaseException().Message}"); }
    }
}
