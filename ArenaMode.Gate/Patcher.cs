using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ArenaMode.Shared.Session;
using Mono.Cecil;

namespace ArenaMode.Gate
{
    /// <summary>
    /// BepInEx preloader patcher entry point of the Arena session gate
    /// (docs/ARENA-SESSION-20261021.md, section 5). It patches no assembly: a patcher is simply the
    /// earliest place where a mod can run, and the two callbacks below are all the gate needs.
    ///
    /// Without the <c>-arena-session</c> command-line flag every callback returns on its first
    /// statement: no Harmony, no logging, no file access, nothing that a normal start could
    /// notice. The work lives in <see cref="ArenaGateRuntime"/>, in methods marked NoInlining, so
    /// that even loading this class does not load anything the work needs.
    ///
    /// Two stages, in the order BepInEx runs them:
    /// <list type="number">
    /// <item><c>Initialize</c> loads the policy and neutralises every preloader patcher that is not
    /// allowed. Both must happen before the preloader asks the patchers for their target DLLs.</item>
    /// <item><c>Finish</c> installs the filter on the plugin list. It cannot run any earlier:
    /// patching the generic <c>TypeLoader.FindPluginTypes&lt;PluginInfo&gt;</c> makes the runtime
    /// load <c>PluginInfo</c> and with it <c>UnityEngine.CoreModule</c> (measured with the Mono of
    /// the game's Unity version, see docs/ARENA-GATE-20261021.md and tools/ArenaGateChecks).
    /// During <c>Initialize</c> the preloader has not yet patched
    /// and loaded that assembly, so the runtime would load the unpatched file from disk, BepInEx's
    /// entry point would never be injected and no plugin would start at all. <c>Finish</c> runs
    /// after the preloader has loaded its patched assemblies; the chainloader itself only starts
    /// later, when Unity runs <c>Application..cctor</c>.</item>
    /// </list>
    /// </summary>
    public static class Patcher
    {
        /// <summary>
        /// No assembly is patched. The getter does no work; BepInEx only needs it to recognise
        /// the class as a patcher.
        /// </summary>
        public static IEnumerable<string> TargetDLLs
        {
            get { return new string[0]; }
        }

        public static void Initialize()
        {
            if (!IsArenaSession()) return;

            try
            {
                ArenaGateRuntime.Prepare();
            }
            catch (Exception ex)
            {
                ReportUnexpectedFailure("Initialize", ex);
            }
        }

        public static void Patch(AssemblyDefinition assembly)
        {
            // Never called: TargetDLLs is empty.
        }

        public static void Finish()
        {
            if (!IsArenaSession()) return;

            try
            {
                ArenaGateRuntime.Arm();
            }
            catch (Exception ex)
            {
                ReportUnexpectedFailure("Finish", ex);
            }
        }

        /// <summary>
        /// True when the game process was started with <c>-arena-session</c>. Any failure to read
        /// the command line counts as "no", so a normal start can never be affected.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsArenaSession()
        {
            try
            {
                return ArenaSessionContract.HasSessionFlag(Environment.GetCommandLineArgs());
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Last resort for a failure the runtime class could not handle itself (it handles every
        /// exception it can raise; this catches what happens before its code runs, such as a type
        /// that cannot be loaded). Publishes an inactive report with only BCL calls, so that
        /// ArenaMode refuses Arena with a reason instead of finding no report at all.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReportUnexpectedFailure(string callback, Exception exception)
        {
            try
            {
                string error = "the gate failed in " + callback + ": " + exception.GetType().Name + ": " + exception.Message;
                AppDomain.CurrentDomain.SetData(ArenaSessionContract.GateReportDataKey, ArenaGateReport.Inactive(error, null).ToJson());
            }
            catch
            {
                // Nothing left to report with.
            }
        }
    }
}
