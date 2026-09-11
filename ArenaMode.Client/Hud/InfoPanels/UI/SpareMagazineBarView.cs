using TMPro;

using UnityEngine;



namespace ArenaMode.Hud.InfoPanels.UI

{

    internal sealed class SpareMagazineBarView

    {

        private const float SlotWidth = 88f;

        private const float RowHeight = 30f;



        private readonly RectTransform _root;

        private readonly MagazineCountSlotView _fullSlot;

        private readonly MagazineCountSlotView _partialSlot;

        private readonly MagazineCountSlotView _backpackSlot;



        public SpareMagazineBarView(RectTransform panelRect, float topOffset)

        {

            _root = CreateRowRoot(panelRect, topOffset);

            _fullSlot = new MagazineCountSlotView(_root, "A", 0f);

            _partialSlot = new MagazineCountSlotView(_root, "B", SlotWidth);

            _backpackSlot = new MagazineCountSlotView(_root, "C", SlotWidth * 2f);

        }



        public void SetSummary(int fullCount, int partialCount, int backpackCount, Color color)

        {

            _fullSlot.Set(MagazineVisualStyle.Full, fullCount, color);

            _partialSlot.Set(MagazineVisualStyle.Partial, partialCount, color);

            _backpackSlot.Set(MagazineVisualStyle.Backpack, backpackCount, color);



            bool hasAny = fullCount > 0 || partialCount > 0 || backpackCount > 0;

            _root.gameObject.SetActive(hasAny);

        }



        // Shell-by-shell weapons have no spare magazines to count, so the same

        // row shows carried loose cartridges instead: reachable first, backpack

        // second, and the third slot stays empty.

        public void SetRoundSummary(int readyRounds, int backpackRounds, Color color)

        {

            _fullSlot.Set(MagazineVisualStyle.LooseRounds, readyRounds, color);

            _partialSlot.Set(MagazineVisualStyle.LooseRoundsBackpack, backpackRounds, color);

            _backpackSlot.Hide();



            _root.gameObject.SetActive(readyRounds > 0 || backpackRounds > 0);

        }



        public void Hide()

        {

            _fullSlot.Hide();

            _partialSlot.Hide();

            _backpackSlot.Hide();

            _root.gameObject.SetActive(false);

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

        private const float CountTextLeft = 30f;



        private readonly RectTransform _root;

        private readonly MagazineIconWidget _icon;

        private readonly TextMeshProUGUI _countText;



        public MagazineCountSlotView(RectTransform parent, string name, float slotOffsetX)

        {

            var slotObject = new GameObject($"MagSlot_{name}");

            slotObject.transform.SetParent(parent, false);



            _root = slotObject.AddComponent<RectTransform>();

            _root.anchorMin = new Vector2(0f, 0f);

            _root.anchorMax = new Vector2(0f, 1f);

            _root.pivot = new Vector2(0f, 0.5f);

            _root.anchoredPosition = new Vector2(8f + slotOffsetX, 0f);

            _root.sizeDelta = new Vector2(80f, 0f);



            _icon = new MagazineIconWidget(_root, 0f);

            _countText = CreateCountText(_root);

        }



        public void Set(MagazineVisualStyle style, int count, Color color)

        {

            if (count <= 0)

            {

                Hide();

                return;

            }



            _root.gameObject.SetActive(true);

            _icon.Apply(style, color);

            _countText.text = count.ToString();

            PanelStyle.ApplyTextColor(_countText, color);

        }



        public void Hide()

        {

            _root.gameObject.SetActive(false);

            _countText.text = string.Empty;

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

            text.fontSize = 18f;

            text.enableWordWrapping = false;

            text.overflowMode = TextOverflowModes.Ellipsis;

            text.margin = Vector4.zero;

            return text;

        }

    }

}
