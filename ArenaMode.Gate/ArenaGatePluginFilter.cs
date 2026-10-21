using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ArenaMode.Shared.Session;
using BepInEx;

namespace ArenaMode.Gate
{
    /// <summary>
    /// The Harmony postfix on <c>TypeLoader.FindPluginTypes&lt;PluginInfo&gt;</c> and the code behind
    /// it: reads every <c>PluginInfo</c> the chainloader found, asks <see cref="ArenaWhitelistDecision"/>
    /// which units may load, and removes the excluded ones from their lists.
    ///
    /// The plugin list is filtered, never the type cache: <c>FindPluginTypes</c> has written
    /// <c>chainloader_typeloader.dat</c> before this postfix runs, so the cache file stays complete
    /// and a normal start afterwards finds every plugin again. An excluded plugin keeps its (now
    /// empty) dictionary entry, exactly like an assembly without a plugin type.
    ///
    /// Every <c>PluginInfo</c> member is read by reflection, and the postfix takes
    /// <c>object __result</c>, so this class never names <c>PluginInfo</c>. That alone does not keep
    /// <c>UnityEngine.CoreModule</c> from loading when the postfix is installed (Harmony loads
    /// <c>PluginInfo</c>, and with it that module, as soon as it patches
    /// <c>FindPluginTypes&lt;PluginInfo&gt;</c>), which is why the installation waits for
    /// <c>Patcher.Finish</c>; it does keep the gate's own types free of the dependency.
    /// </summary>
    public static class ArenaGatePluginFilter
    {
        internal const string PluginInfoTypeName = "BepInEx.PluginInfo";

        /// <summary>
        /// Runs after every <c>FindPluginTypes&lt;PluginInfo&gt;</c>. Never throws: BepInEx would
        /// treat an exception here as a failed chainloader start and load no plugin at all. A
        /// failure stops the gate instead (everything loads, the report says why).
        /// </summary>
        /// <param name="directory">The method's own <c>directory</c> argument (bound by name).</param>
        /// <param name="__result">The method's result, untyped.</param>
        public static void Postfix(string directory, object __result)
        {
            try
            {
                ArenaGateRuntime.OnPluginTypes(directory, __result);
            }
            catch (Exception ex)
            {
                try
                {
                    ArenaGateRuntime.Fail("the plugin list could not be filtered: " + ex.Message, ex);
                }
                catch
                {
                    // Nothing left to do. The plugin list is still the unfiltered one on every path
                    // that can get here: Plan.Decide does not touch the lists, Plan.Apply restores
                    // them when it fails, and nothing that runs after Plan.Apply may throw (see
                    // ArenaGateRuntime.OnPluginTypes), because a throw there would leave a filtered
                    // list behind an inactive report.
                }
            }
        }

        /// <summary>
        /// True for <c>Dictionary&lt;string, List&lt;BepInEx.PluginInfo&gt;&gt;</c>. The postfix sits on
        /// the shared code of every reference-type instantiation, so this is what tells the
        /// chainloader's call from, say, the patcher scan.
        /// </summary>
        internal static bool IsPluginInfoDictionary(object result)
        {
            if (!(result is IDictionary)) return false;

            Type dictionary = result.GetType();
            if (!dictionary.IsGenericType) return false;

            Type[] arguments = dictionary.GetGenericArguments();
            if (arguments.Length != 2 || arguments[0] != typeof(string)) return false;

            Type list = arguments[1];
            if (!list.IsGenericType || list.GetGenericTypeDefinition() != typeof(List<>)) return false;

            Type element = list.GetGenericArguments()[0];
            return element.FullName == PluginInfoTypeName && element.Assembly == typeof(Paths).Assembly;
        }

        /// <summary>
        /// A decision about the plugin infos of one scan and the means to carry it out. Deciding
        /// does not touch the lists; <see cref="Apply"/> does, and either filters them or, when
        /// anything goes wrong, leaves them exactly as they were and lets the exception propagate.
        /// </summary>
        internal sealed class Plan
        {
            private readonly List<Slot> _slots;
            private readonly bool[] _excluded;

            internal Plan(ArenaWhitelistDecision decision, List<Slot> slots, bool[] excluded)
            {
                Decision = decision;
                _slots = slots;
                _excluded = excluded;
            }

            public ArenaWhitelistDecision Decision { get; private set; }

            public void Apply()
            {
                Remove(_slots, _excluded);
            }
        }

        /// <summary>
        /// Applies the policy to the plugin infos in <paramref name="plugins"/>. Nothing is
        /// removed yet: the caller builds whatever depends on the decision first, so that nothing
        /// that can fail is left to do once the lists have changed.
        /// </summary>
        internal static Plan Decide(ArenaWhitelistPolicy policy, IDictionary plugins)
        {
            var slots = new List<Slot>();
            var units = new List<ArenaSessionUnit>();
            PluginInfoReader reader = null;

            foreach (DictionaryEntry entry in plugins)
            {
                // The key is the DLL path. PluginInfo.Location is only set by the chainloader
                // after this method has returned.
                string file = entry.Key as string;
                IList list = entry.Value as IList;
                if (list == null) continue;

                for (int index = 0; index < list.Count; index++)
                {
                    object info = list[index];
                    if (info == null) continue;

                    if (reader == null) reader = new PluginInfoReader(info.GetType());
                    ArenaSessionUnit unit = reader.Read(info, file);
                    slots.Add(new Slot(list, index));
                    units.Add(unit);
                }
            }

            ArenaWhitelistDecision decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, units);
            return new Plan(decision, slots, MapExcluded(units, decision));
        }

        /// <summary>
        /// Maps the decision back onto the input. Allowed and excluded both keep the input order
        /// and every unit is in exactly one of them, so one walk over the input does it.
        /// </summary>
        private static bool[] MapExcluded(List<ArenaSessionUnit> units, ArenaWhitelistDecision decision)
        {
            if (decision.Allowed.Count + decision.Excluded.Count != units.Count)
            {
                throw new InvalidOperationException("the decision covers " + (decision.Allowed.Count + decision.Excluded.Count)
                    + " of " + units.Count + " plugins");
            }

            var excluded = new bool[units.Count];
            int next = 0;
            for (int i = 0; i < units.Count; i++)
            {
                if (next < decision.Excluded.Count && SameUnit(decision.Excluded[next], units[i]))
                {
                    excluded[i] = true;
                    next++;
                }
            }

            if (next != decision.Excluded.Count)
            {
                throw new InvalidOperationException("the decision excludes a plugin that is not in the plugin list");
            }

            return excluded;
        }

        private static bool SameUnit(ArenaSessionUnit a, ArenaSessionUnit b)
        {
            return string.Equals(a.Guid, b.Guid, StringComparison.Ordinal)
                && string.Equals(a.Name, b.Name, StringComparison.Ordinal)
                && string.Equals(a.Version, b.Version, StringComparison.Ordinal)
                && string.Equals(a.File, b.File, StringComparison.Ordinal);
        }

        private static void Remove(List<Slot> slots, bool[] excluded)
        {
            // Snapshot every list that is about to change, so that a failure part-way through
            // leaves the lists as they were.
            var snapshots = new Dictionary<IList, object[]>();
            for (int i = 0; i < slots.Count; i++)
            {
                IList list = slots[i].List;
                if (!excluded[i] || snapshots.ContainsKey(list)) continue;

                var copy = new object[list.Count];
                list.CopyTo(copy, 0);
                snapshots.Add(list, copy);
            }

            try
            {
                // Back to front: the slots of one list are in ascending index order, so removing
                // from the end never shifts an index that is still to be removed.
                for (int i = slots.Count - 1; i >= 0; i--)
                {
                    if (excluded[i]) slots[i].List.RemoveAt(slots[i].Index);
                }
            }
            catch
            {
                foreach (KeyValuePair<IList, object[]> snapshot in snapshots)
                {
                    snapshot.Key.Clear();
                    foreach (object item in snapshot.Value) snapshot.Key.Add(item);
                }

                throw;
            }
        }

        internal struct Slot
        {
            public readonly IList List;
            public readonly int Index;

            public Slot(IList list, int index)
            {
                List = list;
                Index = index;
            }
        }

        /// <summary>
        /// Reads a <c>PluginInfo</c> into an <see cref="ArenaSessionUnit"/> by reflection. A shape
        /// that differs from BepInEx 5.4 throws in the constructor or in <see cref="Read"/>, which
        /// stops the gate (everything loads) instead of guessing.
        /// </summary>
        private sealed class PluginInfoReader
        {
            // BepInDependency.DependencyFlags.HardDependency
            private const int HardDependencyFlag = 1;

            private readonly PropertyInfo _metadata;
            private readonly PropertyInfo _dependencies;
            private PropertyInfo _guid;
            private PropertyInfo _name;
            private PropertyInfo _version;
            private Type _metadataType;
            private Type _dependencyType;
            private PropertyInfo _dependencyGuid;
            private PropertyInfo _dependencyFlags;

            public PluginInfoReader(Type pluginInfo)
            {
                _metadata = Property(pluginInfo, "Metadata");
                _dependencies = Property(pluginInfo, "Dependencies");
            }

            public ArenaSessionUnit Read(object info, string file)
            {
                object metadata = _metadata.GetValue(info, null);
                if (metadata == null) throw new InvalidOperationException("a PluginInfo from '" + file + "' has no metadata");

                if (_metadataType != metadata.GetType())
                {
                    _metadataType = metadata.GetType();
                    _guid = Property(_metadataType, "GUID");
                    _name = Property(_metadataType, "Name");
                    _version = Property(_metadataType, "Version");
                }

                object version = _version.GetValue(metadata, null);
                var unit = new ArenaSessionUnit
                {
                    Guid = _guid.GetValue(metadata, null) as string,
                    Name = _name.GetValue(metadata, null) as string,
                    Version = version == null ? null : version.ToString(),
                    File = file,
                };

                IEnumerable dependencies = _dependencies.GetValue(info, null) as IEnumerable;
                if (dependencies != null)
                {
                    foreach (object dependency in dependencies)
                    {
                        if (dependency == null) continue;
                        if (_dependencyType != dependency.GetType())
                        {
                            _dependencyType = dependency.GetType();
                            _dependencyGuid = Property(_dependencyType, "DependencyGUID");
                            _dependencyFlags = Property(_dependencyType, "Flags");
                        }

                        // Soft dependencies do not pull a plugin in: the dependant runs without them.
                        int flags = Convert.ToInt32(_dependencyFlags.GetValue(dependency, null));
                        if ((flags & HardDependencyFlag) == 0) continue;

                        string dependencyGuid = _dependencyGuid.GetValue(dependency, null) as string;
                        if (!string.IsNullOrEmpty(dependencyGuid)) unit.HardDependencies.Add(dependencyGuid);
                    }
                }

                return unit;
            }

            private static PropertyInfo Property(Type type, string name)
            {
                PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property == null) throw new MissingMemberException(type.FullName, name);
                return property;
            }
        }
    }
}
