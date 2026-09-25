using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud.InfoPanels.UI
{
    // Right half of the weapon panel's reserve row (see ReserveRowLayout): one
    // slot per grenade kind, packed from the right edge with frag outermost,
    // then flash, then smoke. An empty kind takes no room.
    //
    // The number is "usable": every carried grenade of that kind
    // GrenadeInventoryCounter finds reachable — native rig/pockets recursion
    // (nested pouches included) always, widened to the backpack (top level or
    // nested) inside the arena when Settings.UseItemsAnywhere is on
    // (ArenaMode.Client/Kits/ArenaReachPatches.cs). The icon dims when none of
    // that kind is on the native tap-G candidate list, which stays rig/pockets
    // top level only regardless of the setting: "usable by number key, but G
    // itself won't draw one". That is the one place tap-G and this row can
    // disagree — GRENADE-PICK-REVIEW.md documents the TopPriorityGrenade
    // exception that can otherwise leave G unable to throw even a dimmed-in
    // kind until a fresh grenade is drawn.
    internal sealed class GrenadeBarView
    {
        // Same height as the magazine row, so both groups share its midline.
        private const float RowHeight = 30f;
        private const float ContentInset = 8f;
        // Fixed inset from the panel's own edge to this row's pivot (see the
        // constructor); never scales, unlike everything drawn inside it.
        private const float RootInset = 8f;
        private const float SlotSlack = 4f;
        private const float IconSize = 22f;
        private const float CountTextLeft = 27f;
        private const float CountFontSize = 17f;

        private readonly RectTransform _root;
        private readonly RectTransform[] _slots = new RectTransform[3];
        private readonly Image[] _icons = new Image[3];
        private readonly TextMeshProUGUI[] _counts = new TextMeshProUGUI[3];
        private readonly int[] _shownUsable = { -1, -1, -1 };
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
            // Pivot on the RIGHT edge, not centered: ApplyScale shrinks this
            // row toward that edge, so grenades stay pinned to the row's
            // right content edge at any scale (ReserveRowLayout.FitScale).
            // anchoredPosition.x compensates for the pivot move so the drawn
            // right edge stays RootInset from the panel edge regardless of
            // scale, exactly where the centered pivot put it before.
            _root.pivot = new Vector2(1f, 1f);
            _root.anchoredPosition = new Vector2(-RootInset, topOffset);
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

        // Shrinks the whole row (icons, text and its own 8 px content inset)
        // toward its right edge so it never overlaps the magazine group; see
        // ReserveRowLayout.FitScale for the derivation and WeaponInfoPanel for
        // the caller. Skips the write once applied, matching the idle-work
        // style elsewhere in this panel (commit 92cbc5c).
        public void ApplyScale(float scale)
        {
            if (_root.localScale.x != scale)
            {
                _root.localScale = new Vector3(scale, scale, 1f);
            }
        }

        public void SetSummary(GrenadeInventoryCounter.GrenadeSummary summary, Color color)
        {
            for (int i = 0; i < 3; i++)
            {
                var kind = (GrenadeKind)i;
                int usable = summary.Usable(kind);
                _visible[i] = usable > 0;
                _slots[i].gameObject.SetActive(_visible[i]);
                if (!_visible[i])
                {
                    ForgetShown(i);
                    continue;
                }

                Sprite sprite = EmbeddedIcons.Get(IconFiles[i]);
                _icons[i].sprite = sprite;
                _icons[i].enabled = sprite != null;
                // Dimmed when none of this kind is on G's own candidate list,
                // even though it is still usable through a bound number key.
                _icons[i].color = summary.OnG(kind) > 0 ? Color.white : new Color(1f, 1f, 1f, 0.45f);

                if (usable != _shownUsable[i])
                {
                    _counts[i].text = usable.ToString();
                    _shownUsable[i] = usable;
                    _widths[i] = ReserveRowLayout.SlotWidth(
                        CountTextLeft, PanelStyle.MeasureTextWidth(_counts[i]), Digits(usable), CountFontSize);
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
            _shownUsable[i] = -1;
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
