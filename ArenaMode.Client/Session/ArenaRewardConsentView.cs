using ArenaMode.Hud;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>Profile-level consent and payout status, owned by the Arena map screen.</summary>
internal sealed class ArenaRewardConsentView
{
    private GameObject _root, _owner;
    private Transform _screen;
    private Button _button;
    private TMP_Text _label, _detail;

    internal void Create(Transform screen, GameObject owner, TMP_Text typography)
    {
        Close();
        _screen = screen;
        _owner = owner;
        try
        {
            _root = new GameObject("Arena Profile Rewards", typeof(RectTransform), typeof(Image));
            _root.SetActive(false);
            _root.transform.SetParent(screen, false);
            _root.GetComponent<Image>().color = ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, .96f);
            _root.GetComponent<Image>().raycastTarget = false;
            ArenaUiTheme.Decorate((RectTransform)_root.transform);

            var buttonObject = new GameObject("Reward consent", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(_root.transform, false);
            _button = buttonObject.GetComponent<Button>();
            _button.targetGraphic = buttonObject.GetComponent<Image>();
            _label = Text(buttonObject.transform, typography, "Reward consent label", 15);
            ArenaUiTheme.StyleButton(_button, _label);
            // StyleButton creates its own tinted surface. The original white
            // Image must not shine through that surface's disabled alpha.
            buttonObject.GetComponent<Image>().color = Color.clear;
            var navigation = _button.navigation;
            navigation.mode = Navigation.Mode.None;
            _button.navigation = navigation;
            var generation = _root;
            _button.onClick.AddListener(() =>
            {
                if (!Owns(generation) || IsObjectivePrototype || !ArenaRewardMenuState.CanToggle) return;
                ArenaRewardMenuState.Toggle();
                Refresh();
            });
            _detail = Text(_root.transform, typography, "Reward consent status", 12);
            _detail.alignment = TextAlignmentOptions.TopLeft;
            _detail.enableWordWrapping = true;
            _detail.richText = false;
            ArenaRewardMenuState.Refresh(force: true);
        }
        catch (Exception e)
        {
            Close();
            Plugin.ModLogger.LogWarning("Arena reward consent control could not be displayed: " + e.GetBaseException().Message);
        }
    }

    internal void Refresh()
    {
        if (_root == null) return;
        _root.SetActive(_owner != null && _owner.activeInHierarchy);
        if (!_root.activeInHierarchy) return;
        // Use the free header space; map preview, mutators and native navigation stay put.
        ArenaMapPanelLayout.Place((RectTransform)_root.transform, _screen, 40f, 112f);
        float width = ArenaMapPanelLayout.Width(_screen);
        Place((RectTransform)_button.transform, 12f, 10f, width - 24f, 28f);
        Place(_label.rectTransform, 8f, 0f, width - 40f, 28f);
        Place(_detail.rectTransform, 14f, 44f, width - 28f, 62f);
        if (IsObjectivePrototype)
        {
            _label.text = "DOMINATION PROTOTYPE — NO REWARDS";
            _detail.text = "Objective rewards are not available yet. Your ordinary PMC profile is restored after Arena.";
            _label.color = ArenaUiTheme.Muted;
            _button.interactable = false;
            return;
        }
        ArenaRewardMenuState.Refresh();
        _label.text = ArenaRewardMenuState.Label;
        _detail.text = ArenaRewardMenuState.Detail;
        _label.color = ArenaRewardMenuState.Status.Enabled ? ArenaUiTheme.Positive : ArenaUiTheme.Muted;
        _button.interactable = ArenaRewardMenuState.CanToggle;
    }

    private static bool IsObjectivePrototype => Config.Settings.GameMode.Value == Modes.ArenaModeCatalog.DominationId;

    private bool Owns(GameObject generation) => ReferenceEquals(generation, _root)
        && _root != null && _root.activeInHierarchy && _owner != null && _owner.activeInHierarchy;

    internal void Close()
    {
        if (_root != null) ArenaRewardMenuState.CancelQueuedToggle();
        if (_button != null) _button.onClick.RemoveAllListeners();
        _button = null;
        _label = _detail = null;
        _owner = null;
        _screen = null;
        var root = _root;
        _root = null;
        if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
    }

    private static TextMeshProUGUI Text(Transform parent, TMP_Text source, string name, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = source?.font ?? TMP_Settings.defaultFontAsset;
        if (source != null) text.fontSharedMaterial = source.fontSharedMaterial;
        text.fontSize = size;
        text.color = ArenaUiTheme.Text;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private static void Place(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }
}
