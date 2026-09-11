using TMPro;
using UnityEngine;

namespace ArenaMode.Hud.InfoPanels.UI
{
    // Left half of the weapon panel's reserve row (see ReserveRowLayout):
    // full, partial and backpack magazines, or loose cartridges for weapons
    // that load shell by shell. The grenade group shares the row from the
    // right, so the slots pack by their measured width instead of the old
    // fixed 88 px pitch.
    internal sealed class SpareMagazineBarView
    {
        private const float RowHeight = 30f;

        private readonly RectTransform _root;
        private readonly MagazineCountSlotView[] _slots = new MagazineCountSlotView[3];
        private readonly float[] _widths = new float[3];
        private readonly bool[] _visible = new bool[3];
        private readonly float[] _lefts = new float[3];

        public SpareMagazineBarView(RectTransform panelRect, float topOffset)
        {
            _root = CreateRowRoot(panelRect, topOffset);
            _slots[0] = new MagazineCountSlotView(_root, "A");
            _slots[1] = new MagazineCountSlotView(_root, "B");
            _slots[2] = new MagazineCountSlotView(_root, "C");
        }

        // Right edge of the last visible slot within the row content; 0 while
        // nothing is shown.
        public float ContentRight { get; private set; }

        public void SetSummary(int fullCount, int partialCount, int backpackCount, Color color)
        {
            _slots[0].Set(MagazineVisualStyle.Full, fullCount, color);
            _slots[1].Set(MagazineVisualStyle.Partial, partialCount, color);
            _slots[2].Set(MagazineVisualStyle.Backpack, backpackCount, color);
            Arrange();
        }

        // Shell-by-shell weapons have no spare magazines to count, so the same
        // row shows carried loose cartridges instead: reachable first, backpack
        // second, and the third slot stays empty.
        public void SetRoundSummary(int readyRounds, int backpackRounds, Color color)
        {
            _slots[0].Set(MagazineVisualStyle.LooseRounds, readyRounds, color);
            _slots[1].Set(MagazineVisualStyle.LooseRoundsBackpack, backpackRounds, color);
            _slots[2].Hide();
            Arrange();
        }

        public void Hide()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].Hide();
            }

            _root.gameObject.SetActive(false);
            ContentRight = 0f;
        }

        private void Arrange()
        {
            bool hasAny = false;
            for (int i = 0; i < _slots.Length; i++)
            {
                _visible[i] = _slots[i].IsVisible;
                _widths[i] = _slots[i].Width;
                hasAny |= _visible[i];
            }

            ContentRight = ReserveRowLayout.PackFromLeft(_widths, _visible, _lefts);
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_visible[i])
                {
                    _slots[i].PlaceAt(_lefts[i]);
                }
            }

            _root.gameObject.SetActive(hasAny);
        }

        private static RectTransform CreateRowRoot(RectTransform panelRect, float topOffset)
        {
            var rowObject = new GameObject("SpareMagazineBar");
            rowObject.transform.SetParent(panelRect, false);

            var rectTransform = rowObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(1f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.anchoredPosition = new Vector2(0f, topOffset);
            rectTransform.sizeDelta = new Vector2(-16f, RowHeight);
            return rectTransform;
        }
    }

    internal sealed class MagazineCountSlotView
    {
        private const float ContentInset = 8f;
        private const float CountTextLeft = 30f;
        private const float CountFontSize = 18f;

        private readonly RectTransform _root;
        private readonly MagazineIconWidget _icon;
        private readonly TextMeshProUGUI _countText;
        private int _shownCount = -1;

        public MagazineCountSlotView(RectTransform parent, string name)
        {
            var slotObject = new GameObject($"MagSlot_{name}");
            slotObject.transform.SetParent(parent, false);

            _root = slotObject.AddComponent<RectTransform>();
            _root.anchorMin = new Vector2(0f, 0f);
            _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 0.5f);
            _root.anchoredPosition = new Vector2(ContentInset, 0f);
            _root.sizeDelta = new Vector2(80f, 0f);

            _icon = new MagazineIconWidget(_root, 0f);
            _countText = CreateCountText(_root);
        }

        public bool IsVisible { get; private set; }

        // Icon column plus the measured count, valid while visible.
        public float Width { get; private set; }

        public void Set(MagazineVisualStyle style, int count, Color color)
        {
            if (count <= 0)
            {
                Hide();
                return;
            }

            IsVisible = true;
            _root.gameObject.SetActive(true);
            _icon.Apply(style, color);
            if (count != _shownCount)
            {
                string text = count.ToString();
                _countText.text = text;
                _shownCount = count;
                Width = ReserveRowLayout.SlotWidth(
                    CountTextLeft, PanelStyle.MeasureTextWidth(_countText), text.Length, CountFontSize);
            }

            PanelStyle.ApplyTextColor(_countText, color);
        }

        public void PlaceAt(float left)
        {
            _root.anchoredPosition = new Vector2(ContentInset + left, 0f);
            // A little slack beyond the measurement; the count overflows
            // rather than truncating anyway.
            _root.sizeDelta = new Vector2(Width + 4f, 0f);
        }

        public void Hide()
        {
            IsVisible = false;
            _root.gameObject.SetActive(false);
            _countText.text = string.Empty;
            _shownCount = -1;
        }

        private static TextMeshProUGUI CreateCountText(RectTransform parent)
        {
            var textObject = new GameObject("Count");
            textObject.transform.SetParent(parent, false);

            var rectTransform = textObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0f, 0f);
            rectTransform.anchorMax = new Vector2(1f, 1f);
            rectTransform.offsetMin = new Vector2(CountTextLeft, 0f);
            rectTransform.offsetMax = new Vector2(-2f, 0f);

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
