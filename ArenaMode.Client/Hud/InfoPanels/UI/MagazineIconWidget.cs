using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud.InfoPanels.UI
{
    internal enum MagazineVisualStyle
    {
        Full,
        Partial,
        LooseRounds,
        LooseRoundsBackpack
    }

    // One image per slot; each visual state is baked into a purpose-built
    // embedded sprite (see EmbeddedIcons / tools/RaidInfoPanelIcons/gen_icons.ps1). The earlier
    // approach — EFT's magazine sprite with masked ammo icons stacked inside —
    // was unreadable at this display size. There used to be a fifth state,
    // a backpack magazine icon; the reserve row no longer shows a separate
    // backpack magazine bucket (backpack spares that R can actually load now
    // count as full/partial like any other spare), so that style and its
    // embedded icon are retired.
    internal sealed class MagazineIconWidget
    {
        private const float IconWidth = 24f;
        private const float IconHeight = 30f;

        private readonly Image _image;

        public RectTransform Root { get; }

        public MagazineIconWidget(Transform parent, float anchoredPositionX)
        {
            var rootObject = new GameObject("MagazineIcon");
            rootObject.transform.SetParent(parent, false);
            Root = rootObject.AddComponent<RectTransform>();
            Root.anchorMin = new Vector2(0f, 0.5f);
            Root.anchorMax = new Vector2(0f, 0.5f);
            Root.pivot = new Vector2(0f, 0.5f);
            Root.anchoredPosition = new Vector2(anchoredPositionX, 0f);
            Root.sizeDelta = new Vector2(IconWidth, IconHeight);

            _image = rootObject.AddComponent<Image>();
            _image.preserveAspect = true;
            _image.raycastTarget = false;
        }

        public void Apply(MagazineVisualStyle style, Color accentColor)
        {
            Sprite sprite;
            switch (style)
            {
                case MagazineVisualStyle.Full:
                    sprite = EmbeddedIcons.MagFull;
                    break;
                case MagazineVisualStyle.Partial:
                    sprite = EmbeddedIcons.MagPartial;
                    break;
                case MagazineVisualStyle.LooseRoundsBackpack:
                    sprite = EmbeddedIcons.Get("rounds_backpack.png");
                    break;
                default: // LooseRounds
                    sprite = EmbeddedIcons.Get("rounds_ready.png");
                    break;
            }

            if (sprite != null)
            {
                // The embedded glyphs carry their own palette; the accent color
                // stays on the count text next to the icon.
                _image.sprite = sprite;
                _image.color = Color.white;
                return;
            }

            // Fallback if the embedded resources are ever missing: tinted game
            // magazine silhouette, dimmed for the non-quick-reload styles.
            _image.sprite = GameIconLoader.GetMagazineIcon();
            float alpha = style == MagazineVisualStyle.Full ? 0.95f : 0.55f;
            _image.color = new Color(accentColor.r, accentColor.g, accentColor.b, alpha);
        }
    }
}
