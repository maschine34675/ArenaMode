using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ArenaMode.Shared.Session;
using BepInEx;
using BepInEx.Preloader.Patching;
using Mono.Cecil;

namespace ArenaMode.Gate
{
    /// <summary>
    /// Switches off every preloader patcher the policy does not allow
    /// (docs/ARENA-SESSION-20261021.md, section 5, step 3).
    ///
    /// A <c>PatcherPlugin</c> is four delegates. The preloader reads them from the object at the
    /// moment it needs each one: the initializer in turn during <c>InitializePatchers</c>, the
    /// target list and the patch method afterwards, the finalizer at the end. Replacing the
    /// delegates of a patcher that has not run yet therefore stops it. A patcher that sorts before
    /// the gate has already run its initializer; that cannot be undone, so it is reported as a
    /// warning, and its target list, patch method and finalizer are still replaced.
    ///
    /// Never touched: the gate itself and BepInEx's own entry-point patcher (without which no
    /// plugin would ever start), recognised by the assembly their delegates come from.
    /// </summary>
    internal static class ArenaGatePatchers
    {
        private static readonly Func<IEnumerable<string>> NoTargets = EmptyTargets;
        private static readonly Action NoAction = DoNothing;
        private static readonly AssemblyPatcherDelegate NoPatch = DoNotPatch;

        /// <summary>What <see cref="Neutralise"/> did, with the means to take it back.</summary>
        internal sealed class Outcome
        {
            private readonly List<Action> _undo = new List<Action>();

            /// <summary>Files of the patchers that were switched off (each once).</summary>
            public readonly List<string> Neutralised = new List<string>();

            /// <summary>Patchers that could not be handled completely.</summary>
            public readonly List<string> Warnings = new List<string>();

            internal void AddUndo(Action undo)
            {
                _undo.Add(undo);
            }

            /// <summary>Puts every replaced delegate back (used when a later step of the gate fails).</summary>
            public void Rollback()
            {
                for (int i = _undo.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _undo[i]();
                    }
                    catch
                    {
                        // Best effort; nothing more can be done about a setter that throws.
                    }
                }

                _undo.Clear();
            }
        }

        /// <summary>
        /// Neutralises the patchers that are not allowed. Either every patcher is handled or, when
        /// anything throws, none is changed and the exception propagates.
        /// </summary>
        internal static Outcome Neutralise(ArenaWhitelistPolicy policy)
        {
            var outcome = new Outcome();
            try
            {
                Apply(policy, outcome);
            }
            catch
            {
                outcome.Rollback();
                throw;
            }

            return outcome;
        }

        private static void Apply(ArenaWhitelistPolicy policy, Outcome outcome)
        {
            PatcherPlugin[] plugins = AssemblyPatcher.PatcherPlugins.ToArray();
            Assembly self = typeof(ArenaGatePatchers).Assembly;
            Assembly preloader = typeof(AssemblyPatcher).Assembly;
            Assembly core = typeof(Paths).Assembly;

            // The preloader runs the initializers in list order, so the patchers before the gate
            // have run theirs and the ones after it have not.
            var origins = new Assembly[plugins.Length];
            int selfIndex = -1;
            for (int i = 0; i < plugins.Length; i++)
            {
                origins[i] = OriginOf(plugins[i]);
                if (selfIndex < 0 && origins[i] == self) selfIndex = i;
            }

            bool orderUnknown = false;
            for (int i = 0; i < plugins.Length; i++)
            {
                PatcherPlugin plugin = plugins[i];
                Assembly origin = origins[i];

                if (origin == null)
                {
                    outcome.Warnings.Add("preloader patcher " + plugin.TypeName
                        + ": its assembly could not be determined, so it was left unchanged");
                    continue;
                }

                if (origin == self || origin == preloader || origin == core) continue;

                string file = FileNameOf(origin);
                if (file.Length == 0)
                {
                    outcome.Warnings.Add("preloader patcher " + plugin.TypeName
                        + ": its file could not be determined, so it was left unchanged");
                    continue;
                }

                if (policy.IsAllowedPatcher(file)) continue;

                bool initializerMayHaveRun = plugin.Initializer != null && (selfIndex < 0 || i < selfIndex);
                if (initializerMayHaveRun && selfIndex >= 0)
                {
                    outcome.Warnings.Add("preloader patcher " + file + " is not on the whitelist, but its initializer ran "
                        + "before the gate and cannot be undone; its patch targets and finalizer were neutralised");
                }
                else if (initializerMayHaveRun)
                {
                    orderUnknown = true;
                }

                Func<IEnumerable<string>> targets = plugin.TargetDLLs;
                Action initializer = plugin.Initializer;
                Action finalizer = plugin.Finalizer;
                AssemblyPatcherDelegate patcher = plugin.Patcher;
                outcome.AddUndo(() =>
                {
                    plugin.TargetDLLs = targets;
                    plugin.Initializer = initializer;
                    plugin.Finalizer = finalizer;
                    plugin.Patcher = patcher;
                });

                plugin.TargetDLLs = NoTargets;
                plugin.Initializer = NoAction;
                plugin.Finalizer = NoAction;
                plugin.Patcher = NoPatch;

                AddOnce(outcome.Neutralised, file);
            }

            if (orderUnknown)
            {
                outcome.Warnings.Add("the position of the gate in the preloader's patcher list is unknown, so "
                    + "initializers of the neutralised patchers may already have run");
            }
        }

        /// <summary>
        /// The assembly a patcher comes from, read from its target-list, initializer or finalizer
        /// delegate. Never from the patch delegate: for patchers found in a directory that one is a
        /// closure of the preloader itself.
        /// </summary>
        private static Assembly OriginOf(PatcherPlugin plugin)
        {
            Delegate[] candidates = { plugin.TargetDLLs, plugin.Initializer, plugin.Finalizer };
            foreach (Delegate candidate in candidates)
            {
                if (candidate == null) continue;

                MethodInfo method = candidate.Method;
                Assembly assembly = method == null || method.DeclaringType == null ? null : method.DeclaringType.Assembly;
                if (assembly != null) return assembly;
            }

            return null;
        }

        private static string FileNameOf(Assembly assembly)
        {
            try
            {
                string location = assembly.Location;
                return string.IsNullOrEmpty(location) ? string.Empty : Path.GetFileName(location);
            }
            catch
            {
                // A dynamic assembly has no location.
                return string.Empty;
            }
        }

        private static void AddOnce(List<string> files, string file)
        {
            foreach (string existing in files)
            {
                if (string.Equals(existing, file, StringComparison.OrdinalIgnoreCase)) return;
            }

            files.Add(file);
        }

        private static IEnumerable<string> EmptyTargets()
        {
            return new string[0];
        }

        private static void DoNothing()
        {
        }

        private static void DoNotPatch(ref AssemblyDefinition assembly)
        {
        }
    }
}
