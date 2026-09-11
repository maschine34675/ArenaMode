using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud.InfoPanels.UI
{
    // One row with a fixed slot per grenade kind (frag, flash, smoke), mirroring
    // the spare-magazine bar: each kind always sits in the same place, so the
    // eye learns where to look, and an empty kind leaves its slot hidden.
    //
    // The number is what the native quick throw can take (see
    // GrenadeInventoryCounter: vanilla rig/pockets top level; the installed
    // UseItemsAnywhere widens that to the backpack's top level). Grenades
    // carried outside that set, e.g. in a nested pouch, are appended dimmed as
    // "+N", and a kind that is only carried there shows "0 +N" with a dimmed
    // icon: "you have these, but G's candidate list holds none". The counter
    // documents the one TopPriorityGrenade exception to that reading.
    internal sealed class GrenadeBarView
    {
        private const float SlotWidth = 88f;
        private const float RowHeight = 28f;
        private const float IconSize = 22f;
        private const float CountTextLeft = 27f;

        private readonly RectTransform _root;
        private readonly GameObject[] _slots = new GameObject[3];
        private readonly Image[] _icons = new Image[3];
        private readonly TextMeshProUGUI[] _counts = new TextMeshProUGUI[3];

        private static readonly string[] IconFiles =
        {
            "grenade_frag.png",
            "grenade_flash.png",
            "grenade_smoke.png"
        };

        public GrenadeBarView(RectTransform panelRect, float topOffset)
        {
            var rowObject = new GameObject("GrenadeBar");
            rowObject.transform.SetParent(panelRect, false);

            _root = rowObject.AddComponent<RectTransform>();
            _root.anchorMin = new Vector2(0f, 1f);
            _root.anchorMax = new Vector2(1f, 1f);
            _root.pivot = new Vector2(0.5f, 1f);
            _root.anchoredPosition = new Vector2(0f, topOffset);
            _root.sizeDelta = new Vector2(-16f, RowHeight);

            for (int i = 0; i < 3; i++)
            {
                var slot = new GameObject($"Grenade_{(GrenadeKind)i}");
                slot.transform.SetParent(_root, false);

                var slotRect = slot.AddComponent<RectTransform>();
                slotRect.anchorMin = new Vector2(0f, 0f);
                slotRect.anchorMax = new Vector2(0f, 1f);
                slotRect.pivot = new Vector2(0f, 0.5f);
                slotRect.anchoredPosition = new Vector2(8f + i * SlotWidth, 0f);
                slotRect.sizeDelta = new Vector2(SlotWidth - 8f, 0f);

                _slots[i] = slot;
                _icons[i] = CreateIcon(slotRect);
                _counts[i] = CreateCount(slotRect);
                slot.SetActive(false);
            }

            rowObject.SetActive(false);
        }

        public void SetSummary(GrenadeInventoryCounter.GrenadeSummary summary, Color color)
        {
            for (int i = 0; i < 3; i++)
            {
                var kind = (GrenadeKind)i;
                int ready = summary.Ready(kind);
                int stored = summary.Stored(kind);
                bool visible = ready > 0 || stored > 0;
                _slots[i].SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                Sprite sprite = EmbeddedIcons.Get(IconFiles[i]);
                _icons[i].sprite = sprite;
                _icons[i].enabled = sprite != null;
                // Dimmed when G's candidate list holds none of this kind.
                _icons[i].color = ready > 0 ? Color.white : new Color(1f, 1f, 1f, 0.45f);

                _counts[i].text = stored > 0
                    ? $"{ready}<alpha=#80> +{stored}"
                    : ready.ToString();
                PanelStyle.ApplyTextColor(_counts[i], color);
            }

            _root.gameObject.SetActive(!summary.IsEmpty);
        }

        public void Hide()
        {
            for (int i = 0; i < 3; i++)
            {
                _slots[i].SetActive(false);
                _counts[i].text = string.Empty;
            }

            _root.gameObject.SetActive(false);
        }

        private static Image CreateIcon(RectTransform parent)
        {
            var iconObject = new GameObject("Icon");
            iconObject.transform.SetParent(parent, false);

            var rect = iconObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(IconSize, IconSize);

            var image = iconObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI CreateCount(RectTransform parent)
        {
            var textObject = new GameObject("Count");
            textObject.transform.SetParent(parent, false);

            var rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(CountTextLeft, 0f);
            rect.offsetMax = new Vector2(-2f, 0f);

            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = PanelStyle.LoadHudFont();
            text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontSize = 17f;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.margin = Vector4.zero;
            return text;
        }
    }
}
