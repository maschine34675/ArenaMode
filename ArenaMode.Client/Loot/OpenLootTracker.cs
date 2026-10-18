using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Linq;
using System.Reflection;

namespace ArenaMode.Loot;

/// <summary>
/// Tracks the loot container currently open in the inventory screen — shared
/// chokepoint for the auto-search chain (start/cancel) and the corpse cleanup
/// (never destroy a corpse the player is looking into).
/// </summary>
internal static class OpenLootTracker
{
    public static CompoundItem CurrentOpenLoot { get; private set; }

    /// <summary>Raised with (controller, lootItem) when a loot screen opens.</summary>
    public static event Action<EFT.InventoryLogic.InventoryController, CompoundItem> OnLootScreenOpened;

    public static event Action OnLootScreenClosed;

    public static void Enable()
    {
        new ArenaOpenLootTrackerShowPatch().Enable();
        new ArenaOpenLootTrackerClosePatch().Enable();
    }

    public static bool IsOpen(Item rootItem)
        => CurrentOpenLoot != null && ReferenceEquals(CurrentOpenLoot, rootItem);

    private sealed class ArenaOpenLootTrackerShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // Overload set makes AccessTools.Method ambiguous — pick the Show(...)
            // that carries the CompoundItem lootItem parameter (same resolution the
            // standalone AutoCorpseSearch mod uses).
            return typeof(InventoryScreen)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(m => m.Name == nameof(InventoryScreen.Show)
                            && m.GetParameters().Any(p => p.ParameterType == typeof(CompoundItem)));
        }

        [PatchPostfix]
        private static void Postfix(EFT.InventoryLogic.InventoryController controller, CompoundItem lootItem)
        {
            CurrentOpenLoot = lootItem;
            if (lootItem == null)
                return;

            try
            {
                OnLootScreenOpened?.Invoke(controller, lootItem);
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning($"Loot-open subscriber failed: {ex.Message}");
            }
        }
    }

    private sealed class ArenaOpenLootTrackerClosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(InventoryScreen), nameof(InventoryScreen.Close));

        [PatchPrefix]
        private static void Prefix()
        {
            CurrentOpenLoot = null;
            try
            {
                OnLootScreenClosed?.Invoke();
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning($"Loot-close subscriber failed: {ex.Message}");
            }
        }
    }
}
