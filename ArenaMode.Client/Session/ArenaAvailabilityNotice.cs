using System;
using System.Runtime.CompilerServices;
using EFT.UI;
using EFT.UI.Matchmaker;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>
/// An optional button under a notice. It needs two clicks, because what it triggers (a full game
/// restart) must not fire on a stray click: the first click changes the label to the confirmation
/// text, a second click within <see cref="ArenaTwoClickGate.WindowSeconds"/> runs the callback, and
/// the label falls back when the window passes unused.
/// </summary>
internal sealed class ArenaNoticeAction
{
    internal const string DefaultConfirmLabel = "CLICK AGAIN TO RESTART THE GAME";

    internal ArenaNoticeAction(string label, Action callback, string confirmLabel = DefaultConfirmLabel,
        ArenaNoticeOption option = null)
    {
        Label = label;
        Callback = callback;
        ConfirmLabel = confirmLabel;
        Option = option;
    }

    internal string Label { get; }
    internal Action Callback { get; }
    internal string ConfirmLabel { get; }
    internal ArenaNoticeOption Option { get; }
}

/// <summary>Optional screen-owned preference; delegates keep the Core-free notice independent.</summary>
internal sealed class ArenaNoticeOption
{
    internal Func<string> Label, Detail;
    internal Func<bool> CanClick, CanProceed;
    internal Action Refresh, Click, Close;
}

/// <summary>
/// Passive, screen-owned diagnostics. Keep this independent of Core, settings,
/// previews and compatibility adapters: it also runs when Core cannot load.
/// </summary>
internal static class ArenaAvailabilityNotice
{
    private static readonly ConditionalWeakTable<MatchMakerSideSelectionScreen, View> Views = new();

    /// <summary>The "ARENA UNAVAILABLE" notice, with an optional two-click action under the text.</summary>
    internal static void Show(MatchMakerSideSelectionScreen screen, string reason, ArenaNoticeAction action = null)
        => ShowCore(screen, reason, true, action);

    /// <summary>A neutral information notice (no unavailability heading), with an optional action.</summary>
    internal static void ShowInfo(MatchMakerSideSelectionScreen screen, string text, ArenaNoticeAction action = null)
        => ShowCore(screen, text, false, action);

    private static void ShowCore(MatchMakerSideSelectionScreen screen, string text, bool unavailable, ArenaNoticeAction action)
    {
        try { ShowUnsafe(screen, text, unavailable, action); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning("Arena availability notice could not be displayed: " + ex.Message); }
    }

    private static void ShowUnsafe(MatchMakerSideSelectionScreen screen, string text, bool unavailable, ArenaNoticeAction action)
    {
        if (screen == null) return;
        if (string.IsNullOrWhiteSpace(text))
        {
            Hide(screen);
            return;
        }
        if (!Views.TryGetValue(screen, out var view))
        {
            view = new View(screen);
            Views.Add(screen, view);
        }
        view.Set(screen, text, unavailable, action);
    }

    internal static void Hide(MatchMakerSideSelectionScreen screen)
    {
        if (screen == null || !Views.TryGetValue(screen, out var view)) return;
        Views.Remove(screen);
        view.Close();
        view.Root.SetActive(false);
        UnityEngine.Object.Destroy(view.Root);
    }

    /// <summary>Hides an information notice but leaves an "unavailable" notice in place.</summary>
    internal static void HideInfo(MatchMakerSideSelectionScreen screen)
    {
        if (screen != null && Views.TryGetValue(screen, out var view) && !view.Unavailable)
            Hide(screen);
    }

    private sealed class View
    {
        private const float Width = 440f;
        private const float ButtonHeight = 30f;
        private const float ButtonBlock = ButtonHeight + 14f; // button plus the gap above it
        private const float OptionBlock = 36f;

        private static readonly Color UnavailableText = new Color(.95f, .77f, .46f, 1f);
        private static readonly Color InfoText = new Color(.78f, .88f, .95f, 1f);
        private static readonly Color ButtonIdle = new Color(.20f, .25f, .30f, 1f);
        private static readonly Color ButtonArmed = new Color(.72f, .30f, .22f, 1f);

        internal readonly GameObject Root;
        internal bool Unavailable { get; private set; }

        private readonly TextMeshProUGUI _text;
        private readonly GameObject _button;
        private readonly Image _buttonImage;
        private readonly TextMeshProUGUI _buttonLabel;
        private readonly Button _optionButton;
        private readonly TextMeshProUGUI _optionLabel;
        private readonly HoverTooltipArea _optionTooltip;
        private readonly ArenaTwoClickGate _gate = new ArenaTwoClickGate();
        private ArenaNoticeAction _action;
        private ArenaNoticeOption _option;
        private bool _shownArmed;

        internal View(MatchMakerSideSelectionScreen screen)
        {
            Root = new GameObject("ArenaUnavailableNotice", typeof(RectTransform), typeof(Image));
            Root.SetActive(false);
            var rect = (RectTransform)Root.transform;
            rect.SetParent(screen.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, 0f);
            var anchor = Root.AddComponent<ArenaAvailabilityNoticeAnchor>();
            anchor.Source = screen._randomButton.transform as RectTransform;
            anchor.Tick = Tick;
            var background = Root.GetComponent<Image>();
            background.color = new Color(.06f, .07f, .08f, .94f);
            background.raycastTarget = false;

            var label = new GameObject("Reason", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(rect, false);
            _text = label.GetComponent<TextMeshProUGUI>();
            _text.rectTransform.anchorMin = Vector2.zero;
            _text.rectTransform.anchorMax = Vector2.one;
            _text.rectTransform.offsetMin = new Vector2(14f, 10f);
            _text.rectTransform.offsetMax = new Vector2(-14f, -10f);
            _text.font = screen._sideDescriptionText.font;
            _text.fontSharedMaterial = screen._sideDescriptionText.fontSharedMaterial;
            _text.fontSize = 16f;
            _text.fontStyle = FontStyles.Normal;
            _text.color = UnavailableText;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.enableWordWrapping = true;
            _text.richText = false; // A foreign error must remain literal text.
            _text.raycastTarget = false;

            // The optional action button: bottom centre, only active while an action is set.
            _button = new GameObject("ArenaNoticeAction", typeof(RectTransform), typeof(Image), typeof(Button));
            _button.SetActive(false);
            var buttonRect = (RectTransform)_button.transform;
            buttonRect.SetParent(rect, false);
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(.5f, 0f);
            buttonRect.pivot = new Vector2(.5f, 0f);
            buttonRect.sizeDelta = new Vector2(330f, ButtonHeight);
            buttonRect.anchoredPosition = new Vector2(0f, 12f);
            _buttonImage = _button.GetComponent<Image>();
            _buttonImage.color = ButtonIdle;
            var button = _button.GetComponent<Button>();
            button.targetGraphic = _buttonImage;
            var colors = button.colors;
            colors.normalColor = new Color(.85f, .85f, .85f, 1f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(.7f, .7f, .7f, 1f);
            button.colors = colors;
            button.onClick.AddListener(OnClick);

            var buttonText = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            buttonText.transform.SetParent(buttonRect, false);
            _buttonLabel = buttonText.GetComponent<TextMeshProUGUI>();
            _buttonLabel.rectTransform.anchorMin = Vector2.zero;
            _buttonLabel.rectTransform.anchorMax = Vector2.one;
            _buttonLabel.rectTransform.offsetMin = new Vector2(8f, 2f);
            _buttonLabel.rectTransform.offsetMax = new Vector2(-8f, -2f);
            _buttonLabel.font = screen._sideDescriptionText.font;
            _buttonLabel.fontSharedMaterial = screen._sideDescriptionText.fontSharedMaterial;
            _buttonLabel.fontSize = 14f;
            _buttonLabel.fontStyle = FontStyles.Bold;
            _buttonLabel.color = Color.white;
            _buttonLabel.alignment = TextAlignmentOptions.Center;
            _buttonLabel.enableWordWrapping = false;
            _buttonLabel.richText = false; // The label is data too.
            _buttonLabel.raycastTarget = false;

            var option = new GameObject("ArenaNoticePreference", typeof(RectTransform), typeof(Image), typeof(Button));
            option.SetActive(false);
            var optionRect = (RectTransform)option.transform;
            optionRect.SetParent(rect, false);
            optionRect.anchorMin = optionRect.anchorMax = new Vector2(.5f, 0f);
            optionRect.pivot = new Vector2(.5f, 0f);
            optionRect.sizeDelta = new Vector2(330f, 26f);
            optionRect.anchoredPosition = new Vector2(0f, 12f + ButtonBlock);
            option.GetComponent<Image>().color = ButtonIdle;
            _optionButton = option.GetComponent<Button>();
            _optionButton.targetGraphic = option.GetComponent<Image>();
            _optionButton.colors = colors;
            _optionButton.onClick.AddListener(OnOptionClick);
            _optionTooltip = option.AddComponent<HoverTooltipArea>();
            var optionText = new GameObject("Preference label", typeof(RectTransform), typeof(TextMeshProUGUI));
            optionText.transform.SetParent(optionRect, false);
            _optionLabel = optionText.GetComponent<TextMeshProUGUI>();
            _optionLabel.rectTransform.anchorMin = Vector2.zero;
            _optionLabel.rectTransform.anchorMax = Vector2.one;
            _optionLabel.rectTransform.offsetMin = new Vector2(8f, 2f);
            _optionLabel.rectTransform.offsetMax = new Vector2(-8f, -2f);
            _optionLabel.font = screen._sideDescriptionText.font;
            _optionLabel.fontSharedMaterial = screen._sideDescriptionText.fontSharedMaterial;
            _optionLabel.fontSize = 14f;
            _optionLabel.color = Color.white;
            _optionLabel.alignment = TextAlignmentOptions.Center;
            _optionLabel.enableWordWrapping = false;
            _optionLabel.richText = false;
            _optionLabel.raycastTarget = false;
        }

        internal void Set(MatchMakerSideSelectionScreen screen, string text, bool unavailable, ArenaNoticeAction action)
        {
            Unavailable = unavailable;
            if (!ReferenceEquals(_option, action?.Option)) _option?.Close?.Invoke();
            _action = action;
            _option = action?.Option;
            _gate.Reset();
            _text.color = unavailable ? UnavailableText : InfoText;
            _text.text = unavailable ? "ARENA UNAVAILABLE\n" + text : text;
            float buttonBlock = action == null ? 0f : ButtonBlock + (_option == null ? 0f : OptionBlock);
            _text.rectTransform.offsetMin = new Vector2(14f, 10f + buttonBlock);
            var rect = (RectTransform)Root.transform;
            rect.sizeDelta = new Vector2(Width, _text.GetPreferredValues(_text.text, Width - 28f, 0f).y + 24f + buttonBlock);
            _button.SetActive(action != null);
            UpdateButton(Time.realtimeSinceStartup);
            Root.GetComponent<ArenaAvailabilityNoticeAnchor>().Place();
            Root.transform.SetAsLastSibling();
            Root.SetActive(true);
            RefreshOption();
        }

        internal void Close()
        {
            var option = _option;
            _option = null;
            _action = null;
            Root.GetComponent<ArenaAvailabilityNoticeAnchor>().Tick = null;
            _optionButton.onClick.RemoveAllListeners();
            option?.Close?.Invoke();
        }

        private void OnOptionClick()
        {
            var option = _option;
            if (Root == null || !Root.activeInHierarchy || option == null || !option.CanClick()) return;
            option.Click();
            _gate.Reset();
            UpdateButton(Time.realtimeSinceStartup);
            RefreshOption();
        }

        private void RefreshOption()
        {
            _optionButton.gameObject.SetActive(_option != null);
            if (_option != null)
            {
                _option.Refresh();
                _optionLabel.text = _option.Label();
                _optionTooltip.SetMessageText(_option.Detail(), rawText: true);
                _optionButton.interactable = _option.CanClick();
            }
            _button.GetComponent<Button>().interactable = _option?.CanProceed() ?? true;
        }

        private void OnClick()
        {
            var action = _action;
            if (Root == null || !Root.activeInHierarchy || action == null || !(_option?.CanProceed() ?? true)) return;
            float now = Time.realtimeSinceStartup;
            bool confirmed = _gate.Click(now);
            UpdateButton(now);
            if (!confirmed) return;
            try { action.Callback?.Invoke(); }
            catch (Exception ex) { Plugin.ModLogger.LogWarning("Arena notice action failed: " + ex.Message); }
        }

        /// <summary>Called every frame by the anchor: puts the label back when the window ran out.</summary>
        private void Tick()
        {
            if (Root == null || !Root.activeInHierarchy) return;
            RefreshOption();
            if (_action != null && _shownArmed && !_gate.IsArmed(Time.realtimeSinceStartup))
                UpdateButton(Time.realtimeSinceStartup);
        }

        private void UpdateButton(float now)
        {
            bool armed = _action != null && _gate.IsArmed(now);
            _shownArmed = armed;
            if (_action == null) return;
            _buttonLabel.text = armed ? _action.ConfirmLabel : _action.Label;
            _buttonImage.color = armed ? ButtonArmed : ButtonIdle;
        }
    }
}

/// <summary>Native toggle sizing finishes after Show; follow its stable center.</summary>
internal sealed class ArenaAvailabilityNoticeAnchor : MonoBehaviour
{
    internal RectTransform Source;
    internal Action Tick;
    private void LateUpdate()
    {
        Place();
        Tick?.Invoke();
    }
    internal void Place()
    {
        if (Source == null || transform.parent == null) return;
        var center = transform.parent.InverseTransformPoint(Source.TransformPoint(Source.rect.center));
        // Above the choice, away from the shared PMC/Scav description and Next.
        transform.localPosition = new Vector3(center.x, center.y + 42f, 0f);
    }
}
