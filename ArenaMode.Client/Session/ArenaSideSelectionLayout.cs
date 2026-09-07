using EFT.UI.Matchmaker;
using System;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>Place the existing Arena toggle in the native character-choice row.</summary>
internal sealed class ArenaSideSelectionLayout : IDisposable
{
    private static readonly ConditionalWeakTable<MatchMakerSideSelectionScreen, ArenaSideSelectionLayout> Active = new();

    private RectTransform _wrapper;
    private Vector3 _originalPosition;
    private GameObject _caption;
    private bool _disposed;

    public static void Restore(MatchMakerSideSelectionScreen screen)
    {
        if (screen != null && Active.TryGetValue(screen, out var layout))
        {
            Active.Remove(screen);
            layout.Dispose();
        }
    }

    public static void Apply(MatchMakerSideSelectionScreen screen)
    {
        Restore(screen);
        var layout = new ArenaSideSelectionLayout();
        try
        {
            layout.Arrange(screen);
            Active.Add(screen, layout);
        }
        catch (Exception ex)
        {
            layout.Dispose();
            // Optional presentation must not disable the working native choice.
            Plugin.ModLogger.LogWarning($"Arena character-row layout unavailable: {ex.Message}");
        }
    }

    private void Arrange(MatchMakerSideSelectionScreen screen)
    {
        var arena = screen._randomButton;
        var wrapper = arena?.transform.parent as RectTransform;
        var arenaLabel = arena?.SpawnableToggle._headerLabel;
        var pmcLabel = screen._pmcButton?.SpawnableToggle._headerLabel;
        if (wrapper == null || wrapper.parent != screen.transform
            || wrapper.childCount != 1 || wrapper.GetChild(0) != arena.transform
            || arenaLabel == null || pmcLabel == null)
            throw new InvalidOperationException("The native character-choice row could not be located.");

        // Compare the real labels in the same coordinate space. The two character
        // previews, large click targets, Scav cooldown and PMC health UI stay put.
        var arenaCenter = screen.transform.InverseTransformPoint(
            arenaLabel.rectTransform.TransformPoint(arenaLabel.rectTransform.rect.center));
        var pmcCenter = screen.transform.InverseTransformPoint(
            pmcLabel.rectTransform.TransformPoint(pmcLabel.rectTransform.rect.center));
        _wrapper = wrapper;
        _originalPosition = wrapper.localPosition;
        wrapper.localPosition += new Vector3(0f, pmcCenter.y - arenaCenter.y, 0f);

        // Keep AlphaBlocker's single child intact. Anchor the display-only caption
        // to the actual toggle spawner so deferred native text sizing cannot move
        // it sideways. Ignore layout to avoid changing the native toggle's size.
        _caption = new GameObject("ArenaChoiceCaption", typeof(RectTransform), typeof(LayoutElement));
        _caption.SetActive(false);
        _caption.GetComponent<LayoutElement>().ignoreLayout = true;
        var rect = (RectTransform)_caption.transform;
        rect.SetParent(arena.transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.sizeDelta = new Vector2(220f, 18f);
        rect.anchoredPosition = new Vector2(0f, -2f);

        var caption = _caption.AddComponent<TextMeshProUGUI>();
        caption.font = pmcLabel.font;
        caption.fontSharedMaterial = pmcLabel.fontSharedMaterial;
        caption.fontSize = 10f;
        caption.fontStyle = FontStyles.Normal;
        // The toggle's current label tint depends on hover/selection. Use the
        // stable native description tint for this always-neutral subheading.
        caption.color = screen._sideDescriptionText.color;
        caption.alignment = TextAlignmentOptions.Center;
        caption.enableWordWrapping = false;
        caption.raycastTarget = false;
        caption.text = "SEPARATE MATCH";
        _caption.SetActive(true);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_wrapper != null)
            _wrapper.localPosition = _originalPosition;
        if (_caption != null)
        {
            _caption.SetActive(false);
            UnityEngine.Object.Destroy(_caption);
        }
        _wrapper = null;
        _caption = null;
    }
}
