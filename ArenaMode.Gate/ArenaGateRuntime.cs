using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using ArenaMode.Shared.Session;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace ArenaMode.Gate
{
    /// <summary>
    /// The gate's state machine and everything that talks to BepInEx
    /// (docs/ARENA-SESSION-20261021.md, section 5). Only reached when the process was started with
    /// <c>-arena-session</c>.
    ///
    /// <code>
    /// Idle ---- Prepare() ----> Prepared ---- Arm() ----> Armed
    ///   any failure, in any state, leads to Failed (final)
    /// </code>
    ///
    /// Failing is always safe: only Armed filters the plugin list, Failed publishes an inactive
    /// report with the reason, and nothing here ever exits the game. ArenaMode refuses Arena in a
    /// process whose report is not active.
    ///
    /// Nothing in this class names <c>BepInEx.PluginInfo</c>, <c>BaseUnityPlugin</c> or the
    /// chainloader: those types reach <c>UnityEngine.CoreModule</c>, which must not be loaded
    /// before the preloader has patched it (see <see cref="Patcher"/>).
    /// </summary>
    internal static class ArenaGateRuntime
    {
        internal const string LogSourceName = "maschine-ArenaMode.Gate";
        internal const string HarmonyId = "com.maschine.ArenaMode.Gate";

        private const string CoreModuleAssemblyName = "UnityEngine.CoreModule";
        private const string TypeLoaderTypeName = "BepInEx.Bootstrap.TypeLoader";
        private const string FindPluginTypesName = "FindPluginTypes";

        private enum Stage
        {
            Idle,
            Prepared,
            Armed,
            Failed,
        }

        private static readonly object Sync = new object();

        // What the report carries besides the decision: policy diagnostics (an ignored user file,
        // for example), patcher warnings, the neutralised patcher files and, of those, the ones whose
        // initializer had already run when the gate got to them.
        private static readonly List<string> Diagnostics = new List<string>();
        private static readonly List<string> PatcherWarnings = new List<string>();
        private static readonly List<string> Neutralised = new List<string>();
        private static readonly List<string> RanBeforeGate = new List<string>();

        private static Stage _stage = Stage.Idle;
        private static ManualLogSource _log;
        private static ArenaWhitelistPolicy _policy;
        private static int _filterRuns;

        // ------------------------------------------------------------------ Initialize

        /// <summary>
        /// Loads the policy and neutralises every preloader patcher that is not allowed. Runs in
        /// <c>Patcher.Initialize</c>, i.e. before the preloader asks any patcher for its target
        /// DLLs. Touches nothing that could load a Unity assembly.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Prepare()
        {
            lock (Sync)
            {
                if (_stage != Stage.Idle) return;

                // Pessimistic until every step below has succeeded.
                _stage = Stage.Failed;

                try
                {
                    _log = Logger.CreateLogSource(LogSourceName);
                }
                catch
                {
                    // Logging is a convenience. Carry on without it.
                    _log = null;
                }

                ArenaGatePatchers.Outcome patchers = null;
                try
                {
                    string error;
                    if (!TryLoadPolicy(out error))
                    {
                        Fail(error, null);
                        return;
                    }

                    foreach (string diagnostic in Diagnostics) Log(LogLevel.Warning, "Arena session: " + diagnostic);

                    patchers = ArenaGatePatchers.Neutralise(_policy);
                    Neutralised.AddRange(patchers.Neutralised);
                    RanBeforeGate.AddRange(patchers.RanBeforeGate);
                    PatcherWarnings.AddRange(patchers.Warnings);
                    foreach (string warning in PatcherWarnings) Log(LogLevel.Warning, "Arena session: " + warning);

                    _stage = Stage.Prepared;
                    Log(LogLevel.Debug, "Whitelist policy " + _policy.Hash + ": " + _policy.Entries.Count
                        + " entries, user file " + (_policy.UserFileApplied ? "applied" : "not applied")
                        + ", unlisted units " + (_policy.AllowsUnlisted ? "allowed (blacklist)" : "denied (whitelist)"));

                    // Until the plugin list has been filtered the honest answer is "inactive".
                    Publish(BuildInactive("the plugin list has not been filtered yet"));

                    // Last, after the steps that can still fail and roll the patchers back: a line that
                    // says they are off must not be logged for patchers that were switched on again.
                    // Info, not Debug: the preloader's log listener keeps only the levels of
                    // Logging.Console.LogLevels (Debug is not among the defaults) and nothing else
                    // names these patchers. Once this method has returned nothing switches them on
                    // again: a later failure of the gate leaves them off (see Fail).
                    if (Neutralised.Count > 0) Log(LogLevel.Info, "Arena session: " + DescribeNeutralised());
                }
                catch (Exception ex)
                {
                    if (patchers != null) patchers.Rollback();
                    Neutralised.Clear();
                    RanBeforeGate.Clear();
                    PatcherWarnings.Clear();
                    Fail("the gate could not prepare the Arena session: " + ex.Message, ex);
                }
            }
        }

        private static bool TryLoadPolicy(out string error)
        {
            error = null;

            string defaultPath = Path.Combine(
                Path.Combine(Paths.PluginPath, ArenaSessionContract.PluginFolderName),
                ArenaSessionContract.DefaultPolicyFileName);

            // Both files go through ArenaPolicyFiles, the reader the client uses as well (one size limit, one
            // decoding): the client builds the request from its reading of these files, and the Arena session
            // refuses when the two policy hashes differ.
            string defaultJson;
            string detail;
            ArenaPolicyFileState defaultState = ArenaPolicyFiles.TryRead(defaultPath, out defaultJson, out detail);
            if (defaultState != ArenaPolicyFileState.Read)
            {
                error = "cannot read the default whitelist '" + defaultPath + "': " + ArenaPolicyFiles.Describe(defaultState, detail);
                return false;
            }

            string userPath = Path.Combine(Paths.ConfigPath, ArenaSessionContract.UserPolicyFileName);
            string userJson;
            ArenaPolicyFileState userState = ArenaPolicyFiles.TryRead(userPath, out userJson, out detail);
            if (userState == ArenaPolicyFileState.TooLarge || userState == ArenaPolicyFileState.Unreadable)
            {
                Diagnostics.Add("user whitelist: cannot read '" + userPath + "', the file is ignored: " + ArenaPolicyFiles.Describe(userState, detail));
                userJson = null;
            }

            ArenaWhitelistPolicy policy;
            List<string> diagnostics;
            if (!ArenaWhitelistPolicy.TryLoad(defaultJson, userJson, out policy, out diagnostics))
            {
                error = diagnostics.Count > 0
                    ? string.Join("; ", diagnostics.ToArray())
                    : "the whitelist policy could not be loaded";
                return false;
            }

            _policy = policy;
            Diagnostics.AddRange(diagnostics);
            return true;
        }

        // ------------------------------------------------------------------ Finish

        /// <summary>
        /// Installs the filter on the plugin list. Runs in <c>Patcher.Finish</c>, after the
        /// preloader has loaded its patched assemblies. Refuses to install when
        /// <c>UnityEngine.CoreModule</c> is not loaded yet: patching would then be the thing that
        /// loads the unpatched module from disk.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Arm()
        {
            lock (Sync)
            {
                if (_stage != Stage.Prepared) return;

                try
                {
                    if (!IsAssemblyLoaded(CoreModuleAssemblyName))
                    {
                        Fail(CoreModuleAssemblyName + " is not loaded at the end of the preloader stage; installing the "
                            + "plugin filter would load the unpatched module from disk and keep BepInEx from starting, "
                            + "so the filter was not installed", null);
                        return;
                    }

                    var watch = Stopwatch.StartNew();
                    InstallFilter();
                    watch.Stop();
                    _stage = Stage.Armed;
                    Log(LogLevel.Debug, "Plugin list filter installed in " + watch.ElapsedMilliseconds + " ms");
                }
                catch (Exception ex)
                {
                    Fail("the plugin list filter could not be installed: " + ex.Message, ex);
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InstallFilter()
        {
            // Reached by name: a typed reference would bind this code to the BepInEx build, and
            // PluginInfo must not appear in any signature of the gate.
            Assembly bepInEx = typeof(Paths).Assembly;
            Type typeLoader = bepInEx.GetType(TypeLoaderTypeName, false);
            Type pluginInfo = bepInEx.GetType(ArenaGatePluginFilter.PluginInfoTypeName, false);
            if (typeLoader == null || pluginInfo == null)
            {
                throw new MissingMemberException("BepInEx has no " + TypeLoaderTypeName + " or "
                    + ArenaGatePluginFilter.PluginInfoTypeName + " type");
            }

            MethodInfo open = FindGenericFindPluginTypes(typeLoader);
            if (open == null)
            {
                throw new MissingMethodException(TypeLoaderTypeName, FindPluginTypesName
                    + "<T>(string, Func<..>, Func<..>, string)");
            }

            MethodInfo closed = open.MakeGenericMethod(pluginInfo);
            MethodInfo postfix = typeof(ArenaGatePluginFilter).GetMethod(
                nameof(ArenaGatePluginFilter.Postfix), BindingFlags.Public | BindingFlags.Static);
            if (postfix == null) throw new MissingMethodException(nameof(ArenaGatePluginFilter), nameof(ArenaGatePluginFilter.Postfix));

            var harmony = new Harmony(HarmonyId);
            harmony.Patch(closed, postfix: new HarmonyMethod(postfix));

            // Do not take Patch() returning for success: ask Harmony what is registered.
            Patches patches = Harmony.GetPatchInfo(closed);
            bool installed = false;
            if (patches != null)
            {
                foreach (Patch candidate in patches.Postfixes)
                {
                    if (candidate.owner == HarmonyId) installed = true;
                }
            }

            if (!installed) throw new InvalidOperationException("Harmony did not register the postfix");
        }

        private static MethodInfo FindGenericFindPluginTypes(Type typeLoader)
        {
            foreach (MethodInfo candidate in typeLoader.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (candidate.Name != FindPluginTypesName || !candidate.IsGenericMethodDefinition) continue;
                if (candidate.GetGenericArguments().Length != 1) continue;

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length != 4) continue;
                if (parameters[0].ParameterType != typeof(string) || parameters[3].ParameterType != typeof(string)) continue;
                if (parameters[0].Name != "directory") continue;
                return candidate;
            }

            return null;
        }

        private static bool IsAssemblyLoaded(string simpleName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (string.Equals(assembly.GetName().Name, simpleName, StringComparison.Ordinal)) return true;
                }
                catch
                {
                    // A dynamic or broken assembly is not the one asked for.
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ the plugin list

        /// <summary>
        /// Called by the Harmony postfix on every <c>FindPluginTypes&lt;PluginInfo&gt;</c> call.
        /// Acts only when the result is the plugin-info dictionary of the chainloader's plugin
        /// directory; any other instantiation or directory is left alone. May throw: the postfix
        /// turns every exception into a Failed state.
        /// </summary>
        internal static void OnPluginTypes(string directory, object result)
        {
            lock (Sync)
            {
                if (_stage != Stage.Armed) return;
                if (!ArenaGatePluginFilter.IsPluginInfoDictionary(result)) return;
                if (!IsChainloaderDirectory(directory)) return;

                var watch = Stopwatch.StartNew();

                // Everything that can fail comes before the lists change; afterwards only the
                // removal itself (which restores the lists when it fails) and the guarded
                // publishing and logging are left.
                ArenaGatePluginFilter.Plan plan = ArenaGatePluginFilter.Decide(_policy, (IDictionary)result);
                ArenaWhitelistDecision decision = plan.Decision;
                ArenaGateReport report = ArenaGateReport.FromDecision(_policy, decision, Neutralised, RanBeforeGate);
                report.Warnings.AddRange(Diagnostics);
                report.Warnings.AddRange(PatcherWarnings);
                string json = report.ToJson();

                plan.Apply();

                // From here on the lists are filtered and nothing below may throw: the postfix would
                // turn an exception into Fail, which publishes an inactive report for a filtered list.
                // Publish and Log swallow their own failures; the rest is string building.
                Publish(json);
                watch.Stop();

                _filterRuns++;
                if (_filterRuns == 1) Log(LogLevel.Info, report.SummaryLine());
                else Log(LogLevel.Debug, "The plugin list was filtered again (run " + _filterRuns + "): " + report.SummaryLine());
                Log(LogLevel.Debug, "Plugin list filtered in " + watch.ElapsedMilliseconds + " ms, policy " + _policy.Hash);

                foreach (ArenaSessionUnit unit in decision.Excluded)
                {
                    Log(LogLevel.Debug, "Excluded " + unit.DisplayName + " " + unit.Version + " (" + unit.Guid + "): "
                        + unit.Reason + ", " + unit.File);
                }

                foreach (string warning in decision.Warnings) Log(LogLevel.Debug, warning);
            }
        }

        private static bool IsChainloaderDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(Paths.PluginPath)) return false;
            return string.Equals(NormalizeDirectory(directory), NormalizeDirectory(Paths.PluginPath),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDirectory(string path)
        {
            return Path.GetFullPath(path).TrimEnd('\\', '/');
        }

        // ------------------------------------------------------------------ failing, reporting, logging

        /// <summary>
        /// Stops the gate for good: nothing is filtered from here on, and the published report
        /// says why. Safe to call from any state and more than once.
        /// </summary>
        internal static void Fail(string error, Exception exception)
        {
            lock (Sync)
            {
                _stage = Stage.Failed;

                // Every plugin loads, but preloader patchers that were switched off in Prepare stay off:
                // the preloader has consumed their lists and nothing puts the originals back. Say so,
                // because the plugins of those patchers then run without them. (A failure inside
                // Prepare has already put them back and cleared the list, so nothing is added there.)
                string message = "Arena session: the plugin filter is not active and every plugin loads: " + error;
                if (Neutralised.Count > 0) message += " (preloader patchers that were already switched off stay off: " + string.Join(", ", Neutralised.ToArray()) + ")";
                Log(LogLevel.Error, message);
                if (exception != null) Log(LogLevel.Debug, exception.ToString());
                Publish(BuildInactive(error));
            }
        }

        private static string DescribeNeutralised()
        {
            return Neutralised.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " preloader patcher"
                + (Neutralised.Count == 1 ? string.Empty : "s") + " switched off: " + string.Join(", ", Neutralised.ToArray());
        }

        private static ArenaGateReport BuildInactive(string error)
        {
            ArenaGateReport report = ArenaGateReport.Inactive(error, _policy == null ? null : _policy.Hash);
            report.Warnings.AddRange(Diagnostics);
            report.Warnings.AddRange(PatcherWarnings);
            report.PatchersNeutralised.AddRange(Neutralised);
            report.PatchersRanBeforeGate.AddRange(RanBeforeGate);
            return report;
        }

        private static void Publish(ArenaGateReport report)
        {
            string json;
            try
            {
                json = report.ToJson();
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, "Arena session: the gate report could not be built: " + ex.Message);
                return;
            }

            Publish(json);
        }

        private static void Publish(string json)
        {
            try
            {
                AppDomain.CurrentDomain.SetData(ArenaSessionContract.GateReportDataKey, json);
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, "Arena session: the gate report could not be published: " + ex.Message);
            }
        }

        private static void Log(LogLevel level, string message)
        {
            ManualLogSource log = _log;
            if (log == null) return;
            try
            {
                log.Log(level, message);
            }
            catch
            {
                // A log listener that throws must not take the gate down.
            }
        }
    }
}
