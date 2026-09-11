using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud.InfoPanels.UI
{
    // A single horizontal row of active-status badges (icon + body part), e.g.
    // "[drop] L.ARM   [drops] CHEST   [bone] R.LEG". Kept to one line so the
    // panel height stays fixed whether or not anything is wrong.
    internal sealed class StatusStripView
    {
        public const int MaxEntries = 3;

        private const float SlotWidth = 76f;
        private const float IconSize = 18f;
        private const float LabelLeft = 21f;

        private readonly RectTransform _root;
        private readonly Image[] _icons = new Image[MaxEntries];
        private readonly TextMeshProUGUI[] _labels = new TextMeshProUGUI[MaxEntries];

        public StatusStripView(RectTransform panelRect, float topOffset, float height)
        {
            var rowObject = new GameObject("StatusStrip");
            rowObject.transform.SetParent(panelRect, false);

            _root = rowObject.AddComponent<RectTransform>();
            _root.anchorMin = new Vector2(0f, 1f);
            _root.anchorMax = new Vector2(1f, 1f);
            _root.pivot = new Vector2(0.5f, 1f);
            _root.anchoredPosition = new Vector2(0f, topOffset);
            _root.sizeDelta = new Vector2(-24f, height);

            for (int i = 0; i < MaxEntries; i++)
            {
                var slot = new GameObject($"Status{i}");
                slot.transform.SetParent(_root, false);

                var slotRect = slot.AddComponent<RectTransform>();
                slotRect.anchorMin = new Vector2(0f, 0f);
                slotRect.anchorMax = new Vector2(0f, 1f);
                slotRect.pivot = new Vector2(0f, 0.5f);
                slotRect.anchoredPosition = new Vector2(i * SlotWidth, 0f);
                slotRect.sizeDelta = new Vector2(SlotWidth, 0f);

                _icons[i] = CreateIcon(slotRect);
                _labels[i] = CreateLabel(slotRect);
                slot.SetActive(false);
            }
        }

        public void SetEntries(BodyStatusEntry[] entries, int count)
        {
            for (int i = 0; i < MaxEntries; i++)
            {
                bool visible = i < count;
                _icons[i].transform.parent.gameObject.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                _icons[i].sprite = GetSprite(entries[i].Kind);
                _icons[i].enabled = _icons[i].sprite != null;
                _labels[i].text = BodyStatusCounter.GetBodyPartLabel(entries[i].BodyPart);
                PanelStyle.ApplyTextColor(_labels[i], GetColor(entries[i].Kind));
            }
        }

        public void Clear()
        {
            for (int i = 0; i < MaxEntries; i++)
            {
                _icons[i].transform.parent.gameObject.SetActive(false);
            }
        }

        private static Sprite GetSprite(BodyStatusKind kind)
        {
            switch (kind)
            {
                case BodyStatusKind.HeavyBleed:
                    return EmbeddedIcons.Get("status_bleed_heavy.png");
                case BodyStatusKind.Fracture:
                    return EmbeddedIcons.Get("status_fracture.png");
                default:
                    return EmbeddedIcons.Get("status_bleed_light.png");
            }
        }

        // The icons carry their own color, so the label just echoes it to tie
        // the pair together.
        private static Color GetColor(BodyStatusKind kind)
        {
            switch (kind)
            {
                case BodyStatusKind.HeavyBleed:
                    return new Color(0.92f, 0.32f, 0.30f, 0.96f);
                case BodyStatusKind.Fracture:
                    return new Color(0.90f, 0.90f, 0.86f, 0.96f);
                default:
                    return new Color(0.91f, 0.55f, 0.50f, 0.96f);
            }
        }

        private static Image CreateIcon(RectTransform parent)
        {
            var iconObject = new GameObject("Icon");
            iconObject.transform.SetParent(parent, false);

            var rectTransform = iconObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0f, 0.5f);
            rectTransform.anchorMax = new Vector2(0f, 0.5f);
            rectTransform.pivot = new Vector2(0f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0f, 0f);
            rectTransform.sizeDelta = new Vector2(IconSize, IconSize);

            var image = iconObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI CreateLabel(RectTransform parent)
        {
            var textObject = new GameObject("Label");
            textObject.transform.SetParent(parent, false);

            var rectTransform = textObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0f, 0f);
            rectTransform.anchorMax = new Vector2(1f, 1f);
            rectTransform.offsetMin = new Vector2(LabelLeft, 0f);
            rectTransform.offsetMax = new Vector2(-4f, 0f);

            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = PanelStyle.LoadHudFont();
            text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontSize = 12f;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.margin = Vector4.zero;
            return text;
        }
    }
}
