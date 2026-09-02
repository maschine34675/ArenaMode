using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using JsonType;
using Newtonsoft.Json;
using Redeploy.Core.Inventory;
using System;
using System.Collections.Generic;
using System.Linq;
using EftFastAccess = EFT.InventoryLogic.FastAccess;
using EftInventory = EFT.InventoryLogic.Inventory;

namespace ArenaMode.Kits;

/// <summary>
/// Immutable, process-local copy of the PMC equipment present when a Tactical team
/// match starts. The snapshot deliberately excludes items that must be transferred
/// from the just-dead runtime body (secure container, scabbard, armband and special
/// pocket slots). Every restore deserializes a detached graph and assigns fresh IDs.
/// Nothing is written to the profile service, disk or network.
/// </summary>
public sealed class TeamArenaLoadoutSnapshot
{
    private static readonly AccessTools.FieldRef<EftInventory, InventoryEquipment> EquipmentField =
        AccessTools.FieldRefAccess<EftInventory, InventoryEquipment>(nameof(EftInventory.Equipment));

    private static readonly AccessTools.FieldRef<EftInventory, EftFastAccess> FastAccessField =
        AccessTools.FieldRefAccess<EftInventory, EftFastAccess>(nameof(EftInventory.FastAccess));

    private static readonly EquipmentSlot[] RetainedEquipmentSlots =
    {
        EquipmentSlot.SecuredContainer,
        EquipmentSlot.Scabbard,
        EquipmentSlot.ArmBand,
    };

    private readonly string _flatItemJson;
    private readonly TeamArenaLoadoutRestoreGate _restoreGate = new();
    private readonly object _identitySync = new();
    private readonly HashSet<string> _capturedIncludedIds;
    private readonly HashSet<string> _capturedProtectedIds;
    private Dictionary<string, string> _runtimeByCapturedId;

    private TeamArenaLoadoutSnapshot(
        string flatItemJson,
        IReadOnlyCollection<string> capturedIncludedIds,
        IReadOnlyCollection<string> capturedProtectedIds)
    {
        _flatItemJson = flatItemJson;
        _capturedIncludedIds = new HashSet<string>(capturedIncludedIds, StringComparer.Ordinal);
        _capturedProtectedIds = new HashSet<string>(capturedProtectedIds, StringComparer.Ordinal);
        _runtimeByCapturedId = _capturedIncludedIds.ToDictionary(id => id, id => id, StringComparer.Ordinal);
        CapturedItemCount = _capturedIncludedIds.Count;
    }

    public int CapturedItemCount { get; }
    public int ProtectedItemCount => _capturedProtectedIds.Count;

    /// <summary>Diagnostic only; the JSON itself intentionally never leaves process memory.</summary>
    public int SerializedCharacterCount => _flatItemJson.Length;

    public bool IsPoisoned => _restoreGate.IsPoisoned;

    /// <summary>
    /// Captures the live equipment graph. Protected roots are found from the actual
    /// slot objects (including dynamically marked special pocket slots), then their
    /// complete descendant sets are removed from the serialized graph.
    /// </summary>
    public static bool TryCapture(Profile profile, out TeamArenaLoadoutSnapshot snapshot, out string reason)
    {
        snapshot = null;
        reason = null;

        try
        {
            InventoryEquipment equipment = profile?.Inventory?.Equipment;
            ItemFactory factory = Singleton<ItemFactory>.Instance;
            if (equipment == null || factory == null)
            {
                reason = equipment == null
                    ? "runtime profile has no equipment root"
                    : "SPT ItemFactory is unavailable";
                return false;
            }

            FlatItem[] flatItems = factory.TreeToFlatItems(equipment);
            if (flatItems == null || flatItems.Length == 0)
            {
                reason = "SPT produced an empty equipment snapshot";
                return false;
            }

            HashSet<string> protectedRootIds = FindProtectedRootIds(equipment);
            TeamArenaLoadoutSnapshotPolicy.FlatNode[] sourceNodes = ToPolicyNodes(flatItems);
            if (!TeamArenaLoadoutSnapshotPolicy.TryFilterProtectedSubtrees(
                    sourceNodes,
                    protectedRootIds,
                    out HashSet<string> includedIds,
                    out reason))
            {
                return false;
            }

            FlatItem[] filtered = flatItems
                .Where(item => includedIds.Contains(item._id.ToString()))
                .ToArray();

            // The JSON string is the ownership boundary: later rounds never retain
            // references to the match-entry Item objects or their mutable upd/location.
            string json = JsonConvert.SerializeObject(filtered, Formatting.None, EftJsonConverters.Converters);
            FlatItem[] detached = JsonConvert.DeserializeObject<FlatItem[]>(json, EftJsonConverters.Converters);
            if (detached == null
                || !TryValidateJsonRoundTrip(filtered, detached, out reason))
            {
                reason ??= "FlatItem JSON did not deserialize into a detached equipment graph";
                return false;
            }

            var allOriginalIds = new HashSet<string>(
                sourceNodes.Select(node => node.Id),
                StringComparer.Ordinal);
            allOriginalIds.ExceptWith(includedIds);
            snapshot = new TeamArenaLoadoutSnapshot(json, includedIds, allOriginalIds);
            return true;
        }
        catch (Exception ex)
        {
            reason = $"loadout snapshot capture failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Arms one strictly increasing round epoch. The following restore is one-shot;
    /// an inventory or rollback failure permanently poisons this match snapshot.
    /// </summary>
    public bool TryArm(long roundEpoch, out string reason)
        => _restoreGate.TryArm(roundEpoch, out reason);

    /// <summary>
    /// Rebuilds and validates the complete detached graph while the dead player's old
    /// owner/body still exists. Callers must finish this phase before releasing a held
    /// respawn pipeline; the later equipment strategy callback only commits the already
    /// prepared transaction.
    /// </summary>
    public bool TryPrepare(
        Profile currentProfile,
        long roundEpoch,
        out TeamArenaPreparedLoadoutRestore prepared,
        out string reason)
    {
        prepared = null;
        if (!_restoreGate.TryBeginPrepare(roundEpoch, out reason))
            return false;

        try
        {
            prepared = PrepareCore(currentProfile, roundEpoch);
            _restoreGate.CompletePrepare(roundEpoch, succeeded: true, failure: null);
            reason = null;
            return true;
        }
        catch (Exception ex)
        {
            reason = $"loadout prepare failed: {ex.Message}";
            _restoreGate.CompletePrepare(roundEpoch, succeeded: false, failure: reason);
            return false;
        }
    }

    internal bool TryCommitPrepared(
        TeamArenaPreparedLoadoutRestore prepared,
        Profile runtimeProfile,
        out TeamArenaLoadoutRestoreResult result)
    {
        long roundEpoch = prepared?.RoundEpoch ?? 0;
        if (prepared == null || !ReferenceEquals(prepared.Owner, this))
        {
            result = TeamArenaLoadoutRestoreResult.Failed(roundEpoch, "prepared restore belongs to another snapshot", true);
            return false;
        }

        if (!_restoreGate.TryBeginCommit(roundEpoch, out string gateReason))
        {
            result = TeamArenaLoadoutRestoreResult.Failed(roundEpoch, gateReason, rollbackComplete: true);
            return false;
        }

        bool transactionCommitted = false;
        try
        {
            if (runtimeProfile?.Inventory == null
                || !ReferenceEquals(runtimeProfile.Inventory, prepared.SourceInventory)
                || !ReferenceEquals(runtimeProfile.Inventory.Equipment, prepared.SourceEquipment))
            {
                throw new InvalidOperationException("runtime profile/equipment changed after detached prepare");
            }

            HashSet<string> currentProtectedIds = FindProtectedItemIds(prepared.SourceEquipment);
            if (!currentProtectedIds.SetEquals(prepared.ExpectedProtectedIds))
                throw new InvalidOperationException("protected equipment graph changed after detached prepare");

            if (!TeamArenaLoadoutSnapshotPolicy.TryBuildRuntimeRemap(
                    _capturedIncludedIds,
                    _capturedProtectedIds,
                    prepared.PreviousRuntimeByCapturedId,
                    currentProtectedIds,
                    prepared.NextRuntimeByCapturedId,
                    out Dictionary<string, string> currentToFresh,
                    out string identityReason))
            {
                throw new InvalidOperationException($"protected identity validation failed at commit: {identityReason}");
            }

            if (!DictionaryEqual(currentToFresh, prepared.NextRuntimeByPreviousRuntimeId))
                throw new InvalidOperationException("runtime ID remap changed after detached prepare");

            CommitEquipmentTransaction(
                runtimeProfile,
                prepared.SourceEquipment,
                prepared.PreparedEquipment,
                currentToFresh);
            transactionCommitted = true;

            lock (_identitySync)
                _runtimeByCapturedId = prepared.NextRuntimeByCapturedId;

            _restoreGate.CompleteCommit(roundEpoch, succeeded: true, failure: null);
            result = TeamArenaLoadoutRestoreResult.Success(roundEpoch, prepared.PreparedItemCount);
            return true;
        }
        catch (LoadoutRestoreException ex)
        {
            _restoreGate.CompleteCommit(roundEpoch, succeeded: false, failure: ex.Message);
            result = TeamArenaLoadoutRestoreResult.Failed(roundEpoch, ex.Message, ex.RollbackComplete);
            return false;
        }
        catch (Exception ex)
        {
            _restoreGate.CompleteCommit(roundEpoch, succeeded: false, failure: ex.Message);
            result = TeamArenaLoadoutRestoreResult.Failed(
                roundEpoch,
                transactionCommitted
                    ? $"loadout transaction committed but finalization failed: {ex.Message}"
                    : $"prepared loadout commit failed before transaction mutation: {ex.Message}",
                rollbackComplete: !transactionCommitted);
            return false;
        }
    }

    private TeamArenaPreparedLoadoutRestore PrepareCore(Profile profile, long roundEpoch)
    {
        EftInventory inventory = profile?.Inventory;
        InventoryEquipment oldEquipment = inventory?.Equipment;
        ItemFactory factory = Singleton<ItemFactory>.Instance;
        if (inventory == null || oldEquipment == null || factory == null)
            throw new InvalidOperationException("runtime profile, equipment or ItemFactory is unavailable");

        Dictionary<string, string> previousRuntimeByCapturedId;
        lock (_identitySync)
        {
            previousRuntimeByCapturedId = new Dictionary<string, string>(
                _runtimeByCapturedId,
                StringComparer.Ordinal);
        }

        HashSet<string> currentProtectedIds = FindProtectedItemIds(oldEquipment);
        if (!TeamArenaLoadoutSnapshotPolicy.TryValidateProtectedBoundary(
                _capturedIncludedIds,
                _capturedProtectedIds,
                previousRuntimeByCapturedId,
                currentProtectedIds,
                out string protectedReason))
        {
            throw new InvalidOperationException($"protected identity validation failed: {protectedReason}");
        }

        FlatItem[] source = JsonConvert.DeserializeObject<FlatItem[]>(_flatItemJson, EftJsonConverters.Converters)
                            ?? throw new InvalidOperationException("in-memory FlatItem JSON is invalid");
        if (source.Length != CapturedItemCount)
            throw new InvalidOperationException("in-memory FlatItem JSON changed item count");

        TeamArenaLoadoutSnapshotPolicy.FlatNode[] sourceNodes = ToPolicyNodes(source);
        FlatItem[] remapped = RemapWithFreshIds(source, factory, out Dictionary<string, string> idMap);
        TeamArenaLoadoutSnapshotPolicy.FlatNode[] remappedNodes = ToPolicyNodes(remapped);
        if (!TeamArenaLoadoutSnapshotPolicy.TryValidateFreshRemap(sourceNodes, remappedNodes, idMap, out string reason))
            throw new InvalidOperationException($"fresh-ID validation failed: {reason}");

        ItemFactory.FlatItemsToResultTree tree = factory.FlatItemsToTree(remapped, silentMode: true);
        if (tree.DeserializationErrors == null || tree.DeserializationErrors.Count > 0)
        {
            string errors = tree.DeserializationErrors == null
                ? "SPT returned no error collection"
                : string.Join("; ", tree.DeserializationErrors.Select(error => error?.ToString() ?? "<null>"));
            throw new InvalidOperationException($"FlatItemsToTree reported errors: {errors}");
        }

        FlatItem rootFlat = remapped.Single(item => !item.parentId.HasValue);
        if (tree.Items == null
            || tree.Items.Count != remapped.Length
            || !tree.Items.TryGetValue(rootFlat._id.ToString(), out Item rootItem)
            || rootItem is not InventoryEquipment newEquipment)
        {
            throw new InvalidOperationException(
                $"FlatItemsToTree returned {tree.Items?.Count ?? 0}/{remapped.Length} items or a non-equipment root");
        }

        ValidateReconstructedGraph(factory, remapped, newEquipment);
        if (!TeamArenaLoadoutSnapshotPolicy.TryBuildRuntimeRemap(
                _capturedIncludedIds,
                _capturedProtectedIds,
                previousRuntimeByCapturedId,
                currentProtectedIds,
                idMap,
                out Dictionary<string, string> currentToFresh,
                out string runtimeMapReason))
        {
            throw new InvalidOperationException($"runtime metadata remap failed: {runtimeMapReason}");
        }

        return new TeamArenaPreparedLoadoutRestore(
            this,
            roundEpoch,
            inventory,
            oldEquipment,
            newEquipment,
            currentProtectedIds,
            previousRuntimeByCapturedId,
            idMap,
            currentToFresh,
            remapped.Length);
    }

    private static FlatItem[] RemapWithFreshIds(
        FlatItem[] source,
        ItemFactory factory,
        out Dictionary<string, string> idMap)
    {
        idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var originalIds = new HashSet<string>(source.Select(item => item._id.ToString()), StringComparer.Ordinal);

        foreach (FlatItem item in source)
        {
            string originalId = item._id.ToString();
            string freshId = factory.NextId.ToString();
            if (originalIds.Contains(freshId)
                || idMap.Values.Contains(freshId, StringComparer.Ordinal)
                || !idMap.TryAdd(originalId, freshId))
            {
                throw new InvalidOperationException($"ItemFactory produced a reused ID for '{originalId}'");
            }
        }

        foreach (FlatItem item in source)
        {
            string originalId = item._id.ToString();
            item._id = new MongoID(idMap[originalId]);
            if (!item.parentId.HasValue)
                continue;

            string originalParent = item.parentId.Value.ToString();
            if (!idMap.TryGetValue(originalParent, out string freshParent))
                throw new InvalidOperationException($"captured parent '{originalParent}' has no fresh ID");

            item.parentId = new MongoID(freshParent);
        }

        return source;
    }

    private static void ValidateReconstructedGraph(
        ItemFactory factory,
        FlatItem[] remapped,
        InventoryEquipment newEquipment)
    {
        if (newEquipment.CurrentAddress != null)
            throw new InvalidOperationException("reconstructed equipment root is already attached");

        string[] expectedIds = remapped.Select(item => item._id.ToString())
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        string[] reachableIds = newEquipment.GetAllItems()
            .Select(item => item.Id.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (!expectedIds.SequenceEqual(reachableIds, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"reconstructed graph reaches {reachableIds.Length}/{expectedIds.Length} captured items");
        }

        FlatItem[] reflattened = factory.TreeToFlatItems(newEquipment);
        if (!TryValidateEquivalentGraph(remapped, reflattened, out string reason))
            throw new InvalidOperationException($"reflatten validation failed: {reason}");
    }

    private static void CommitEquipmentTransaction(
        Profile profile,
        InventoryEquipment oldEquipment,
        InventoryEquipment newEquipment,
        IReadOnlyDictionary<string, string> currentToFreshIds)
    {
        EftInventory inventory = profile.Inventory;
        SanitizerState sanitizerState = SanitizerState.Capture(profile, FastAccessField(inventory));
        List<SlotTransfer> plan = BuildProtectedTransferPlan(oldEquipment, newEquipment);
        var completedTransfers = new List<SlotTransfer>(plan.Count);
        var rollbackErrors = new List<string>();
        bool committed = false;

        try
        {
            foreach (SlotTransfer transfer in plan)
                ExecuteTransfer(transfer, completedTransfers, rollbackErrors);

            EquipmentField(inventory) = newEquipment;
            committed = true;
            ProfileInventorySanitizer.Sanitize(profile);
            sanitizerState.ApplyRemapped(profile, newEquipment, currentToFreshIds, FastAccessField);

            if (!ReferenceEquals(inventory.Equipment, newEquipment))
                throw new InvalidOperationException("Equipment FieldRef commit did not retain the reconstructed root");

            InvalidateWeightCache(inventory);
        }
        catch (Exception failure)
        {
            if (committed)
            {
                try
                {
                    EquipmentField(inventory) = oldEquipment;
                }
                catch (Exception ex)
                {
                    rollbackErrors.Add($"equipment FieldRef rollback failed: {ex.Message}");
                }
            }

            RollBackTransfers(completedTransfers, rollbackErrors);

            if (committed)
            {
                try
                {
                    sanitizerState.Restore(profile, FastAccessField);
                }
                catch (Exception ex)
                {
                    rollbackErrors.Add($"sanitizer-state rollback failed: {ex.Message}");
                }
            }

            try
            {
                InvalidateWeightCache(inventory);
            }
            catch (Exception ex)
            {
                rollbackErrors.Add($"inventory-weight rollback invalidation failed: {ex.Message}");
            }

            bool rollbackComplete = rollbackErrors.Count == 0
                                    && ReferenceEquals(inventory.Equipment, oldEquipment);
            string message = $"equipment transaction failed: {failure.Message}";
            if (rollbackErrors.Count > 0)
                message += $"; rollback errors: {string.Join("; ", rollbackErrors)}";

            throw new LoadoutRestoreException(message, rollbackComplete, failure);
        }
    }

    private static void InvalidateWeightCache(EftInventory inventory)
    {
        inventory.TotalWeight.SetDirty();
        inventory.TotalWeightEliteSkill.SetDirty();
    }

    private static List<SlotTransfer> BuildProtectedTransferPlan(
        InventoryEquipment oldEquipment,
        InventoryEquipment newEquipment)
    {
        var plan = new List<SlotTransfer>();

        foreach (EquipmentSlot slotType in RetainedEquipmentSlots)
        {
            Slot source = oldEquipment.GetSlot(slotType);
            if (source?.ContainedItem == null)
                continue;

            AddTransfer(plan, source, newEquipment.GetSlot(slotType), slotType.ToString());
        }

        CompoundItem oldPockets = oldEquipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem as CompoundItem;
        CompoundItem newPockets = newEquipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem as CompoundItem;
        if (oldPockets != null)
        {
            foreach (Slot source in oldPockets.Slots.Where(slot => slot.IsSpecial && slot.ContainedItem != null))
            {
                Slot destination = newPockets?.Slots.FirstOrDefault(slot => slot.ID == source.ID);
                AddTransfer(plan, source, destination, $"Pockets/{source.ID}");
            }
        }

        return plan;
    }

    private static void AddTransfer(List<SlotTransfer> plan, Slot source, Slot destination, string label)
    {
        Item item = source?.ContainedItem;
        if (item == null)
            return;

        if (destination == null)
            throw new InvalidOperationException($"protected destination slot '{label}' is missing");
        if (destination.ContainedItem != null)
            throw new InvalidOperationException($"protected destination slot '{label}' is not empty");
        if (item.StackObjectsCount != 1)
            throw new InvalidOperationException($"protected root '{label}' has invalid stack count {item.StackObjectsCount}");
        if (plan.Any(transfer => ReferenceEquals(transfer.Item, item)
                                 || ReferenceEquals(transfer.Source, source)
                                 || ReferenceEquals(transfer.Destination, destination)))
        {
            throw new InvalidOperationException($"protected transfer '{label}' is duplicated");
        }

        plan.Add(new SlotTransfer(source, destination, item, label));
    }

    private static void ExecuteTransfer(
        SlotTransfer transfer,
        List<SlotTransfer> completed,
        List<string> rollbackErrors)
    {
        var remove = transfer.Source.RemoveItemWithoutRestrictions();
        if (remove.Failed)
            throw new InvalidOperationException($"could not detach {transfer.Label}: {remove.Error}");

        var add = transfer.Destination.AddWithoutRestrictions(transfer.Item);
        if (add.Failed)
        {
            var immediateRollback = transfer.Source.AddWithoutRestrictions(transfer.Item);
            string rollback = immediateRollback.Failed
                ? $"; immediate rollback also failed: {immediateRollback.Error}"
                : "; immediately rolled back";
            if (immediateRollback.Failed)
                rollbackErrors.Add($"could not immediately restore {transfer.Label}: {immediateRollback.Error}");
            throw new InvalidOperationException($"could not attach {transfer.Label}: {add.Error}{rollback}");
        }

        completed.Add(transfer);
    }

    private static void RollBackTransfers(List<SlotTransfer> completed, List<string> errors)
    {
        for (int index = completed.Count - 1; index >= 0; index--)
        {
            SlotTransfer transfer = completed[index];
            if (!ReferenceEquals(transfer.Destination.ContainedItem, transfer.Item))
            {
                errors.Add($"{transfer.Label} was no longer in its rollback destination");
                continue;
            }

            var remove = transfer.Destination.RemoveItemWithoutRestrictions();
            if (remove.Failed)
            {
                errors.Add($"could not detach {transfer.Label} during rollback: {remove.Error}");
                continue;
            }

            var add = transfer.Source.AddWithoutRestrictions(transfer.Item);
            if (add.Failed)
                errors.Add($"could not restore {transfer.Label} during rollback: {add.Error}");
        }
    }

    private static HashSet<string> FindProtectedRootIds(InventoryEquipment equipment)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (EquipmentSlot slotType in RetainedEquipmentSlots)
        {
            Item item = equipment.GetSlot(slotType)?.ContainedItem;
            if (item != null)
                ids.Add(item.Id.ToString());
        }

        if (equipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem is CompoundItem pockets)
        {
            foreach (Slot slot in pockets.Slots)
            {
                if (slot.IsSpecial && slot.ContainedItem != null)
                    ids.Add(slot.ContainedItem.Id.ToString());
            }
        }

        return ids;
    }

    private static HashSet<string> FindProtectedItemIds(InventoryEquipment equipment)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (EquipmentSlot slotType in RetainedEquipmentSlots)
        {
            Item item = equipment.GetSlot(slotType)?.ContainedItem;
            if (item == null)
                continue;

            ids.UnionWith(item.GetAllItems().Select(child => child.Id.ToString()));
        }

        if (equipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem is CompoundItem pockets)
        {
            foreach (Slot slot in pockets.Slots)
            {
                if (slot.IsSpecial && slot.ContainedItem != null)
                {
                    ids.UnionWith(slot.ContainedItem.GetAllItems()
                        .Select(child => child.Id.ToString()));
                }
            }
        }

        return ids;
    }

    private static bool DictionaryEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
        => left != null
           && right != null
           && left.Count == right.Count
           && left.All(entry => right.TryGetValue(entry.Key, out string value)
                                && string.Equals(entry.Value, value, StringComparison.Ordinal));

    private static TeamArenaLoadoutSnapshotPolicy.FlatNode[] ToPolicyNodes(IEnumerable<FlatItem> flatItems)
        => flatItems.Select(item => new TeamArenaLoadoutSnapshotPolicy.FlatNode(
                item._id.ToString(),
                item.parentId?.ToString(),
                item.slotId,
                item._tpl.ToString()))
            .ToArray();

    private static bool TryValidateJsonRoundTrip(FlatItem[] source, FlatItem[] detached, out string reason)
    {
        if (ReferenceEquals(source, detached)
            || source.Length != detached.Length
            || source.Zip(detached, (left, right) => ReferenceEquals(left, right)).Any(same => same))
        {
            reason = "FlatItem JSON round-trip retained references or changed item count";
            return false;
        }

        return TryValidateEquivalentGraph(source, detached, out reason);
    }

    private static bool TryValidateEquivalentGraph(FlatItem[] expected, FlatItem[] actual, out string reason)
    {
        reason = null;
        if (expected == null || actual == null || expected.Length != actual.Length)
        {
            reason = "flat graphs have different item counts";
            return false;
        }

        Dictionary<string, FlatItem> actualById;
        try
        {
            actualById = actual.ToDictionary(item => item._id.ToString(), StringComparer.Ordinal);
        }
        catch (ArgumentException)
        {
            reason = "flat graph contains duplicate IDs";
            return false;
        }

        foreach (FlatItem item in expected)
        {
            string id = item._id.ToString();
            if (!actualById.TryGetValue(id, out FlatItem other))
            {
                reason = $"flat item '{id}' is missing";
                return false;
            }

            if (item._tpl != other._tpl)
            {
                reason = $"flat item '{id}' changed template";
                return false;
            }

            if (item.parentId != other.parentId)
            {
                reason = $"flat item '{id}' changed parent";
                return false;
            }

            if (!string.Equals(item.slotId, other.slotId, StringComparison.Ordinal))
            {
                reason = $"flat item '{id}' changed slot";
                return false;
            }

            if (!JsonEquivalent(item.location, other.location))
            {
                reason = $"flat item '{id}' changed location";
                return false;
            }

            if (!JsonEquivalent(item.upd, other.upd))
            {
                reason = $"flat item '{id}' changed upd";
                return false;
            }
        }

        return true;
    }

    private static bool JsonEquivalent(UnparsedData left, UnparsedData right)
        => TeamArenaJsonEquivalence.AreEquivalent(left?.JToken, right?.JToken);

    private sealed class SlotTransfer
    {
        internal SlotTransfer(Slot source, Slot destination, Item item, string label)
        {
            Source = source;
            Destination = destination;
            Item = item;
            Label = label;
        }

        internal Slot Source { get; }
        internal Slot Destination { get; }
        internal Item Item { get; }
        internal string Label { get; }
    }

    private sealed class SanitizerState
    {
        private readonly EftFastAccess _fastAccess;
        private readonly MongoID[] _favorites;
        private readonly Dictionary<MongoID, int> _discardLimits;
        private readonly Dictionary<MongoID, int> _checkedMagazines;
        private readonly MongoID[] _checkedChambers;

        private SanitizerState(
            EftFastAccess fastAccess,
            MongoID[] favorites,
            Dictionary<MongoID, int> discardLimits,
            Dictionary<MongoID, int> checkedMagazines,
            MongoID[] checkedChambers)
        {
            _fastAccess = fastAccess;
            _favorites = favorites;
            _discardLimits = discardLimits;
            _checkedMagazines = checkedMagazines;
            _checkedChambers = checkedChambers;
        }

        internal static SanitizerState Capture(Profile profile, EftFastAccess fastAccess)
            => new(
                fastAccess,
                profile.Inventory.FavoriteItemsStorage?.ToArray() ?? Array.Empty<MongoID>(),
                profile.Inventory.DiscardLimits != null
                    ? new Dictionary<MongoID, int>(profile.Inventory.DiscardLimits)
                    : null,
                profile.CheckedMagazines != null
                    ? new Dictionary<MongoID, int>(profile.CheckedMagazines)
                    : null,
                profile.CheckedChambers?.ToArray() ?? Array.Empty<MongoID>());

        internal void ApplyRemapped(
            Profile profile,
            InventoryEquipment newEquipment,
            IReadOnlyDictionary<string, string> currentToFreshIds,
            AccessTools.FieldRef<EftInventory, EftFastAccess> fastAccessField)
        {
            var validIds = new HashSet<string>(
                profile.Inventory.GetPlayerItems(EPlayerItems.All)
                    .Select(item => item.Id.ToString()),
                StringComparer.Ordinal);

            bool TryMap(MongoID oldId, out MongoID mappedId)
            {
                string oldText = oldId.ToString();
                string mappedText = currentToFreshIds.TryGetValue(oldText, out string fresh)
                    ? fresh
                    : oldText;
                if (!validIds.Contains(mappedText))
                {
                    mappedId = default;
                    return false;
                }

                mappedId = new MongoID(mappedText);
                return true;
            }

            var remappedBindings = new Dictionary<EBoundItem, MongoID>();
            if (_fastAccess?.BoundItems != null)
            {
                foreach (KeyValuePair<EBoundItem, Item> binding in _fastAccess.BoundItems)
                {
                    if (binding.Value == null || !TryMap(binding.Value.Id, out MongoID mappedId))
                        continue;

                    if (!newEquipment.TryFindItem(mappedId, out _))
                        throw new InvalidOperationException($"remapped quickslot {binding.Key} is absent from new equipment");

                    remappedBindings.Add(binding.Key, mappedId);
                }
            }

            fastAccessField(profile.Inventory) = new EftFastAccess(newEquipment, remappedBindings);

            if (profile.Inventory.FavoriteItemsStorage != null)
            {
                profile.Inventory.FavoriteItemsStorage.Clear();
                foreach (MongoID id in _favorites)
                {
                    if (TryMap(id, out MongoID mappedId))
                        profile.Inventory.FavoriteItemsStorage.Add(mappedId);
                }
            }

            if (profile.Inventory.DiscardLimits != null)
            {
                profile.Inventory.DiscardLimits.Clear();
                foreach (KeyValuePair<MongoID, int> entry in _discardLimits
                             ?? new Dictionary<MongoID, int>())
                {
                    if (TryMap(entry.Key, out MongoID mappedId))
                        profile.Inventory.DiscardLimits.Add(mappedId, entry.Value);
                }
            }

            if (profile.CheckedMagazines != null)
            {
                profile.CheckedMagazines.Clear();
                foreach (KeyValuePair<MongoID, int> entry in _checkedMagazines
                             ?? new Dictionary<MongoID, int>())
                {
                    if (TryMap(entry.Key, out MongoID mappedId))
                        profile.CheckedMagazines.Add(mappedId, entry.Value);
                }
            }

            if (profile.CheckedChambers != null)
            {
                profile.CheckedChambers.Clear();
                foreach (MongoID id in _checkedChambers)
                {
                    if (TryMap(id, out MongoID mappedId))
                        profile.CheckedChambers.Add(mappedId);
                }
            }
        }

        internal void Restore(
            Profile profile,
            AccessTools.FieldRef<EftInventory, EftFastAccess> fastAccessField)
        {
            fastAccessField(profile.Inventory) = _fastAccess;

            if (profile.Inventory.FavoriteItemsStorage != null)
            {
                profile.Inventory.FavoriteItemsStorage.Clear();
                profile.Inventory.FavoriteItemsStorage.UnionWith(_favorites);
            }

            if (profile.Inventory.DiscardLimits != null && _discardLimits != null)
            {
                profile.Inventory.DiscardLimits.Clear();
                foreach (KeyValuePair<MongoID, int> entry in _discardLimits)
                    profile.Inventory.DiscardLimits.Add(entry.Key, entry.Value);
            }

            if (profile.CheckedMagazines != null && _checkedMagazines != null)
            {
                profile.CheckedMagazines.Clear();
                foreach (KeyValuePair<MongoID, int> entry in _checkedMagazines)
                    profile.CheckedMagazines.Add(entry.Key, entry.Value);
            }

            if (profile.CheckedChambers != null)
            {
                profile.CheckedChambers.Clear();
                profile.CheckedChambers.AddRange(_checkedChambers);
            }
        }
    }

    private sealed class LoadoutRestoreException : Exception
    {
        internal LoadoutRestoreException(string message, bool rollbackComplete, Exception innerException)
            : base(message, innerException)
        {
            RollbackComplete = rollbackComplete;
        }

        internal bool RollbackComplete { get; }
    }
}

/// <summary>
/// Fully materialized, detached equipment transaction for one round epoch. Creating
/// this object performs every ItemFactory/deserialization/graph check; TryCommit is
/// intentionally limited to identity recheck, protected transfer, FieldRef swap and
/// sanitizer transaction so it is safe to invoke from RespawnEngine's strategy hook.
/// </summary>
public sealed class TeamArenaPreparedLoadoutRestore
{
    internal TeamArenaPreparedLoadoutRestore(
        TeamArenaLoadoutSnapshot owner,
        long roundEpoch,
        EftInventory sourceInventory,
        InventoryEquipment sourceEquipment,
        InventoryEquipment preparedEquipment,
        IReadOnlyCollection<string> expectedProtectedIds,
        IReadOnlyDictionary<string, string> previousRuntimeByCapturedId,
        IReadOnlyDictionary<string, string> nextRuntimeByCapturedId,
        IReadOnlyDictionary<string, string> nextRuntimeByPreviousRuntimeId,
        int preparedItemCount)
    {
        Owner = owner;
        RoundEpoch = roundEpoch;
        SourceInventory = sourceInventory;
        SourceEquipment = sourceEquipment;
        PreparedEquipment = preparedEquipment;
        ExpectedProtectedIds = new HashSet<string>(expectedProtectedIds, StringComparer.Ordinal);
        PreviousRuntimeByCapturedId = new Dictionary<string, string>(previousRuntimeByCapturedId, StringComparer.Ordinal);
        NextRuntimeByCapturedId = new Dictionary<string, string>(nextRuntimeByCapturedId, StringComparer.Ordinal);
        NextRuntimeByPreviousRuntimeId = new Dictionary<string, string>(nextRuntimeByPreviousRuntimeId, StringComparer.Ordinal);
        PreparedItemCount = preparedItemCount;
    }

    public long RoundEpoch { get; }
    public int PreparedItemCount { get; }

    internal TeamArenaLoadoutSnapshot Owner { get; }
    internal EftInventory SourceInventory { get; }
    internal InventoryEquipment SourceEquipment { get; }
    internal InventoryEquipment PreparedEquipment { get; }
    internal HashSet<string> ExpectedProtectedIds { get; }
    internal Dictionary<string, string> PreviousRuntimeByCapturedId { get; }
    internal Dictionary<string, string> NextRuntimeByCapturedId { get; }
    internal Dictionary<string, string> NextRuntimeByPreviousRuntimeId { get; }

    public bool TryCommit(Profile runtimeProfile, out TeamArenaLoadoutRestoreResult result)
        => Owner.TryCommitPrepared(this, runtimeProfile, out result);
}

public sealed class TeamArenaLoadoutRestoreResult
{
    private TeamArenaLoadoutRestoreResult(
        bool succeeded,
        long roundEpoch,
        int restoredItemCount,
        string failure,
        bool rollbackComplete)
    {
        Succeeded = succeeded;
        RoundEpoch = roundEpoch;
        RestoredItemCount = restoredItemCount;
        Failure = failure;
        RollbackComplete = rollbackComplete;
    }

    public bool Succeeded { get; }
    public long RoundEpoch { get; }
    public int RestoredItemCount { get; }
    public string Failure { get; }
    public bool RollbackComplete { get; }

    internal static TeamArenaLoadoutRestoreResult Success(long epoch, int count)
        => new(true, epoch, count, null, rollbackComplete: true);

    internal static TeamArenaLoadoutRestoreResult Failed(long epoch, string reason, bool rollbackComplete)
        => new(false, epoch, 0, reason, rollbackComplete);
}
