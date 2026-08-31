using EFT.UI.Matchmaker;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Session;

/// <summary>Hide ordinary raid decorations without hiding the map buttons.</summary>
internal sealed class ArenaLocationSelectionView
{
    private readonly Dictionary<GameObject, bool> _hiddenViews = new();

    public void HideExtras(Transform pathsContainer, GameObject marathonButton)
    {
        Restore();

        // In EFT 0.16.9 level48 BOTH _locationPathsContainer and
        // _locationButtonsContainer reference the same Transform. Disabling
        // that container hides every map. Only touch individual path views.
        if (pathsContainer != null)
        {
            foreach (var path in pathsContainer.GetComponentsInChildren<LocationPath>(true))
            {
                if (path.transform != pathsContainer)
                    Hide(path.gameObject);
            }
        }

        Hide(marathonButton);
    }

    public void Restore()
    {
        foreach (var entry in _hiddenViews)
        {
            // Native UI.Dispose destroys generated views when changing screens.
            if (entry.Key != null)
                entry.Key.SetActive(entry.Value);
        }
        _hiddenViews.Clear();
    }

    private void Hide(GameObject view)
    {
        if (view == null || _hiddenViews.ContainsKey(view))
            return;

        _hiddenViews.Add(view, view.activeSelf);
        view.SetActive(false);
    }
}
