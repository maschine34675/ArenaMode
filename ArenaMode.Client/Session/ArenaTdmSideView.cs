using ArenaMode.Config;
using ArenaMode.Modes;
using Comfort.Common;
using EFT;
using EFT.UI;
using EFT.UI.Matchmaker;
using HarmonyLib;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>Two explicit teams; the implicit Profile preference never becomes a third team.</summary>
internal sealed class ArenaTdmSideView
{
    private static readonly AccessTools.FieldRef<MatchMakerSelectionLocationScreen,
        MatchMakerSelectionLocationScreen.SelectionLocationScreenController> Controller =
        AccessTools.FieldRefAccess<MatchMakerSelectionLocationScreen,
            MatchMakerSelectionLocationScreen.SelectionLocationScreenController>("ScreenController");
    private GameObject _root;
    private readonly Button[] _buttons = new Button[2];
    private readonly TextMeshProUGUI[] _labels = new TextMeshProUGUI[2];
    private Func<EPlayerSide?> _nativeSide;
    private bool _visible;

    internal static EPlayerSide? ReadNativeSide(Transform screen)
    {
        var owner = screen == null ? null : screen.GetComponent<MatchMakerSelectionLocationScreen>();
        return owner == null ? null : Controller(owner)?.Session?.Profile?.Info?.Side;
    }

    internal void Show(Transform parent, Button template, TMP_Text typography, Func<EPlayerSide?> nativeSide)
    {
        Close();
        _nativeSide = nativeSide;
        _root = new GameObject("Arena TDM Team Choice", typeof(RectTransform));
        _root.transform.SetParent(parent, false);
        Rect((RectTransform)_root.transform, 20f, 522f, 320f, 48f);
        var headingObject = new GameObject("Your Team", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        headingObject.transform.SetParent(_root.transform, false);
        var heading = headingObject.GetComponent<TextMeshProUGUI>();
        heading.font = typography?.font ?? TMP_Settings.defaultFontAsset;
        heading.text = "YOUR TEAM";
        heading.fontSize = 13f;
        heading.color = Hud.ArenaUiTheme.Muted;
        heading.raycastTarget = false;
        Rect(heading.rectTransform, 0f, 0f, 320f, 16f);
        var owner = _root;
        for (int index = 0; index < 2; index++)
        {
            int captured = index;
            var button = UnityEngine.Object.Instantiate(template, _root.transform, false);
            _buttons[index] = button;
            button.name = index == 0 ? "Arena Team USEC" : "Arena Team BEAR";
            button.onClick = new Button.ButtonClickedEvent();
            button.transform.localScale = Vector3.one;
            foreach (var animator in button.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            var layout = button.GetComponent<LayoutElement>();
            if (layout != null) layout.ignoreLayout = true;
            Rect((RectTransform)button.transform, index * 162f, 19f, 158f, 25f);
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true)
                ?? throw new InvalidOperationException("The Arena team choice label is missing.");
            _labels[index] = label;
            label.text = index == 0 ? "USEC" : "BEAR";
            label.fontSize = 16f;
            label.enableWordWrapping = false;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            Hud.ArenaUiTheme.StyleButton(button, label);
            Rect(label.rectTransform, 6f, 0f, 146f, 25f);
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            var tooltip = button.GetComponent<HoverTooltipArea>();
            if (tooltip != null) tooltip.SetUnlockStatus(true);
            button.onClick.AddListener(() =>
            {
                if (!ReferenceEquals(owner, _root) || owner == null || !owner.activeInHierarchy
                    || !_visible || ArenaSession.IsActive || ArenaSession.ArmedForNextRaid) return;
                if (Settings.TrySelectTeamDeathmatchSide(captured == 0) && Singleton<GUISounds>.Instantiated)
                    Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.MenuDropdownSelect);
                Refresh();
            });
            button.gameObject.SetActive(true);
        }
        _root.SetActive(false);
    }

    internal void Synchronize(string mode, bool team)
    {
        _visible = !team && Shared.ArenaPopulationPolicy.IsIndependentTeam(mode);
        Refresh();
    }

    private void Refresh()
    {
        if (_root == null) return;
        _root.SetActive(_visible);
        var native = _nativeSide?.Invoke();
        bool readable = (native == EPlayerSide.Usec || native == EPlayerSide.Bear)
            && ArenaTeamSideSelection.TryResolveChoice(Settings.TeamDeathmatchSide?.Value,
                native == EPlayerSide.Usec, out _);
        ArenaTeamSideSelection.TryResolveChoice(Settings.TeamDeathmatchSide?.Value,
            native == EPlayerSide.Usec, out bool selectedUsec);
        for (int index = 0; index < 2; index++)
        {
            var button = _buttons[index];
            if (button == null) continue;
            bool selected = readable && selectedUsec == (index == 0);
            Color faction = index == 0 ? Effects.ArenaTeamPalette.UsecUi : Effects.ArenaTeamPalette.BearUi;
            var colors = button.colors;
            colors.normalColor = Hud.ArenaUiTheme.WithAlpha(faction, selected ? .38f : .09f);
            colors.highlightedColor = Hud.ArenaUiTheme.WithAlpha(faction, .48f);
            colors.selectedColor = colors.normalColor;
            colors.pressedColor = Hud.ArenaUiTheme.WithAlpha(faction, .6f);
            colors.disabledColor = colors.normalColor;
            button.colors = colors;
            _labels[index].color = selected ? Color.white : faction;
            _labels[index].text = (selected ? "● " : "") + (index == 0 ? "USEC" : "BEAR");
            button.interactable = readable && !ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid;
        }
    }

    internal void Close()
    {
        _nativeSide = null;
        _visible = false;
        for (int i = 0; i < _buttons.Length; i++)
        {
            if (_buttons[i] != null) _buttons[i].onClick.RemoveAllListeners();
            _buttons[i] = null;
            _labels[i] = null;
        }
        var root = _root;
        _root = null;
        if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
    }

    private static void Rect(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }
}
