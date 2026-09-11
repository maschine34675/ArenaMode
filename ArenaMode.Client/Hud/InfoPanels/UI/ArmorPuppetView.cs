using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud.InfoPanels.UI
{
    // One paper-doll figure: neutral gray body silhouette with one tintable
    // white overlay per fine armor zone. All sprites share the same 128x256
    // canvas (see tools/RaidInfoPanelIcons/gen_puppet.ps1), so stretching every layer over the
    // same rect keeps the zones pixel-aligned with the base. The band sprites
    // (throat/thorax/stomach/pelvis) are reused by the back figure for the
    // corresponding back zones.
    internal sealed class ArmorPuppetView
    {
        public RectTransform Root { get; }

        private readonly Image[] _overlays = new Image[ArmorDurabilityCounter.ZoneCount];

        public ArmorPuppetView(
            RectTransform parent,
            float x,
            float topOffset,
            float width,
            float height,
            string label,
            bool isFrontFigure)
        {
            var rootObject = new GameObject($"ArmorPuppet_{label}");
            rootObject.transform.SetParent(parent, false);
            Root = rootObject.AddComponent<RectTransform>();
            Root.anchorMin = new Vector2(0f, 1f);
            Root.anchorMax = new Vector2(0f, 1f);
            Root.pivot = new Vector2(0f, 1f);
            Root.anchoredPosition = new Vector2(x, topOffset);
            Root.sizeDelta = new Vector2(width, height);

            CreateLayer("Base", "armor_base.png");

            if (isFrontFigure)
            {
                AddZone(FineArmorZone.Head, "armor_zone_head.png");
                AddZone(FineArmorZone.Throat, "armor_zone_throat.png");
                AddZone(FineArmorZone.ThoraxFront, "armor_zone_thorax.png");
                AddZone(FineArmorZone.StomachFront, "armor_zone_stomach.png");
                AddZone(FineArmorZone.PelvisFront, "armor_zone_pelvis.png");
                AddZone(FineArmorZone.LeftSide, "armor_zone_side_left.png");
                AddZone(FineArmorZone.RightSide, "armor_zone_side_right.png");
                AddZone(FineArmorZone.LeftUpperArm, "armor_zone_arm_upper_left.png");
                AddZone(FineArmorZone.LeftForearm, "armor_zone_arm_fore_left.png");
                AddZone(FineArmorZone.RightUpperArm, "armor_zone_arm_upper_right.png");
                AddZone(FineArmorZone.RightForearm, "armor_zone_arm_fore_right.png");
                AddZone(FineArmorZone.LeftThigh, "armor_zone_thigh_left.png");
                AddZone(FineArmorZone.LeftCalf, "armor_zone_calf_left.png");
                AddZone(FineArmorZone.RightThigh, "armor_zone_thigh_right.png");
                AddZone(FineArmorZone.RightCalf, "armor_zone_calf_right.png");
            }
            else
            {
                AddZone(FineArmorZone.ThroatBack, "armor_zone_throat.png");
                AddZone(FineArmorZone.ThoraxBack, "armor_zone_thorax.png");
                AddZone(FineArmorZone.StomachBack, "armor_zone_stomach.png");
                AddZone(FineArmorZone.PelvisBack, "armor_zone_pelvis.png");
            }

            CreateLabel(label);
        }

        // Ignores zones this figure doesn't display, so the caller can feed
        // every zone to both figures without bookkeeping.
        public void SetZone(FineArmorZone zone, bool visible, Color color)
        {
            Image overlay = _overlays[(int)zone];
            if (overlay == null)
            {
                return;
            }

            overlay.enabled = visible;
            if (visible)
            {
                overlay.color = color;
            }
        }

        public void HideZones()
        {
            for (int i = 0; i < _overlays.Length; i++)
            {
                if (_overlays[i] != null)
                {
                    _overlays[i].enabled = false;
                }
            }
        }

        private void AddZone(FineArmorZone zone, string spriteFile)
        {
            _overlays[(int)zone] = CreateLayer(zone.ToString(), spriteFile);
        }

        private Image CreateLayer(string name, string spriteFile)
        {
            var layerObject = new GameObject(name);
            layerObject.transform.SetParent(Root, false);

            var rectTransform = layerObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            Sprite sprite = EmbeddedIcons.Get(spriteFile);
            var image = layerObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = sprite != null;
            return image;
        }

        private void CreateLabel(string label)
        {
            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(Root, false);

            var rectTransform = labelObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0f, 0f);
            rectTransform.anchorMax = new Vector2(1f, 0f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.anchoredPosition = new Vector2(0f, -1f);
            rectTransform.sizeDelta = new Vector2(0f, 12f);

            var text = labelObject.AddComponent<TextMeshProUGUI>();
            text.font = PanelStyle.LoadHudFont();
            text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 10f;
            text.text = label;
            text.color = new Color(0.75f, 0.79f, 0.85f, 0.55f);
        }
    }
}
