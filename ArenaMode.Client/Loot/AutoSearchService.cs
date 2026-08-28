using ArenaMode.Config;
using ArenaMode.Session;
using BepInEx.Bootstrap;
using EFT;
using EFT.InventoryLogic;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Loot;

/// <summary>
/// Auto-search of corpse equipment slots on opening the loot screen — absorbed
/// from the standalone maschine-AutoCorpseSearch mod, arena-gated. With the
/// instant-search flag active the whole corpse reveals within a few frames.
/// Defers entirely when the standalone mod is loaded (it already covers arena
/// raids; two drivers would race the search-operation slots).
/// </summary>
internal static class AutoSearchService
{
    private const string StandaloneGuid = "com.maschine.AutoCorpseSearch";

    private static readonly EquipmentSlot[] SearchOrder =
    {
        EquipmentSlot.TacticalVest,
        EquipmentSlot.Pockets,
        EquipmentSlot.Backpack,
    };

    private static bool? _standaloneLoaded;
    private static Coroutine _activeSearch;

    public static void Initialize()
    {
        OpenLootTracker.OnLootScreenOpened += OnLootOpened;
        OpenLootTracker.OnLootScreenClosed += CancelSearch;
    }

    private static bool StandaloneLoaded
        => _standaloneLoaded ??= Chainloader.PluginInfos.ContainsKey(StandaloneGuid);

    private static void OnLootOpened(InventoryController controller, CompoundItem lootItem)
    {
        if (!ArenaSession.IsActive || !Settings.AutoSearchCorpses.Value || StandaloneLoaded)
            return;

        var searchController = controller?.SearchController as IPlayerSearchController;
        if (searchController == null)
            return;

        // Corpse = an equipment root that is not our own.
        if (lootItem is InventoryEquipment equipment
            && (controller as Player.PlayerInventoryController)?.Inventory?.Equipment != equipment)
        {
            CancelSearch();
            _activeSearch = Plugin.Instance.StartCoroutine(SearchCorpseSlots(searchController, equipment));
            return;
        }

        // Container resume: previously started but still unknown content.
        if (lootItem is SearchableItem searchable
            && searchController.IsSearched(searchable)
            && searchController.ContainsUnknownItems(searchable)
            && searchController.CanStartNewSearchOperation())
        {
            searchController.SearchContents(searchable);
        }
    }

    public static void CancelSearch()
    {
        if (_activeSearch == null)
            return;

        Plugin.Instance.StopCoroutine(_activeSearch);
        _activeSearch = null;
    }

    private static IEnumerator SearchCorpseSlots(IPlayerSearchController searchController, InventoryEquipment equipment)
    {
        SearchableItem previous = null;

        foreach (var slot in SearchOrder)
        {
            var item = equipment.GetSlot(slot)?.ContainedItem as SearchableItem;
            if (item == null)
                continue;

            // Wait on the SPECIFIC previous operation, not on free capacity — the
            // elite double-search skill would otherwise let the chain misread a
            // still-running search as cancelled.
            while (previous != null && searchController.SearchOperations.Any(op => op.Item == previous))
                yield return null;

            // Raid may have torn down while waiting (screen-close normally cancels,
            // this closes the residual window for free).
            if (!ArenaSession.IsActive)
            {
                _activeSearch = null;
                yield break;
            }

            // A cancelled slot still contains unknown items; a completed one does
            // not — respect a manual cancel by stopping the chain.
            if (previous != null && searchController.ContainsUnknownItems(previous))
            {
                _activeSearch = null;
                yield break;
            }

            if (!searchController.IsSearched(item) || searchController.ContainsUnknownItems(item))
            {
                searchController.SearchContents(item);
                previous = item;
            }
        }

        _activeSearch = null;
    }
}
