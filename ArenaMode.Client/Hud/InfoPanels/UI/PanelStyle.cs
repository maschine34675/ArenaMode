using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud.InfoPanels.UI
{
    internal static class PanelStyle
    {
        // Matches the 9-slice border baked into panel_bg.png / panel_stroke.png
        // by tools/RaidInfoPanelIcons/gen_panel.ps1.
        private const float PanelSliceBorder = 20f;

        // Glass look: a single translucent dark pane instead of the old pair of
        // stacked opaque rectangles. The tint stays near-black so it darkens the
        // scene behind the text without introducing a color cast; the rim light
        // is what actually defines the panel edge.
        private static readonly Color PanelTint = new Color(0.04f, 0.05f, 0.07f, 1f);
        private static readonly Color RimColor = new Color(1f, 1f, 1f, 0.18f);

        private static readonly List<Image> _panelFills = new List<Image>();
        private static readonly List<Image> _panelRims = new List<Image>();

        // One Arena-owned font resolver for both the original Arena HUD and
        // the absorbed panels. Borrowed game assets are never destroyed here.
        public static TMP_FontAsset LoadHudFont() => HudStyle.Font;

        internal static void Reset()
        {
            _panelFills.Clear();
            _panelRims.Clear();
        }

        public static RectTransform CreatePanel(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            var panelObject = new GameObject(name);
            panelObject.transform.SetParent(parent, false);

            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = anchorMin;
            panelRect.anchorMax = anchorMax;
            panelRect.pivot = pivot;
            panelRect.anchoredPosition = anchoredPosition;
            panelRect.sizeDelta = sizeDelta;

            _panelFills.Add(CreateChrome(panelObject.transform, "Glass", "panel_bg.png", 0));
            _panelRims.Add(CreateChrome(panelObject.transform, "Rim", "panel_stroke.png", 1));

            ApplyTheme();
            return panelRect;
        }

        // Called once at startup and again whenever the opacity setting changes,
        // so the per-frame path stays free of config-driven color work.
        public static void ApplyTheme()
        {
            float opacity = InfoPanelSettings.PanelOpacity != null ? InfoPanelSettings.PanelOpacity.Value : 0.55f;

            var fill = new Color(PanelTint.r, PanelTint.g, PanelTint.b, opacity);
            for (int i = 0; i < _panelFills.Count; i++)
            {
                if (_panelFills[i] != null)
                {
                    _panelFills[i].color = fill;
                    _panelFills[i].enabled = opacity > 0.001f;
                }
            }

            // The rim fades out together with the fill, so opacity 0 gives a
            // completely borderless HUD rather than floating outlines.
            var rim = new Color(RimColor.r, RimColor.g, RimColor.b, RimColor.a * Mathf.Clamp01(opacity / 0.55f));
            for (int i = 0; i < _panelRims.Count; i++)
            {
                if (_panelRims[i] != null)
                {
                    _panelRims[i].color = rim;
                    _panelRims[i].enabled = rim.a > 0.002f;
                }
            }
        }

        // Position and scale in one call, so every panel honors the scale
        // setting without each of them repeating the clamp.
        public static void ApplyPanelTransform(RectTransform panelRect, Vector2 offset)
        {
            panelRect.anchoredPosition = offset;

            float scale = InfoPanelSettings.UiScale != null ? Mathf.Clamp(InfoPanelSettings.UiScale.Value, 0.5f, 2f) : 1f;
            panelRect.localScale = new Vector3(scale, scale, 1f);
        }

        public static TextMeshProUGUI CreateLine(RectTransform panelRect, string name, float topOffset, float height)
        {
            var textObject = new GameObject(name);
            textObject.transform.SetParent(panelRect, false);

            var rectTransform = textObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(1f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.anchoredPosition = new Vector2(0f, topOffset);
            rectTransform.sizeDelta = new Vector2(-24f, height);

            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = LoadHudFont();
            text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontSize = 18f;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.margin = new Vector4(12f, 0f, 12f, 0f);
            return text;
        }

        public static void ApplyTextColor(TextMeshProUGUI text, Color color)
        {
            text.color = color;
            // Just enough edge to stay legible on snow or sky; the old heavier
            // outline made the type look blocky against the glass panel.
            HudStyle.ApplyOutline(text);
        }

        private static Image CreateChrome(Transform parent, string name, string spriteFile, int siblingIndex)
        {
            var chromeObject = new GameObject(name);
            chromeObject.transform.SetParent(parent, false);
            chromeObject.transform.SetSiblingIndex(siblingIndex);

            var rectTransform = chromeObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            var image = chromeObject.AddComponent<Image>();
            image.sprite = EmbeddedIcons.GetSliced(spriteFile, PanelSliceBorder);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1.8f;
            image.raycastTarget = false;
            return image;
        }
    }
}
