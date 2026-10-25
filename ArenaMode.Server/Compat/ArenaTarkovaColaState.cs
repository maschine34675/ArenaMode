using System.Reflection;
using System.Text;
using HarmonyLib;
using ArenaMode.Server.Rewards;
using SPTarkov.Server.Core.Models.Common;

namespace ArenaMode.Server.Compat;

// Cola owns a second profile file. Arena never lets a protected raid write it:
// only a validated monotonic delta may later reach the startup payout journal.
internal static class ArenaTarkovaColaState
{
    internal const string Guid = "com.tarkovacola.server";
    internal const int MaxStateBytes = 524288;
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Scope> Scopes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal);
    private static string? _folder;
    private static string? _failure;
    private static bool _installed;
    internal static string? Failure => _failure;

    internal static void Initialize()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "TarkovaCola.Server").ToArray();
        if (assemblies.Length == 0) return;
        _installed = true;
        var harmony = new Harmony("com.maschine.ArenaMode.TarkovaCola");
        try
        {
            if (assemblies.Length != 1) throw new RewardDataException("cola-duplicate-server-assembly");
            var assembly = assemblies[0];
            _folder = ResolveFolder(Directory.GetCurrentDirectory());
            var metadata = Activator.CreateInstance(assembly.GetType("TarkovaCola.Server.ModMetadata", true)!)!;
            if (Version.Parse(metadata.GetType().GetProperty("Version")!.GetValue(metadata)!.ToString()!) > new Version(2, 2, 1))
                ArenaContext.Logger?.Warning("[ArenaMode] Tarkova Cola is newer than reviewed 2.2.1; required protection contracts and existing achievement rewards match.");
            var store = assembly.GetType("TarkovaCola.Server.PerkStore", true)!;
            var achievements = assembly.GetType("TarkovaCola.Server.AchievementStore", true)!;
            var refunds = assembly.GetType("TarkovaCola.Server.RefundStore", true)!;
            Patch(store, "Load", typeof(string), [typeof(string)], nameof(Load));
            Patch(store, "Save", typeof(void), [typeof(string), typeof(string)], nameof(Save));
            Patch(achievements, "Claim", typeof(List<string>), [typeof(MongoId), typeof(IEnumerable<string>)], nameof(Claim));
            Patch(refunds, "Send", typeof(bool), [typeof(MongoId), typeof(string), typeof(int)], nameof(Refund));
            ArenaContext.Logger?.Info("[ArenaMode] Tarkova Cola: protected Arena progression enabled; bottle and grenade refunds are excluded.");
        }
        catch (Exception e)
        {
            harmony.UnpatchSelf();
            _failure = "Tarkova Cola protection is unavailable (" + e.GetType().Name + ").";
            ArenaContext.Logger?.Warning("[ArenaMode] " + _failure);
        }
        void Patch(Type type, string name, Type result, Type[] args, string prefix)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, args, null);
            if (method is null || method.ReturnType != result) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(ArenaTarkovaColaState), prefix),
                finalizer: new HarmonyMethod(typeof(ArenaTarkovaColaState), nameof(ExitOperation)));
        }
    }

    // Used before native profiles load too: metadata construction is a simple
    // reviewed value object, never an IOnLoad/service invocation.
    internal static string ResolveFolder(string root)
    {
        var matches = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "TarkovaCola.Server").ToArray();
        if (matches.Length != 1) throw new RewardDataException("cola-server-not-installed");
        var assembly = matches[0];
        var metadataType = assembly.GetType("TarkovaCola.Server.ModMetadata", true)!;
        var metadata = Activator.CreateInstance(metadataType)!;
        if (metadataType.GetProperty("ModGuid")?.GetValue(metadata)?.ToString() != Guid
            || !Version.TryParse(metadataType.GetProperty("Version")?.GetValue(metadata)?.ToString(), out var version)
            || version < new Version(2, 2, 1)) throw new RewardDataException("cola-server-contract-unavailable");
        RequireCatalog(assembly);
        var folder = Path.GetDirectoryName(assembly.Location)!;
        RewardStartupFiles.RequireLocalPath(root, folder);
        var mods = Path.GetFullPath(Path.Combine(root, "user", "mods")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(folder).StartsWith(mods, RewardStartupFiles.PathComparison))
            throw new RewardDataException("cola-server-outside-mods");
        // The reviewed store has a parameterless, path-only constructor. Verify
        // its own resolver too, so a newer build cannot silently move storage.
        var storeType = assembly.GetType("TarkovaCola.Server.PerkStore", true)!;
        var resolver = storeType.GetMethod("PathFor", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(string)], null);
        if (resolver?.ReturnType != typeof(string) || storeType.GetConstructor(Type.EmptyTypes) is null)
            throw new RewardDataException("cola-store-path-contract-unavailable");
        const string probeId = "111111111111111111111111";
        var resolved = resolver.Invoke(Activator.CreateInstance(storeType), [probeId]) as string;
        if (resolved is null || !string.Equals(Path.GetFullPath(resolved),
                Path.GetFullPath(Path.Combine(folder, "profiles", probeId + ".json")), RewardStartupFiles.PathComparison))
            throw new RewardDataException("cola-store-path-changed");
        return folder;
    }

    // No upper version gate: newer builds may keep these contracts. Never use a
    // stale reward table if a reviewed achievement's actual reward has changed.
    private static void RequireCatalog(Assembly assembly)
    {
        var data = assembly.GetType("TarkovaCola.Server.AchievementData", true)!;
        var definitions = data.GetProperty("All", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(null) as System.Collections.IEnumerable ?? throw new RewardDataException("cola-reward-catalog-unavailable");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var type = definition.GetType();
            var id = type.GetField("Id")?.GetValue(definition) as string;
            if (id is null || !seen.Add(id)) throw new RewardDataException("cola-reward-catalog-invalid");
            if (!RewardColaCatalog.ById.TryGetValue(id, out var known)) continue;
            if (type.GetField("Key")?.GetValue(definition) as string != known.ServerKey)
                throw new RewardDataException("cola-reward-catalog-changed");
            var rewards = type.GetField("Rewards")?.GetValue(definition) as System.Collections.IEnumerable
                ?? throw new RewardDataException("cola-reward-catalog-unavailable");
            var items = new List<RewardColaItem>();
            foreach (var reward in rewards)
            {
                var tuple = reward.GetType();
                if (tuple.GetField("Item1")?.GetValue(reward) is not string template
                    || tuple.GetField("Item2")?.GetValue(reward) is not int count)
                    throw new RewardDataException("cola-reward-catalog-unavailable");
                items.Add(new(template, count));
            }
            if (!known.Items.SequenceEqual(items)) throw new RewardDataException("cola-reward-catalog-changed");
        }
        if (RewardColaCatalog.ById.Keys.Any(id => !seen.Contains(id))) throw new RewardDataException("cola-reward-catalog-incomplete");
    }

    internal static void Begin(string id, string arm, string mode, string? expectedBaseline)
    {
        if (!_installed)
        {
            if (expectedBaseline is not null) throw new RewardDataException("cola-server-not-installed");
            return;
        }
        lock (Sync)
        {
            if (_failure is not null || _folder is null) throw new RewardDataException("cola-protection-unavailable");
            if (!RewardIdentity.IsHex(id, 24) || !RewardIdentity.IsGuid(arm) || expectedBaseline is null)
                throw new RewardDataException("cola-entry-client-mismatch");
            ValidateState(expectedBaseline);
            if (Scopes.TryGetValue(id, out var current))
            {
                if (current.Arm == arm) return;
                throw new RewardDataException("cola-previous-scope-not-released");
            }
            var baseline = ReadState(Directory.GetCurrentDirectory(), _folder, id);
            if (!RewardRuntimeFiles.SameProfile(baseline, expectedBaseline))
                throw new RewardDataException("cola-entry-state-changed");
            // Previously signed results can be visible in later Arena rounds,
            // but never leak into the ordinary menu's foreign state/save route.
            var view = Pending.GetValueOrDefault(id) is { } prior
                ? RewardColaDelta.Merge(baseline, prior) : baseline;
            Scopes.Add(id, new Scope(arm, mode == "zombie" ? "zombies" : mode, baseline, view));
        }
    }

    internal static string? Capture(string id, string arm, string mode, string? observed)
    {
        mode = mode == "zombie" ? "zombies" : mode;
        lock (Sync)
        {
            if (!Scopes.TryGetValue(id, out var scope))
            {
                if (observed is not null) throw new RewardDataException("cola-capture-without-scope");
                return null;
            }
            if (scope.Arm != arm || scope.Mode != mode) throw new RewardDataException("cola-capture-scope-mismatch");
            if (mode != "zombies") return null;
            if (observed is null) throw new RewardDataException("cola-final-state-missing");
            ValidateState(observed);
            return RewardColaDelta.Capture(scope.View, observed);
        }
    }

    internal static void Published(string id, string arm, string? delta)
    {
        lock (Sync)
        {
            if (delta is null || !Scopes.TryGetValue(id, out var scope)) return;
            if (scope.Arm != arm) throw new RewardDataException("cola-published-scope-mismatch");
            // Recompute an accumulated delta against the original ordinary
            // baseline, rather than importing any raw private mod document.
            var merged = RewardColaDelta.Merge(scope.View, delta);
            var cumulative = RewardColaDelta.Capture(scope.Baseline, merged);
            if (cumulative is not null) Pending[id] = cumulative;
        }
    }

    internal static void Release(string id, string? arm = null)
    {
        lock (Sync)
            if (Scopes.TryGetValue(id, out var scope) && (arm is null || scope.Arm == arm)) Scopes.Remove(id);
    }

    internal static string ReadState(string root, string folder, string id)
    {
        if (!RewardIdentity.IsHex(id, 24)) throw new RewardDataException("cola-invalid-profile");
        var path = Path.Combine(folder, "profiles", id + ".json");
        if (!RewardRuntimeFiles.Exists(root, path)) return "{}";
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaxStateBytes) throw new RewardDataException("cola-state-too-large");
        using var reader = new StreamReader(file);
        var value = reader.ReadToEnd();
        ValidateState(value);
        return value;
    }

    internal static void ValidateState(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxStateBytes) throw new RewardDataException("cola-state-too-large");
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            RewardStartupFiles.RejectDuplicateProperties(document.RootElement);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) throw new RewardDataException("cola-state-not-object");
        }
        catch (System.Text.Json.JsonException) { throw new RewardDataException("cola-state-invalid-json"); }
    }

    private static bool Load(string __0, ref string __result, out bool __state)
    {
        Monitor.Enter(Sync); __state = true;
        if (!Scopes.TryGetValue(__0, out var scope)) return true;
        __result = scope.View;
        return false;
    }
    private static bool Save(string __0, out bool __state)
    {
        Monitor.Enter(Sync); __state = true;
        return !Scopes.ContainsKey(__0);
    }
    private static bool Claim(MongoId __0, ref List<string> __result, out bool __state)
    {
        Monitor.Enter(Sync); __state = true;
        if (!Scopes.ContainsKey(__0.ToString())) return true;
        __result = [];
        return false;
    }
    private static bool Refund(MongoId __0, ref bool __result, out bool __state)
    {
        Monitor.Enter(Sync); __state = true;
        if (!Scopes.ContainsKey(__0.ToString())) return true;
        __result = false;
        return false;
    }
    // Hold through the entire synchronous foreign method, including its disk
    // write: arm cannot overtake a save whose prefix has already returned.
    private static void ExitOperation(bool __state) { if (__state) Monitor.Exit(Sync); }
    private sealed record Scope(string Arm, string Mode, string Baseline, string View);
}
