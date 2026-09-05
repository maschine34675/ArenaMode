using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Kits;

/// <summary>
/// Game-free invariants for the Tactical-team loadout snapshot. Keeping these rules
/// independent of EFT types lets the destructive inventory boundary be exercised by
/// the offline checks instead of relying only on a client build.
/// </summary>
internal static class TeamArenaLoadoutSnapshotPolicy
{
    internal readonly struct FlatNode
    {
        public FlatNode(string id, string parentId, string slotId, string templateId)
        {
            Id = id;
            ParentId = parentId;
            SlotId = slotId;
            TemplateId = templateId;
        }

        public string Id { get; }
        public string ParentId { get; }
        public string SlotId { get; }
        public string TemplateId { get; }
    }

    /// <summary>
    /// Removes complete protected subtrees while retaining their ordinary ancestors
    /// (notably the Pockets item whose non-special grids belong in the snapshot).
    /// </summary>
    internal static bool TryFilterProtectedSubtrees(
        IReadOnlyList<FlatNode> source,
        IReadOnlyCollection<string> protectedRootIds,
        out HashSet<string> includedIds,
        out string reason)
    {
        includedIds = new HashSet<string>(StringComparer.Ordinal);
        reason = null;

        if (!TryValidateSource(source, out Dictionary<string, FlatNode> byId, out string rootId, out reason))
            return false;

        var protectedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string protectedRootId in protectedRootIds ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(protectedRootId) || !byId.ContainsKey(protectedRootId))
            {
                reason = $"protected root '{protectedRootId ?? "<null>"}' is absent from the flat inventory";
                return false;
            }

            protectedIds.Add(protectedRootId);
        }

        // Parent-first ordering is not guaranteed by external JSON, so close over
        // descendants until stable instead of trusting array order.
        bool changed;
        do
        {
            changed = false;
            foreach (FlatNode node in source)
            {
                if (!string.IsNullOrEmpty(node.ParentId)
                    && protectedIds.Contains(node.ParentId)
                    && protectedIds.Add(node.Id))
                {
                    changed = true;
                }
            }
        }
        while (changed);

        if (protectedIds.Contains(rootId))
        {
            reason = "the equipment root cannot be a protected subtree";
            return false;
        }

        foreach (FlatNode node in source)
        {
            if (!protectedIds.Contains(node.Id))
                includedIds.Add(node.Id);
        }

        if (!TryValidateClosedSubset(source, includedIds, out reason))
            return false;

        return true;
    }

    /// <summary>
    /// Verifies that a reconstructed graph is a one-to-one, fresh-ID copy of the
    /// captured graph. Parent links, slots, templates and root reachability must all
    /// survive the remap.
    /// </summary>
    internal static bool TryValidateFreshRemap(
        IReadOnlyList<FlatNode> source,
        IReadOnlyList<FlatNode> remapped,
        IReadOnlyDictionary<string, string> oldToNewIds,
        out string reason)
    {
        reason = null;
        if (!TryValidateSource(source, out _, out string sourceRoot, out reason))
            return false;

        if (!TryValidateSource(remapped, out Dictionary<string, FlatNode> remappedById, out string remappedRoot, out reason))
            return false;

        if (source.Count != remapped.Count || oldToNewIds == null || oldToNewIds.Count != source.Count)
        {
            reason = "fresh-ID remap changed the item count";
            return false;
        }

        var sourceIds = new HashSet<string>(source.Select(node => node.Id), StringComparer.Ordinal);
        var mappedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (FlatNode sourceNode in source)
        {
            if (!oldToNewIds.TryGetValue(sourceNode.Id, out string mappedId)
                || string.IsNullOrWhiteSpace(mappedId)
                || sourceIds.Contains(mappedId)
                || !mappedIds.Add(mappedId)
                || !remappedById.TryGetValue(mappedId, out FlatNode mappedNode))
            {
                reason = $"item '{sourceNode.Id}' does not have one unique fresh runtime ID";
                return false;
            }

            string expectedParent = null;
            if (!string.IsNullOrEmpty(sourceNode.ParentId)
                && !oldToNewIds.TryGetValue(sourceNode.ParentId, out expectedParent))
            {
                reason = $"parent '{sourceNode.ParentId}' was not remapped";
                return false;
            }

            if (!string.Equals(mappedNode.ParentId, expectedParent, StringComparison.Ordinal)
                || !string.Equals(mappedNode.SlotId, sourceNode.SlotId, StringComparison.Ordinal)
                || !string.Equals(mappedNode.TemplateId, sourceNode.TemplateId, StringComparison.Ordinal))
            {
                reason = $"item '{sourceNode.Id}' changed parent, slot or template during remap";
                return false;
            }
        }

        if (!oldToNewIds.TryGetValue(sourceRoot, out string expectedRoot)
            || !string.Equals(remappedRoot, expectedRoot, StringComparison.Ordinal))
        {
            reason = "fresh-ID remap changed the equipment root";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Joins the previous and next canonical-ID maps and validates the protected
    /// identity boundary before a restore. Combat items moved into a protected slot
    /// would otherwise be duplicated by snapshot reconstruction; originally protected
    /// items moved out would otherwise be stranded on the corpse. Known protected
    /// consumable descendants may be absent after use; they are never recreated.
    /// </summary>
    internal static bool TryBuildRuntimeRemap(
        IReadOnlyCollection<string> capturedIncludedIds,
        IReadOnlyCollection<string> capturedProtectedIds,
        IReadOnlyDictionary<string, string> previousRuntimeByCapturedId,
        IReadOnlyCollection<string> currentProtectedIds,
        IReadOnlyDictionary<string, string> nextRuntimeByCapturedId,
        out Dictionary<string, string> nextRuntimeByPreviousRuntimeId,
        out string reason,
        IReadOnlyCollection<string> capturedExpendableProtectedIds = null,
        IReadOnlyCollection<string> currentEquipmentIds = null)
    {
        nextRuntimeByPreviousRuntimeId = new Dictionary<string, string>(StringComparer.Ordinal);
        reason = null;

        var included = new HashSet<string>(capturedIncludedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        if (!TryValidateProtectedBoundary(
                included,
                capturedProtectedIds,
                previousRuntimeByCapturedId,
                currentProtectedIds,
                out reason,
                capturedExpendableProtectedIds,
                currentEquipmentIds))
            return false;

        if (nextRuntimeByCapturedId == null
            || nextRuntimeByCapturedId.Count != included.Count
            || !included.SetEquals(nextRuntimeByCapturedId.Keys))
        {
            reason = "next canonical-to-runtime ID map does not cover the captured ordinary graph exactly";
            return false;
        }

        var priorRuntimeIds = new HashSet<string>(StringComparer.Ordinal);
        var nextRuntimeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string capturedId in included)
        {
            string previousRuntimeId = previousRuntimeByCapturedId[capturedId];
            string nextRuntimeId = nextRuntimeByCapturedId[capturedId];
            if (string.IsNullOrWhiteSpace(previousRuntimeId)
                || string.IsNullOrWhiteSpace(nextRuntimeId)
                || !priorRuntimeIds.Add(previousRuntimeId)
                || !nextRuntimeIds.Add(nextRuntimeId)
                || !nextRuntimeByPreviousRuntimeId.TryAdd(previousRuntimeId, nextRuntimeId))
            {
                reason = "canonical-to-runtime ID maps contain blank or duplicate runtime IDs";
                nextRuntimeByPreviousRuntimeId.Clear();
                return false;
            }
        }

        return true;
    }

    internal static bool TryValidateProtectedBoundary(
        IReadOnlyCollection<string> capturedIncludedIds,
        IReadOnlyCollection<string> capturedProtectedIds,
        IReadOnlyDictionary<string, string> previousRuntimeByCapturedId,
        IReadOnlyCollection<string> currentProtectedIds,
        out string reason,
        IReadOnlyCollection<string> capturedExpendableProtectedIds = null,
        IReadOnlyCollection<string> currentEquipmentIds = null)
    {
        reason = null;
        var included = new HashSet<string>(capturedIncludedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var protectedIds = new HashSet<string>(capturedProtectedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var currentProtected = new HashSet<string>(currentProtectedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var expendable = new HashSet<string>(capturedExpendableProtectedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var currentEquipment = new HashSet<string>(currentEquipmentIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        if (included.Count == 0 || included.Overlaps(protectedIds))
        {
            reason = "captured ordinary and protected ID sets are empty or overlap";
            return false;
        }

        if (previousRuntimeByCapturedId == null
            || previousRuntimeByCapturedId.Count != included.Count
            || !included.SetEquals(previousRuntimeByCapturedId.Keys))
        {
            reason = "previous canonical-to-runtime ID map does not cover the captured ordinary graph exactly";
            return false;
        }

        if (!expendable.IsSubsetOf(protectedIds)
            || (expendable.Count > 0
                && (currentEquipmentIds == null || !currentProtected.IsSubsetOf(currentEquipment))))
        {
            reason = "protected consumable validation lacks a consistent current equipment graph";
            return false;
        }

        // Only IDs classified at match entry can be absent. A consumable still
        // found elsewhere on the old body was moved, not exhausted, and must
        // continue to fail closed. Protected slot roots are never classified.
        string[] missingProtected = protectedIds
            .Where(id => !currentProtected.Contains(id)
                         && (!expendable.Contains(id) || currentEquipment.Contains(id)))
            .ToArray();
        if (missingProtected.Length > 0)
        {
            reason = $"{missingProtected.Length} originally protected items left or disappeared from protected slots";
            return false;
        }

        string[] ordinaryNowProtected = previousRuntimeByCapturedId.Values
            .Where(currentProtected.Contains)
            .ToArray();
        if (ordinaryNowProtected.Length > 0)
        {
            reason = $"{ordinaryNowProtected.Length} snapshot combat items moved into protected slots";
            return false;
        }

        string[] unexpectedProtected = currentProtected.Where(id => !protectedIds.Contains(id)).ToArray();
        if (unexpectedProtected.Length > 0)
        {
            reason = $"{unexpectedProtected.Length} uncaptured items entered protected slots";
            return false;
        }

        return true;
    }

    private static bool TryValidateSource(
        IReadOnlyList<FlatNode> source,
        out Dictionary<string, FlatNode> byId,
        out string rootId,
        out string reason)
    {
        byId = new Dictionary<string, FlatNode>(StringComparer.Ordinal);
        rootId = null;
        reason = null;

        if (source == null || source.Count == 0)
        {
            reason = "flat inventory is empty";
            return false;
        }

        foreach (FlatNode node in source)
        {
            if (string.IsNullOrWhiteSpace(node.Id)
                || string.IsNullOrWhiteSpace(node.TemplateId)
                || !byId.TryAdd(node.Id, node))
            {
                reason = $"flat inventory has a blank or duplicate item ID '{node.Id ?? "<null>"}'";
                return false;
            }
        }

        FlatNode[] roots = source.Where(node => string.IsNullOrEmpty(node.ParentId)).ToArray();
        if (roots.Length != 1)
        {
            reason = $"flat inventory has {roots.Length} roots instead of one";
            return false;
        }

        rootId = roots[0].Id;
        foreach (FlatNode node in source)
        {
            if (!string.IsNullOrEmpty(node.ParentId) && !byId.ContainsKey(node.ParentId))
            {
                reason = $"item '{node.Id}' has missing parent '{node.ParentId}'";
                return false;
            }
        }

        var reachable = new HashSet<string>(StringComparer.Ordinal) { rootId };
        bool changed;
        do
        {
            changed = false;
            foreach (FlatNode node in source)
            {
                if (!string.IsNullOrEmpty(node.ParentId)
                    && reachable.Contains(node.ParentId)
                    && reachable.Add(node.Id))
                {
                    changed = true;
                }
            }
        }
        while (changed);

        if (reachable.Count != source.Count)
        {
            reason = $"flat inventory has {source.Count - reachable.Count} cyclic or unreachable items";
            return false;
        }

        return true;
    }

    private static bool TryValidateClosedSubset(
        IReadOnlyList<FlatNode> source,
        HashSet<string> includedIds,
        out string reason)
    {
        reason = null;
        FlatNode[] retained = source.Where(node => includedIds.Contains(node.Id)).ToArray();
        return TryValidateSource(retained, out _, out _, out reason);
    }
}

/// <summary>
/// Enforces one destructive restore attempt per explicitly armed round epoch. An
/// inventory failure poisons the snapshot for the rest of the match; a successful
/// epoch permits a later, strictly increasing epoch to be armed.
/// </summary>
internal sealed class TeamArenaLoadoutRestoreGate
{
    private enum GateState
    {
        Idle,
        Armed,
        Preparing,
        Prepared,
        Committing,
        Succeeded,
        Poisoned,
    }

    private readonly object _sync = new();
    private GateState _state;
    private long _armedEpoch;
    private long _lastCompletedEpoch;
    private string _failure;

    internal bool IsPoisoned
    {
        get
        {
            lock (_sync)
                return _state == GateState.Poisoned;
        }
    }

    internal bool TryArm(long epoch, out string reason)
    {
        lock (_sync)
        {
            if (epoch <= 0)
            {
                reason = "round epoch must be positive";
                return false;
            }

            if (_state == GateState.Poisoned)
            {
                reason = $"loadout snapshot is poisoned: {_failure}";
                return false;
            }

            if (_state == GateState.Armed
                || _state == GateState.Preparing
                || _state == GateState.Prepared
                || _state == GateState.Committing)
            {
                reason = $"round epoch {_armedEpoch} already owns the restore gate";
                return false;
            }

            if (epoch <= _lastCompletedEpoch)
            {
                reason = $"round epoch {epoch} is not newer than completed epoch {_lastCompletedEpoch}";
                return false;
            }

            _armedEpoch = epoch;
            _state = GateState.Armed;
            reason = null;
            return true;
        }
    }

    internal bool TryBeginPrepare(long epoch, out string reason)
    {
        lock (_sync)
        {
            if (_state == GateState.Poisoned)
            {
                reason = $"loadout snapshot is poisoned: {_failure}";
                return false;
            }

            if (_state != GateState.Armed || epoch != _armedEpoch)
            {
                reason = _state == GateState.Armed
                    ? $"round epoch {epoch} does not own the gate armed for {_armedEpoch}"
                    : "loadout restore gate is not armed";
                return false;
            }

            _state = GateState.Preparing;
            reason = null;
            return true;
        }
    }

    internal void CompletePrepare(long epoch, bool succeeded, string failure)
    {
        lock (_sync)
        {
            if (_state != GateState.Preparing || epoch != _armedEpoch)
                throw new InvalidOperationException("loadout prepare completion does not match the active epoch");

            if (succeeded)
            {
                _state = GateState.Prepared;
                return;
            }

            _failure = string.IsNullOrWhiteSpace(failure) ? "unspecified inventory failure" : failure;
            _state = GateState.Poisoned;
        }
    }

    internal bool TryBeginCommit(long epoch, out string reason)
    {
        lock (_sync)
        {
            if (_state == GateState.Poisoned)
            {
                reason = $"loadout snapshot is poisoned: {_failure}";
                return false;
            }

            if (_state != GateState.Prepared || epoch != _armedEpoch)
            {
                reason = _state == GateState.Prepared
                    ? $"round epoch {epoch} does not own the prepared restore for {_armedEpoch}"
                    : "loadout restore is not prepared";
                return false;
            }

            _state = GateState.Committing;
            reason = null;
            return true;
        }
    }

    internal void CompleteCommit(long epoch, bool succeeded, string failure)
    {
        lock (_sync)
        {
            if (_state != GateState.Committing || epoch != _armedEpoch)
                throw new InvalidOperationException("loadout commit completion does not match the active epoch");

            if (succeeded)
            {
                _lastCompletedEpoch = epoch;
                _state = GateState.Succeeded;
                _failure = null;
                return;
            }

            _failure = string.IsNullOrWhiteSpace(failure) ? "unspecified inventory failure" : failure;
            _state = GateState.Poisoned;
        }
    }
}
