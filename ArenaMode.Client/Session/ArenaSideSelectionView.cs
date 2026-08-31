using EFT.UI;
using System;
using UnityEngine;

namespace ArenaMode.Session;

/// <summary>Expose the unused native third toggle without changing the other sides.</summary>
internal static class ArenaSideSelectionView
{
    public static void Show(Transform screen, UIAnimatedToggleSpawner arena, bool canSelect)
    {
        // In EFT 0.16.9 level48, RandomToggleSpawner is inside an inactive
        // AlphaBlocker: alpha .3, interactable false, with an alpha-era tooltip.
        // Enabling the spawner alone cannot make it visible or clickable.
        // Only unlock a wrapper exclusively owned by this button; never walk up
        // and unlock the screen or the Scav cooldown's CanvasGroup.
        if (!HasOwnedWrapper(screen, arena))
            throw new InvalidOperationException("The native third toggle's exclusive wrapper was not found.");

        var wrapper = arena.transform.parent;
        var group = wrapper.GetComponent<CanvasGroup>();
        if (group == null)
            throw new InvalidOperationException("The native third toggle's wrapper has no CanvasGroup.");

        // Keep the already-spawned toggle, ToggleGroup and native listeners.
        // UIAnimatedToggleSpawner.SetActive cannot unlock this wrapper: the
        // spawner's own serialized _canvasGroup reference is null in this scene.
        arena.vmethod_0("Arena", arena._headerFontSize);
        arena.SpawnedObject.interactable = canSelect;
        var tooltip = wrapper.GetComponent<HoverTooltipArea>();
        if (tooltip != null)
            tooltip.enabled = false;

        group.alpha = 1f;
        group.interactable = canSelect;
        group.blocksRaycasts = true;
        arena.gameObject.SetActive(true);
        wrapper.gameObject.SetActive(true);
    }

    public static void Hide(Transform screen, UIAnimatedToggleSpawner arena)
    {
        if (arena == null)
            return;

        arena.gameObject.SetActive(false);
        if (HasOwnedWrapper(screen, arena))
            arena.transform.parent.gameObject.SetActive(false);
    }

    private static bool HasOwnedWrapper(Transform screen, UIAnimatedToggleSpawner arena)
    {
        if (screen == null || arena == null)
            return false;

        var wrapper = arena.transform.parent;
        return wrapper != null && wrapper.parent == screen
            && wrapper.childCount == 1 && wrapper.GetChild(0) == arena.transform;
    }
}
