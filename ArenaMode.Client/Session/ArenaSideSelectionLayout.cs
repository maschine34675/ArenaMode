using EFT;
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
    private Transform _pmcColumn;
    private Transform _scavColumn;
    private Vector3 _pmcPosition;
    private Vector3 _scavPosition;
    private ArenaCharacterPreview _preview;
    private bool _disposed;

    public static ArenaCharacterPreview DetachPreview(MatchMakerSideSelectionScreen screen)
    {
        if (screen == null || !Active.TryGetValue(screen, out var layout)) return null;
        var preview = layout._preview;
        layout._preview = null;
        return preview;
    }

    public static void Restore(MatchMakerSideSelectionScreen screen)
    {
        if (screen != null && Active.TryGetValue(screen, out var layout))
        {
            Active.Remove(screen);
            layout.Dispose();
        }
    }

    public static void Apply(MatchMakerSideSelectionScreen screen, Profile profile)
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
            return;
        }
        try { layout.AddPreview(screen, profile); }
        catch (Exception ex)
        {
            layout.RestoreColumns();
            Plugin.ModLogger.LogWarning($"Arena character preview layout unavailable: {ex.Message}");
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

        // Compare the real labels in the same coordinate space.
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

    private void AddPreview(MatchMakerSideSelectionScreen screen, Profile profile)
    {
        // Keep each model, click target, label, cooldown and health panel together.
        var pmcColumn = Column(screen, screen._pmcButton.transform);
        var scavColumn = Column(screen, screen._savagesButton.transform);
        if (pmcColumn == scavColumn
            || !screen._pmcModelView.transform.IsChildOf(pmcColumn)
            || !screen._savageModelView.transform.IsChildOf(scavColumn))
            throw new InvalidOperationException("Native character columns could not be identified.");
        _pmcColumn = pmcColumn;
        _scavColumn = scavColumn;
        _pmcPosition = _pmcColumn.localPosition;
        _scavPosition = _scavColumn.localPosition;
        _pmcColumn.localPosition += new Vector3(150f, 0f, 0f);
        _scavColumn.localPosition -= new Vector3(150f, 0f, 0f);
        _preview = new ArenaCharacterPreview(screen, profile, RestoreColumns);
    }

    private static Transform Column(MatchMakerSideSelectionScreen screen, Transform child)
    {
        while (child.parent != null && child.parent != screen.transform) child = child.parent;
        if (child.parent != screen.transform)
            throw new InvalidOperationException("Native character column has an unexpected parent.");
        return child;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        RestoreColumns();
        Safe(() => _preview?.Dispose());
        _preview = null;

        Safe(() => { if (_wrapper != null) _wrapper.localPosition = _originalPosition; });
        if (_caption != null)
        {
            Safe(() => _caption.SetActive(false));
            Safe(() => UnityEngine.Object.Destroy(_caption));
        }
        _wrapper = null;
        _caption = null;
    }

    private void RestoreColumns()
    {
        Safe(() => { if (_pmcColumn != null) _pmcColumn.localPosition = _pmcPosition; });
        Safe(() => { if (_scavColumn != null) _scavColumn.localPosition = _scavPosition; });
        _pmcColumn = null;
        _scavColumn = null;
    }

    private static void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena character-row restore: {ex.Message}"); }
    }
}
