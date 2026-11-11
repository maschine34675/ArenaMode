using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ArenaMode.Hud;

/// <summary>
/// The typeface of the kit browser: the native menu's typography on the map screen, the HUD
/// font in a raid. Arena-owned objects only; no shared material or prefab is changed.
/// </summary>
internal sealed class ArenaKitUiStyle
{
    internal TMP_FontAsset Font;
    internal Material FontMaterial;

    internal static ArenaKitUiStyle From(TMP_Text source)
        => source != null && source.font != null
            ? new ArenaKitUiStyle { Font = source.font, FontMaterial = source.fontSharedMaterial }
            : Hud();

    internal static ArenaKitUiStyle Hud() => new ArenaKitUiStyle { Font = HudStyle.Font };
}

/// <summary>
/// Building blocks of the kit browser (docs/reviews/KIT-PICKER-20261110.md), made in code from
/// the <see cref="ArenaUiTheme"/> palette: no native prefab is cloned, so the same pieces work on
/// the map screen and in a raid. Everything is positioned from the top left of its parent.
/// </summary>
internal static class ArenaKitUi
{
    internal static void Place(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
    }

    internal static RectTransform Box(Transform parent, string name, float left, float top, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        Place(rect, left, top, width, height);
        return rect;
    }

    /// <summary>A flat surface. One that <paramref name="blocksPointer"/> keeps clicks from what lies behind it.</summary>
    internal static Image Panel(Transform parent, string name, Color color, bool blocksPointer,
        float left, float top, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        Place(rect, left, top, width, height);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = blocksPointer;
        return image;
    }

    internal static TextMeshProUGUI Text(Transform parent, string name, ArenaKitUiStyle style, string value,
        float size, Color color, TextAlignmentOptions alignment, float left, float top, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        Place(rect, left, top, width, height);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = style?.Font ?? HudStyle.Font;
        if (style?.FontMaterial != null) text.fontSharedMaterial = style.FontMaterial;
        text.text = value ?? string.Empty;
        text.fontSize = size;
        text.fontStyle = FontStyles.Normal;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    internal static Button Button(Transform parent, string name, ArenaKitUiStyle style, string label, float fontSize,
        float left, float top, float width, float height, out TextMeshProUGUI text)
    {
        Image surface = Panel(parent, name, Color.white, true, left, top, width, height);
        var button = surface.gameObject.AddComponent<Button>();
        button.targetGraphic = surface;
        var colors = button.colors;
        colors.normalColor = ArenaUiTheme.WithAlpha(ArenaUiTheme.Raised, .92f);
        colors.highlightedColor = ArenaUiTheme.Hover;
        colors.pressedColor = ArenaUiTheme.WithAlpha(ArenaUiTheme.Accent, .60f);
        colors.selectedColor = ArenaUiTheme.Hover;
        colors.disabledColor = ArenaUiTheme.WithAlpha(ArenaUiTheme.Raised, .35f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = .10f;
        button.colors = colors;
        button.transition = Selectable.Transition.ColorTint;
        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        text = label == null ? null
            : Text(surface.transform, "Label", style, label, fontSize, ArenaUiTheme.Text,
                TextAlignmentOptions.Center, 0f, 0f, width, height);
        return button;
    }
}

/// <summary>Tells its card when the pointer enters and leaves it; the browser decides from that and the pointer's motion which kit the detail pane shows.</summary>
internal sealed class ArenaKitCardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    internal Action Entered, Exited;

    public void OnPointerEnter(PointerEventData eventData)
    {
        try { Entered?.Invoke(); }
        catch (Exception ex) { Plugin.ModLogger.LogDebug($"Arena kit card hover failed: {ex.Message}"); }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        try { Exited?.Invoke(); }
        catch (Exception ex) { Plugin.ModLogger.LogDebug($"Arena kit card hover failed: {ex.Message}"); }
    }
}

/// <summary>
/// The game's own rendered icon of an item on one of our images. The icon cache answers at once
/// when it has the picture and renders in the background otherwise, raising its Changed event;
/// the subscription ends with the image. The picture is produced by the game at run time from
/// the player's own install: no item art is shipped with the mod.
/// </summary>
internal sealed class ArenaKitItemIcon : MonoBehaviour
{
    private Action _unsubscribe;
    private ItemIcon _icon;
    private Image _image;
    private float _maxWidth, _maxHeight;
    private Vector2 _center;

    /// <summary>Shows <paramref name="item"/>'s icon centred in the given box of the image's parent, never enlarged.</summary>
    internal static void Show(Image image, Item item, float boxLeft, float boxTop, float boxWidth, float boxHeight)
    {
        if (image == null) return;
        var holder = image.GetComponent<ArenaKitItemIcon>() ?? image.gameObject.AddComponent<ArenaKitItemIcon>();
        holder._image = image;
        holder._maxWidth = boxWidth;
        holder._maxHeight = boxHeight;
        holder._center = new Vector2(boxLeft + boxWidth / 2f, boxTop + boxHeight / 2f);
        holder.Load(item);
    }

    private void Load(Item item)
    {
        Release();
        _icon = null;
        if (_image != null) _image.enabled = false;
        if (item == null) return;
        try
        {
            _icon = ItemViewFactory.LoadItemIcon(item);
            Apply();
            _unsubscribe = _icon?.Changed.Subscribe(Apply);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogDebug($"Arena kit icon for {item.TemplateId} unavailable: {ex.Message}");
        }
    }

    private void Apply()
    {
        if (_image == null || _icon == null) return;
        Sprite sprite = _icon.Sprite;
        _image.enabled = sprite != null;
        if (sprite == null) return;
        _image.sprite = sprite;
        _image.preserveAspect = true;
        float width = Mathf.Max(1f, sprite.rect.width), height = Mathf.Max(1f, sprite.rect.height);
        float scale = Mathf.Min(_maxWidth / width, _maxHeight / height, 1f);
        var rect = _image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(width * scale, height * scale);
        rect.anchoredPosition = new Vector2(_center.x, -_center.y);
    }

    private void Release()
    {
        var unsubscribe = _unsubscribe;
        _unsubscribe = null;
        try { unsubscribe?.Invoke(); }
        catch (Exception) { }
    }

    private void OnDestroy() => Release();
}
