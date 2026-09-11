using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud.InfoPanels.UI
{
    // Right half of the weapon panel's reserve row (see ReserveRowLayout): one
    // slot per grenade kind, packed from the right edge with frag outermost,
    // then flash, then smoke. An empty kind takes no room.
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
        // Same height as the magazine row, so both groups share its midline.
        private const float RowHeight = 30f;
        private const float ContentInset = 8f;
        private const float SlotSlack = 4f;
        private const float IconSize = 22f;
        private const float CountTextLeft = 27f;
        private const float CountFontSize = 17f;

        private readonly RectTransform _root;
        private readonly RectTransform[] _slots = new RectTransform[3];
        private readonly Image[] _icons = new Image[3];
        private readonly TextMeshProUGUI[] _counts = new TextMeshProUGUI[3];
        private readonly int[] _shownReady = { -1, -1, -1 };
        private readonly int[] _shownStored = { -1, -1, -1 };
        private readonly float[] _widths = new float[3];
        private readonly bool[] _visible = new bool[3];
        private readonly float[] _rightInsets = new float[3];

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
                slotRect.anchorMin = new Vector2(1f, 0f);
                slotRect.anchorMax = new Vector2(1f, 1f);
                slotRect.pivot = new Vector2(1f, 0.5f);
                slotRect.anchoredPosition = new Vector2(-ContentInset, 0f);
                slotRect.sizeDelta = new Vector2(80f, 0f);

                _slots[i] = slotRect;
                _icons[i] = CreateIcon(slotRect);
                _counts[i] = CreateCount(slotRect);
                slot.SetActive(false);
            }

            rowObject.SetActive(false);
        }

        // Total width of the visible slots including their spacing; 0 while
        // nothing is shown.
        public float GroupWidth { get; private set; }

        // The reserve row, or the row below it when the magazines leave too
        // little room (WeaponInfoPanel decides).
        public void PlaceOnRow(float topOffset)
        {
            if (_root.anchoredPosition.y != topOffset)
            {
                _root.anchoredPosition = new Vector2(0f, topOffset);
            }
        }

        public void SetSummary(GrenadeInventoryCounter.GrenadeSummary summary, Color color)
        {
            for (int i = 0; i < 3; i++)
            {
                var kind = (GrenadeKind)i;
                int ready = summary.Ready(kind);
                int stored = summary.Stored(kind);
                _visible[i] = ready > 0 || stored > 0;
                _slots[i].gameObject.SetActive(_visible[i]);
                if (!_visible[i])
                {
                    ForgetShown(i);
                    continue;
                }

                Sprite sprite = EmbeddedIcons.Get(IconFiles[i]);
                _icons[i].sprite = sprite;
                _icons[i].enabled = sprite != null;
                // Dimmed when G's candidate list holds none of this kind.
                _icons[i].color = ready > 0 ? Color.white : new Color(1f, 1f, 1f, 0.45f);

                if (ready != _shownReady[i] || stored != _shownStored[i])
                {
                    _counts[i].text = stored > 0
                        ? $"{ready}<alpha=#80> +{stored}"
                        : ready.ToString();
                    _shownReady[i] = ready;
                    _shownStored[i] = stored;
                    int glyphs = Digits(ready) + (stored > 0 ? 2 + Digits(stored) : 0);
                    _widths[i] = ReserveRowLayout.SlotWidth(
                        CountTextLeft, PanelStyle.MeasureTextWidth(_counts[i]), glyphs, CountFontSize);
                }

                PanelStyle.ApplyTextColor(_counts[i], color);
            }

            GroupWidth = ReserveRowLayout.PackFromRight(_widths, _visible, _rightInsets);
            for (int i = 0; i < 3; i++)
            {
                if (_visible[i])
                {
                    // The pivot is the slot's right edge, and the slack belongs
                    // beyond the count as on the magazine slots, so shift by
                    // it: icon and count then span exactly the packed width.
                    _slots[i].anchoredPosition = new Vector2(SlotSlack - (ContentInset + _rightInsets[i]), 0f);
                    _slots[i].sizeDelta = new Vector2(_widths[i] + SlotSlack, 0f);
                }
            }

            _root.gameObject.SetActive(!summary.IsEmpty);
        }

        public void Hide()
        {
            for (int i = 0; i < 3; i++)
            {
                _visible[i] = false;
                _slots[i].gameObject.SetActive(false);
                ForgetShown(i);
            }

            _root.gameObject.SetActive(false);
            GroupWidth = 0f;
        }

        private void ForgetShown(int i)
        {
            _counts[i].text = string.Empty;
            _shownReady[i] = -1;
            _shownStored[i] = -1;
        }

        private static int Digits(int value) => value.ToString().Length;

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
            text.fontSize = CountFontSize;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.margin = Vector4.zero;
            return text;
        }
    }
}
