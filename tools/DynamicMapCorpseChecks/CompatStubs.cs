// Minimal observable contracts authored for these checks. This does not execute
// Unity or Harmony IL patching and contains no copied DynamicMaps implementation.
using System.Reflection;
using EFT;

namespace BepInEx
{
    public class Metadata { public Version Version = new(1, 2, 1); }
    public class PluginInfo { public Metadata Metadata = new(); public object Instance; }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader
    {
        public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new();
    }
}
namespace HarmonyLib
{
    public static class Priority { public const int Last = 0; }
    public class HarmonyMethod
    {
        public int priority;
        internal MethodInfo Method;
        public HarmonyMethod(Type type, string name)
            => Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
    }
    public class Harmony
    {
        public static bool ThrowOnPatch;
        public static readonly Dictionary<MethodBase, (string Owner, MethodInfo Prefix)> Hooks = new();
        private readonly string _id;
        public Harmony(string id) => _id = id;
        public void Patch(MethodBase original, HarmonyMethod prefix)
        {
            if (ThrowOnPatch) throw new Exception("patch fault");
            Hooks[original] = (_id, prefix.Method);
        }
        public void UnpatchSelf()
        {
            foreach (var key in Hooks.Where(h => h.Value.Owner == _id).Select(h => h.Key).ToArray()) Hooks.Remove(key);
        }
        public static bool Dispatch(MethodInfo original, object instance, Player player)
            => !Hooks.TryGetValue(original, out var hook) || (bool)hook.Prefix.Invoke(null, new object[] { instance, player });
    }
}
namespace DynamicMaps
{
    public class Plugin { }
}
namespace DynamicMaps.UI.Components
{
    public class MapMarker : UnityEngine.Component { }
}
namespace DynamicMaps.DynamicMarkers
{
    public class CorpseMarkerProvider
    {
        private readonly Dictionary<Player, UI.Components.MapMarker> _corpseMarkers = new();
        private static readonly MethodInfo AddMethod = typeof(CorpseMarkerProvider).GetMethod(nameof(TryAddMarker), BindingFlags.Instance | BindingFlags.NonPublic);
        public int RemoveCalls;
        public bool ThrowOnRemove;
        public bool Contains(Player player) => _corpseMarkers.ContainsKey(player);
        public UI.Components.MapMarker Get(Player player) => _corpseMarkers[player];
        public int Count => _corpseMarkers.Count;
        public void Add(Player player) => TryAddMarker(player);
        public void Reopen(IEnumerable<Player> players) { foreach (var player in players) TryAddMarker(player); }
        public void Refresh(IEnumerable<Player> players)
        {
            foreach (var old in _corpseMarkers.Keys.ToArray()) TryRemoveMarker(old);
            Reopen(players);
        }
        public void Inject(Player player) => _corpseMarkers[player] = new UI.Components.MapMarker();
        private void TryAddMarker(Player player)
        {
            if (HarmonyLib.Harmony.Dispatch(AddMethod, this, player) && player != null && !_corpseMarkers.ContainsKey(player))
                _corpseMarkers.Add(player, new UI.Components.MapMarker());
        }
        private void TryRemoveMarker(Player player)
        {
            if (ThrowOnRemove) throw new Exception("map removal fault");
            if (_corpseMarkers.Remove(player, out var marker)) { RemoveCalls++; marker.Destroyed = true; }
        }
    }
}
