using ArenaMode.Hud;
using EFT.UI.Matchmaker;
using System;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>
/// Consent is reachable on the ordinary character screen even when the whitelist
/// refuses direct Arena play. It is independent of the native choice toggle and
/// of the restart notice; selecting PMC or Scav never implicitly changes consent.
/// </summary>
internal static class ArenaRewardConsentView
{
    private static readonly ConditionalWeakTable<MatchMakerSideSelectionScreen, GameObject> Views = new();

    internal static void Show(MatchMakerSideSelectionScreen screen)
    {
        try { ShowUnsafe(screen); }
        catch (Exception e)
        {
            Hide(screen);
            Plugin.ModLogger.LogWarning("Arena reward consent control could not be displayed: " + e.GetBaseException().Message);
        }
    }

    private static void ShowUnsafe(MatchMakerSideSelectionScreen screen)
    {
        Hide(screen);
        if (screen == null) return;
        var root = new GameObject("Arena Profile Rewards", typeof(RectTransform), typeof(Image));
        Views.Add(screen, root);
        root.transform.SetParent(screen.transform, false);
        var rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(56f, -112f);
        rect.sizeDelta = new Vector2(440f, 112f);
        rect.localScale = Vector3.one * ArenaMapPanelLayout.Scale(screen.transform);
        root.GetComponent<Image>().color = ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, .96f);
        root.GetComponent<Image>().raycastTarget = false;
        ArenaUiTheme.Decorate(rect);

        var buttonObject = new GameObject("Reward consent", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(root.transform, false);
        Place((RectTransform)buttonObject.transform, 12, 10, 416, 28);
        var button = buttonObject.GetComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();
        var label = Text(buttonObject.transform, screen._sideDescriptionText, "Reward consent label", 15, 8, 0, 400, 28);
        ArenaUiTheme.StyleButton(button, label);
        button.onClick.AddListener(ArenaRewardMenuState.Toggle);
        var detail = Text(root.transform, screen._sideDescriptionText, "Reward consent status", 12, 14, 44, 412, 62);
        detail.alignment = TextAlignmentOptions.TopLeft;
        detail.enableWordWrapping = true;
        detail.richText = false;
        var updater = root.AddComponent<ArenaRewardConsentUpdater>();
        updater.Button = button; updater.Label = label; updater.Detail = detail;
        ArenaRewardMenuState.Refresh(force: true);
        updater.Refresh();
    }

    internal static void Hide(MatchMakerSideSelectionScreen screen)
    {
        if (screen == null || !Views.TryGetValue(screen, out var root)) return;
        Views.Remove(screen);
        root.SetActive(false);
        UnityEngine.Object.Destroy(root);
    }

    private static TextMeshProUGUI Text(Transform parent, TMP_Text source, string name,
        float size, float left, float top, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = source?.font ?? TMP_Settings.defaultFontAsset;
        if (source != null) text.fontSharedMaterial = source.fontSharedMaterial;
        text.fontSize = size; text.color = ArenaUiTheme.Text;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        Place(text.rectTransform, left, top, width, height);
        return text;
    }

    private static void Place(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }
}

internal sealed class ArenaRewardConsentUpdater : MonoBehaviour
{
    internal Button Button;
    internal TMP_Text Label, Detail;
    private void Update() => Refresh();
    internal void Refresh()
    {
        ArenaRewardMenuState.Refresh();
        Label.text = ArenaRewardMenuState.Label;
        Detail.text = ArenaRewardMenuState.Detail;
        Label.color = ArenaRewardMenuState.Status.Enabled ? ArenaUiTheme.Positive : ArenaUiTheme.Muted;
        Button.interactable = !ArenaRewardMenuState.Busy && ArenaRewardMenuState.Status.Available
            && ArenaRewardMenuState.Status.CanChange && !ArenaSession.ArmedForNextRaid && !ArenaSession.IsActive;
    }
}
