using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud.InfoPanels.UI
{
    internal sealed class StatRowView
    {
        private const float IconSize = 20f;
        private const float IconLeft = 0f;
        private const float TextLeft = 26f;

        private readonly RectTransform _root;
        private readonly Image _icon;
        private readonly TextMeshProUGUI _text;

        // Explicitly placed rather than stretched, so several rows can sit
        // side by side within one panel.
        public StatRowView(RectTransform panelRect, string name, float x, float topOffset, float width, float height)
        {
            _root = CreateRowRoot(panelRect, name, x, topOffset, width, height);
            _icon = CreateIcon(_root);
            _text = CreateText(_root, height);
        }

        public void Set(Sprite icon, string text, Color color)
        {
            if (icon != null)
            {
                _icon.enabled = true;
                _icon.sprite = icon;
                _icon.color = color;
            }
            else
            {
                _icon.enabled = false;
            }

            _text.text = text;
            PanelStyle.ApplyTextColor(_text, color);
        }

        public void Clear()
        {
            _icon.enabled = false;
            _text.text = string.Empty;
        }

        private static RectTransform CreateRowRoot(
            RectTransform panelRect, string name, float x, float topOffset, float width, float height)
        {
            var rowObject = new GameObject(name);
            rowObject.transform.SetParent(panelRect, false);

            var rectTransform = rowObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(0f, 1f);
            rectTransform.pivot = new Vector2(0f, 1f);
            rectTransform.anchoredPosition = new Vector2(x, topOffset);
            rectTransform.sizeDelta = new Vector2(width, height);
            return rectTransform;
        }

        private static Image CreateIcon(RectTransform parent)
        {
            var iconObject = new GameObject("Icon");
            iconObject.transform.SetParent(parent, false);

            var rectTransform = iconObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0f, 0.5f);
            rectTransform.anchorMax = new Vector2(0f, 0.5f);
            rectTransform.pivot = new Vector2(0f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(IconLeft, 0f);
            rectTransform.sizeDelta = new Vector2(IconSize, IconSize);

            var image = iconObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.enabled = false;
            return image;
        }

        private static TextMeshProUGUI CreateText(RectTransform parent, float height)
        {
            var textObject = new GameObject("Text");
            textObject.transform.SetParent(parent, false);

            var rectTransform = textObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0f, 0f);
            rectTransform.anchorMax = new Vector2(1f, 1f);
            rectTransform.offsetMin = new Vector2(TextLeft, 0f);
            rectTransform.offsetMax = new Vector2(-8f, 0f);

            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = PanelStyle.LoadHudFont();
            text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontSize = 18f;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.margin = Vector4.zero;
            return text;
        }
    }
}
