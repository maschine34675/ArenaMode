using System;
using System.Runtime.CompilerServices;
using EFT.UI.Matchmaker;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>
/// Passive, screen-owned diagnostics. Keep this independent of Core, settings,
/// previews and compatibility adapters: it also runs when Core cannot load.
/// </summary>
internal static class ArenaAvailabilityNotice
{
    private static readonly ConditionalWeakTable<MatchMakerSideSelectionScreen, View> Views = new();

    internal static void Show(MatchMakerSideSelectionScreen screen, string reason)
    {
        try { ShowUnsafe(screen, reason); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning("Arena availability notice could not be displayed: " + ex.Message); }
    }

    private static void ShowUnsafe(MatchMakerSideSelectionScreen screen, string reason)
    {
        if (screen == null) return;
        if (string.IsNullOrWhiteSpace(reason))
        {
            Hide(screen);
            return;
        }
        if (!Views.TryGetValue(screen, out var view))
        {
            view = new View(screen);
            Views.Add(screen, view);
        }
        view.Set(screen, reason);
    }

    internal static void Hide(MatchMakerSideSelectionScreen screen)
    {
        if (screen == null || !Views.TryGetValue(screen, out var view)) return;
        Views.Remove(screen);
        view.Root.SetActive(false);
        UnityEngine.Object.Destroy(view.Root);
    }

    private sealed class View
    {
        internal readonly GameObject Root;
        private readonly TextMeshProUGUI _text;

        internal View(MatchMakerSideSelectionScreen screen)
        {
            Root = new GameObject("ArenaUnavailableNotice", typeof(RectTransform), typeof(Image));
            Root.SetActive(false);
            var rect = (RectTransform)Root.transform;
            rect.SetParent(screen.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, 0f);
            var anchor = Root.AddComponent<ArenaAvailabilityNoticeAnchor>();
            anchor.Source = screen._randomButton.transform as RectTransform;
            var background = Root.GetComponent<Image>();
            background.color = new Color(.06f, .07f, .08f, .94f);
            background.raycastTarget = false;

            var label = new GameObject("Reason", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(rect, false);
            _text = label.GetComponent<TextMeshProUGUI>();
            _text.rectTransform.anchorMin = Vector2.zero;
            _text.rectTransform.anchorMax = Vector2.one;
            _text.rectTransform.offsetMin = new Vector2(14f, 10f);
            _text.rectTransform.offsetMax = new Vector2(-14f, -10f);
            _text.font = screen._sideDescriptionText.font;
            _text.fontSharedMaterial = screen._sideDescriptionText.fontSharedMaterial;
            _text.fontSize = 16f;
            _text.fontStyle = FontStyles.Normal;
            _text.color = new Color(.95f, .77f, .46f, 1f);
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.enableWordWrapping = true;
            _text.richText = false; // A foreign error must remain literal text.
            _text.raycastTarget = false;
        }

        internal void Set(MatchMakerSideSelectionScreen screen, string reason)
        {
            _text.text = "ARENA UNAVAILABLE\n" + reason;
            var rect = (RectTransform)Root.transform;
            const float width = 440f;
            rect.sizeDelta = new Vector2(width, _text.GetPreferredValues(_text.text, width - 28f, 0f).y + 24f);
            Root.GetComponent<ArenaAvailabilityNoticeAnchor>().Place();
            Root.transform.SetAsLastSibling();
            Root.SetActive(true);
        }
    }
}

/// <summary>Native toggle sizing finishes after Show; follow its stable center.</summary>
internal sealed class ArenaAvailabilityNoticeAnchor : MonoBehaviour
{
    internal RectTransform Source;
    private void LateUpdate() => Place();
    internal void Place()
    {
        if (Source == null || transform.parent == null) return;
        var center = transform.parent.InverseTransformPoint(Source.TransformPoint(Source.rect.center));
        // Above the choice, away from the shared PMC/Scav description and Next.
        transform.localPosition = new Vector3(center.x, center.y + 42f, 0f);
    }
}
