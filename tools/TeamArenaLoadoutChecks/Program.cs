using ArenaMode.Kits;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

var check = new CheckRunner();

var runtimeUpd = new JObject
{
    ["Repairable"] = new JObject
    {
        ["Durability"] = new JValue(92.61f),
        ["MaxDurability"] = new JValue(92.61f),
    },
    ["FireMode"] = new JObject
    {
        ["FireMode"] = "fullauto",
    },
};
JToken detachedUpd = JToken.Parse(runtimeUpd.ToString(Formatting.None));
check.Equal(typeof(float), ((JValue)runtimeUpd["Repairable"]["Durability"]).Value.GetType(),
    "EFT repairable durability begins as a Single JValue");
check.Equal(typeof(double), ((JValue)detachedUpd["Repairable"]["Durability"]).Value.GetType(),
    "JSON round-trip materializes decimal durability as a Double JValue");
check.False(JToken.DeepEquals(runtimeUpd, detachedUpd),
    "raw JToken equality reproduces the Single-to-Double snapshot failure");
check.True(TeamArenaJsonEquivalence.AreEquivalent(runtimeUpd, detachedUpd),
    "wire-identical Single and Double JSON numbers survive snapshot validation");

JToken changedDurability = detachedUpd.DeepClone();
changedDurability["Repairable"]["Durability"] = 92.60;
check.False(TeamArenaJsonEquivalence.AreEquivalent(runtimeUpd, changedDurability),
    "a real durability change remains visible after numeric normalization");

JToken changedFireMode = detachedUpd.DeepClone();
changedFireMode["FireMode"]["FireMode"] = "single";
check.False(TeamArenaJsonEquivalence.AreEquivalent(runtimeUpd, changedFireMode),
    "a non-numeric upd change remains visible after normalization");

var largeCounter = new JObject { ["Counter"] = 9007199254740993L };
var changedLargeCounter = new JObject { ["Counter"] = 9007199254740992L };
check.False(TeamArenaJsonEquivalence.AreEquivalent(largeCounter, changedLargeCounter),
    "numeric normalization does not lose Int64 precision through Double conversion");
check.True(TeamArenaJsonEquivalence.AreEquivalent(null, null), "two absent JSON values remain equivalent");
check.True(TeamArenaJsonEquivalence.AreEquivalent(null, JValue.CreateNull()),
    "missing and explicit null follow EFT's UnparsedData null semantics");
check.False(TeamArenaJsonEquivalence.AreEquivalent(runtimeUpd, null),
    "an absent JSON value cannot equal a present value");

var source = new[]
{
    Node("equipment", null, null, "equipment-tpl"),
    Node("pockets", "equipment", "Pockets", "pockets-tpl"),
    Node("normal-med", "pockets", "main", "med-tpl"),
    Node("special", "pockets", "SpecialSlot1", "beacon-tpl"),
    Node("special-child", "special", "mod", "battery-tpl"),
    Node("secure", "equipment", "SecuredContainer", "secure-tpl"),
    Node("secure-child", "secure", "main", "key-tpl"),
    Node("scabbard", "equipment", "Scabbard", "knife-tpl"),
    Node("weapon", "equipment", "FirstPrimaryWeapon", "weapon-tpl"),
    Node("mag", "weapon", "mod_magazine", "mag-tpl"),
};

check.True(
    TeamArenaLoadoutSnapshotPolicy.TryFilterProtectedSubtrees(
        source,
        new[] { "special", "secure", "scabbard" },
        out HashSet<string> included,
        out string filterReason),
    $"protected-subtree filter succeeds: {filterReason}");
check.Equal(5, included.Count, "filter retains exactly ordinary root, pockets, content and weapon graph");
check.True(included.SetEquals(new[] { "equipment", "pockets", "normal-med", "weapon", "mag" }),
    "filter removes each protected root and every descendant");
check.True(included.Contains("pockets"), "ordinary Pockets ancestor remains in the snapshot");
check.False(included.Contains("special-child"), "special-slot child cannot leak into snapshot");
check.False(included.Contains("secure-child"), "secure-container child cannot leak into snapshot");

var reversed = source.Reverse().ToArray();
check.True(
    TeamArenaLoadoutSnapshotPolicy.TryFilterProtectedSubtrees(
        reversed,
        new[] { "special", "secure", "scabbard" },
        out HashSet<string> reversedIncluded,
        out string reversedReason),
    $"filter does not trust FlatItem ordering: {reversedReason}");
check.True(reversedIncluded.SetEquals(included), "reordered input produces the same snapshot set");

check.False(
    TeamArenaLoadoutSnapshotPolicy.TryFilterProtectedSubtrees(
        source,
        new[] { "does-not-exist" },
        out _,
        out string missingProtectedReason),
    "missing protected root fails closed");
check.Contains("absent", missingProtectedReason, "missing protected root explains the failure");
check.False(
    TeamArenaLoadoutSnapshotPolicy.TryFilterProtectedSubtrees(
        source,
        new[] { "equipment" },
        out _,
        out string rootProtectedReason),
    "equipment root cannot be filtered away");
check.Contains("root", rootProtectedReason, "protected equipment root explains the failure");

var orphaned = source.Select(node => node.Id == "mag"
    ? Node(node.Id, "missing", node.SlotId, node.TemplateId)
    : node).ToArray();
check.False(
    TeamArenaLoadoutSnapshotPolicy.TryFilterProtectedSubtrees(
        orphaned,
        Array.Empty<string>(),
        out _,
        out string orphanReason),
    "orphaned capture graph is refused");
check.Contains("missing parent", orphanReason, "orphan failure names missing parent");

var duplicate = source.Concat(new[] { Node("mag", "weapon", "other", "mag-tpl") }).ToArray();
check.False(
    TeamArenaLoadoutSnapshotPolicy.TryFilterProtectedSubtrees(
        duplicate,
        Array.Empty<string>(),
        out _,
        out string duplicateReason),
    "duplicate item IDs are refused");
check.Contains("duplicate", duplicateReason, "duplicate failure is diagnostic");

var ordinarySource = source.Where(node => included.Contains(node.Id)).ToArray();
var idMap = ordinarySource.ToDictionary(node => node.Id, node => $"fresh-{node.Id}", StringComparer.Ordinal);
var fresh = ordinarySource.Select(node => Node(
    idMap[node.Id],
    node.ParentId == null ? null : idMap[node.ParentId],
    node.SlotId,
    node.TemplateId)).ToArray();
check.True(
    TeamArenaLoadoutSnapshotPolicy.TryValidateFreshRemap(ordinarySource, fresh, idMap, out string remapReason),
    $"one-to-one fresh-ID graph is accepted: {remapReason}");

var reusedMap = new Dictionary<string, string>(idMap, StringComparer.Ordinal)
{
    ["normal-med"] = "normal-med",
};
var reused = fresh.Select(node => node.Id == "fresh-normal-med"
    ? Node("normal-med", node.ParentId, node.SlotId, node.TemplateId)
    : node).ToArray();
check.False(
    TeamArenaLoadoutSnapshotPolicy.TryValidateFreshRemap(ordinarySource, reused, reusedMap, out string reusedReason),
    "a captured ID reused at runtime is refused");
check.Contains("fresh", reusedReason, "ID-reuse failure says a fresh ID is required");

var badParent = fresh.Select(node => node.Id == "fresh-mag"
    ? Node(node.Id, "fresh-pockets", node.SlotId, node.TemplateId)
    : node).ToArray();
check.False(
    TeamArenaLoadoutSnapshotPolicy.TryValidateFreshRemap(ordinarySource, badParent, idMap, out string parentReason),
    "changed parent edge is refused");
check.Contains("parent", parentReason, "changed-parent failure is diagnostic");

var badSlot = fresh.Select(node => node.Id == "fresh-mag"
    ? Node(node.Id, node.ParentId, "wrong-slot", node.TemplateId)
    : node).ToArray();
check.False(
    TeamArenaLoadoutSnapshotPolicy.TryValidateFreshRemap(ordinarySource, badSlot, idMap, out string slotReason),
    "changed slot edge is refused");
check.Contains("slot", slotReason, "changed-slot failure is diagnostic");

var badTemplate = fresh.Select(node => node.Id == "fresh-mag"
    ? Node(node.Id, node.ParentId, node.SlotId, "other-template")
    : node).ToArray();
check.False(
    TeamArenaLoadoutSnapshotPolicy.TryValidateFreshRemap(ordinarySource, badTemplate, idMap, out string templateReason),
    "changed template is refused");
check.Contains("template", templateReason, "changed-template failure is diagnostic");

var capturedProtected = source.Where(node => !included.Contains(node.Id)).Select(node => node.Id).ToArray();
check.True(
    TeamArenaLoadoutSnapshotPolicy.TryBuildRuntimeRemap(
        included,
        capturedProtected,
        ordinarySource.ToDictionary(node => node.Id, node => node.Id, StringComparer.Ordinal),
        capturedProtected,
        idMap,
        out Dictionary<string, string> currentToFresh,
        out string runtimeRemapReason),
    $"canonical IDs join to first fresh runtime IDs: {runtimeRemapReason}");
check.Equal(included.Count, currentToFresh.Count, "metadata remap covers every ordinary captured item");
check.Equal("fresh-weapon", currentToFresh["weapon"], "metadata remap sends canonical weapon ID to fresh weapon ID");

var combatMovedToProtected = capturedProtected.Concat(new[] { "normal-med" }).ToArray();
check.False(
    TeamArenaLoadoutSnapshotPolicy.TryValidateProtectedBoundary(
        included,
        capturedProtected,
        ordinarySource.ToDictionary(node => node.Id, node => node.Id, StringComparer.Ordinal),
        combatMovedToProtected,
        out string combatProtectedReason),
    "combat item moved into Secure/Special protection fails closed");
check.Contains("combat items moved", combatProtectedReason, "combat-to-protected failure is diagnostic");

var unknownMovedToProtected = capturedProtected.Concat(new[] { "split-or-new-item" }).ToArray();
check.False(
    TeamArenaLoadoutSnapshotPolicy.TryValidateProtectedBoundary(
        included,
        capturedProtected,
        ordinarySource.ToDictionary(node => node.Id, node => node.Id, StringComparer.Ordinal),
        unknownMovedToProtected,
        out string unexpectedProtectedReason),
    "unknown or split item entering protected slots fails closed");
check.Contains("uncaptured items entered", unexpectedProtectedReason, "unexpected protected item is diagnostic");

var protectedItemRemoved = capturedProtected.Where(id => id != "secure-child").ToArray();
check.False(
    TeamArenaLoadoutSnapshotPolicy.TryValidateProtectedBoundary(
        included,
        capturedProtected,
        ordinarySource.ToDictionary(node => node.Id, node => node.Id, StringComparer.Ordinal),
        protectedItemRemoved,
        out string protectedRemovedReason),
    "originally protected child moved out or consumed fails closed");
check.Contains("left or disappeared", protectedRemovedReason, "protected-removal failure is diagnostic");

var secondRuntimeMap = idMap.ToDictionary(entry => entry.Key, entry => $"second-{entry.Key}", StringComparer.Ordinal);
check.True(
    TeamArenaLoadoutSnapshotPolicy.TryBuildRuntimeRemap(
        included,
        capturedProtected,
        idMap,
        capturedProtected,
        secondRuntimeMap,
        out Dictionary<string, string> secondCurrentToFresh,
        out string secondRuntimeReason),
    $"later epoch joins prior fresh IDs to next fresh IDs: {secondRuntimeReason}");
check.Equal("second-weapon", secondCurrentToFresh["fresh-weapon"],
    "later metadata remap follows canonical-to-runtime lineage");

var gate = new TeamArenaLoadoutRestoreGate();
check.False(gate.TryBeginPrepare(1, out string unarmedReason), "prepare cannot begin before explicit arm");
check.Contains("not armed", unarmedReason, "unarmed restore explains gate state");
check.False(gate.TryArm(0, out string zeroReason), "epoch zero is invalid");
check.Contains("positive", zeroReason, "invalid epoch explains positive requirement");
check.True(gate.TryArm(1, out string armReason), $"first epoch arms: {armReason}");
check.False(gate.TryArm(2, out string doubleArmReason), "second arm cannot replace an active lease");
check.Contains("already owns", doubleArmReason, "double-arm failure names current owner");
check.False(gate.TryBeginPrepare(2, out string wrongEpochReason), "wrong epoch cannot consume the lease");
check.Contains("armed for 1", wrongEpochReason, "wrong-epoch failure names the owner");
check.True(gate.TryBeginPrepare(1, out string beginReason), $"armed epoch prepares exactly once: {beginReason}");
check.False(gate.TryBeginPrepare(1, out string duplicateBeginReason), "same epoch cannot prepare twice");
check.Contains("not armed", duplicateBeginReason, "duplicate begin sees applying state");
gate.CompletePrepare(1, succeeded: true, failure: null);
check.False(gate.TryArm(2, out string preparedArmReason), "prepared epoch cannot be replaced before commit");
check.Contains("already owns", preparedArmReason, "prepared lease keeps epoch ownership");
check.False(gate.TryBeginCommit(2, out string wrongCommitReason), "wrong epoch cannot commit prepared inventory");
check.Contains("prepared restore for 1", wrongCommitReason, "wrong commit names prepared epoch");
check.True(gate.TryBeginCommit(1, out string commitReason), $"prepared epoch commits exactly once: {commitReason}");
check.False(gate.TryBeginCommit(1, out string duplicateCommitReason), "same prepared inventory cannot commit twice");
check.Contains("not prepared", duplicateCommitReason, "duplicate commit sees committing state");
gate.CompleteCommit(1, succeeded: true, failure: null);
check.False(gate.TryArm(1, out string staleReason), "completed epoch cannot be replayed");
check.Contains("not newer", staleReason, "stale epoch failure names monotonic rule");
check.True(gate.TryArm(2, out string secondArmReason), $"newer epoch can restore the same snapshot: {secondArmReason}");
check.True(gate.TryBeginPrepare(2, out string secondBeginReason), $"second epoch prepares: {secondBeginReason}");
gate.CompletePrepare(2, succeeded: true, failure: null);
check.True(gate.TryBeginCommit(2, out string secondCommitReason), $"second epoch commits: {secondCommitReason}");
gate.CompleteCommit(2, succeeded: false, failure: "synthetic inventory error");
check.True(gate.IsPoisoned, "inventory failure poisons the match snapshot");
check.False(gate.TryArm(3, out string poisonReason), "poisoned snapshot cannot arm later rounds");
check.Contains("synthetic inventory error", poisonReason, "poison retains the first destructive failure");

var prepareFailureGate = new TeamArenaLoadoutRestoreGate();
check.True(prepareFailureGate.TryArm(5, out _), "independent prepare-failure gate arms");
check.True(prepareFailureGate.TryBeginPrepare(5, out _), "independent prepare-failure gate begins");
prepareFailureGate.CompletePrepare(5, succeeded: false, failure: "synthetic graph failure");
check.True(prepareFailureGate.IsPoisoned, "detached graph failure poisons before respawn release");
check.False(prepareFailureGate.TryArm(6, out string preparePoisonReason), "prepare failure blocks later epochs");
check.Contains("synthetic graph failure", preparePoisonReason, "prepare poison retains graph failure");

string implementationPath = Path.Combine(AppContext.BaseDirectory, "TeamArenaLoadoutSnapshot.cs.txt");
string implementation = File.ReadAllText(implementationPath);
int commitStart = implementation.IndexOf("internal bool TryCommitPrepared(", StringComparison.Ordinal);
int prepareCoreStart = implementation.IndexOf("private TeamArenaPreparedLoadoutRestore PrepareCore(", StringComparison.Ordinal);
check.True(commitStart >= 0 && prepareCoreStart > commitStart, "source exposes commit and detached prepare boundaries");
string commitSection = implementation.Substring(commitStart, prepareCoreStart - commitStart);
check.DoesNotContain("FlatItemsToTree", commitSection,
    "respawn callback commit cannot deserialize or reconstruct the item graph");
check.Contains("TreeToFlatItems(equipment)", implementation, "capture uses SPT TreeToFlatItems");
check.Contains("JsonConvert.SerializeObject(filtered, Formatting.None, EftJsonConverters.Converters)", implementation,
    "capture crosses an in-memory JSON boundary with EFT's native UnparsedData converter");
check.Contains("JsonConvert.DeserializeObject<FlatItem[]>(json, EftJsonConverters.Converters)", implementation,
    "capture validation reads detached FlatItems with EFT's native UnparsedData converter");
check.Contains("JsonConvert.DeserializeObject<FlatItem[]>(_flatItemJson, EftJsonConverters.Converters)", implementation,
    "each restore reads detached FlatItems with EFT's native UnparsedData converter");
check.DoesNotContain("JsonConvert.DefaultSettings", implementation,
    "snapshot conversion never changes Newtonsoft's process-wide settings");
check.Contains("TeamArenaJsonEquivalence.AreEquivalent", implementation,
    "location and upd compare their normalized raw JSON tokens");
check.Contains("public bool TryPrepare(", implementation, "detached prepare is an explicit pre-respawn phase");
check.Contains("class TeamArenaPreparedLoadoutRestore", implementation, "prepared graph has a one-shot commit handle");
check.Contains("factory.NextId", implementation, "runtime items receive ItemFactory IDs");
check.Contains("FlatItemsToTree(remapped, silentMode: true)", implementation, "restore uses exact SPT FlatItemsToTree API");
check.Contains("tree.DeserializationErrors", implementation, "restore checks every SPT deserialization error");
check.Contains("newEquipment.GetAllItems()", implementation, "restore checks root reachability");
check.Contains("factory.TreeToFlatItems(newEquipment)", implementation, "restore re-flattens the detached graph");
check.Contains("EquipmentSlot.SecuredContainer", implementation, "secure container is protected");
check.Contains("EquipmentSlot.Scabbard", implementation, "scabbard is protected");
check.Contains("EquipmentSlot.ArmBand", implementation, "armband is protected");
check.Contains("slot.IsSpecial", implementation, "dynamic Pockets special slots are protected");
check.Contains("RollBackTransfers", implementation, "protected transfer has reverse rollback");
check.Contains("EquipmentField(inventory) = newEquipment", implementation, "equipment commit uses the readonly FieldRef");
check.Contains("inventory.TotalWeight.SetDirty()", implementation, "equipment commit invalidates the cached normal carry weight");
check.Contains("inventory.TotalWeightEliteSkill.SetDirty()", implementation, "equipment commit invalidates the cached elite-skill carry weight");
check.Contains("ProfileInventorySanitizer.Sanitize(profile)", implementation, "equipment commit sanitizes stale profile references");
check.Contains("sanitizerState.ApplyRemapped", implementation, "successful sanitize restores canonical-to-fresh metadata");
check.Contains("_fastAccess.BoundItems", implementation, "quickslots are rebuilt from pre-commit bindings");
check.Contains("FavoriteItemsStorage.Add(mappedId)", implementation, "favorites are restored with fresh IDs");
check.Contains("DiscardLimits.Add(mappedId", implementation, "discard limits are restored with fresh IDs");
check.Contains("CheckedMagazines.Add(mappedId", implementation, "checked magazines are restored with fresh IDs");
check.Contains("CheckedChambers.Add(mappedId)", implementation, "checked chambers are restored with fresh IDs");
check.Contains("FindProtectedItemIds", implementation, "capture identity is checked against current protected descendants");
check.Contains("_capturedIncludedIds", implementation, "snapshot retains canonical ordinary item IDs");
check.Contains("_capturedProtectedIds", implementation, "snapshot retains canonical protected item IDs");
check.Contains("EquipmentField(inventory) = oldEquipment", implementation, "failed commit restores the old equipment FieldRef");
check.Contains("sanitizerState.Restore", implementation, "failed sanitizer restores sanitized metadata");
check.DoesNotContain("System.IO", implementation, "production snapshot has no file API namespace");
check.DoesNotContain("File.", implementation, "production snapshot performs no file I/O");
check.DoesNotContain("Http", implementation, "production snapshot performs no HTTP I/O");
check.DoesNotContain("WebRequest", implementation, "production snapshot performs no network request");

Console.WriteLine($"Team Arena loadout checks passed: {check.Count} assertions.");
return;

static TeamArenaLoadoutSnapshotPolicy.FlatNode Node(string id, string parent, string slot, string template)
    => new(id, parent, slot, template);

internal sealed class CheckRunner
{
    public int Count { get; private set; }

    public void True(bool condition, string message)
    {
        Count++;
        if (!condition)
            throw new InvalidOperationException($"CHECK FAILED: {message}");
    }

    public void False(bool condition, string message) => True(!condition, message);

    public void Equal<T>(T expected, T actual, string message)
    {
        Count++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"CHECK FAILED: {message}; expected {expected}, got {actual}");
    }

    public void Contains(string expected, string actual, string message)
    {
        Count++;
        if (actual == null || !actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"CHECK FAILED: {message}; missing '{expected}' in '{actual ?? "<null>"}'");
    }

    public void DoesNotContain(string unexpected, string actual, string message)
    {
        Count++;
        if (actual?.Contains(unexpected, StringComparison.Ordinal) == true)
            throw new InvalidOperationException($"CHECK FAILED: {message}; found '{unexpected}'");
    }
}
