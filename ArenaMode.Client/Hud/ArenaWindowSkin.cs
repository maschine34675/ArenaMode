using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Hud;

/// <summary>Private IMGUI resources; never modify GUI.skin or destroy borrowed fonts.</summary>
internal sealed class ArenaWindowSkin : IDisposable
{
    private readonly List<Texture2D> _owned = new List<Texture2D>(4);
    private readonly Texture2D _pane, _button, _hover, _selected;
    private readonly Font _font;

    internal ArenaWindowSkin()
    {
        _font = HudStyle.Font != null ? HudStyle.Font.sourceFontFile : null;
        if (_font == null) _font = GUI.skin.font;
        try
        {
            _pane = Texture(ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, .97f), ArenaUiTheme.Edge);
            _button = Texture(ArenaUiTheme.Raised, ArenaUiTheme.Edge);
            _hover = Texture(ArenaUiTheme.Hover, ArenaUiTheme.WithAlpha(ArenaUiTheme.Accent, .55f));
            _selected = Texture(ArenaUiTheme.WithAlpha(ArenaUiTheme.Accent, .25f), ArenaUiTheme.Accent);
        }
        catch { Dispose(); throw; }
    }

    internal GUIStyle Window() => new GUIStyle(GUI.skin.window)
    {
        font = _font, fontSize = 13, fontStyle = FontStyle.Normal,
        border = new RectOffset(1, 1, 1, 1), padding = new RectOffset(20, 20, 32, 18),
        normal = { background = _pane, textColor = ArenaUiTheme.Muted },
        onNormal = { background = _pane, textColor = ArenaUiTheme.Muted },
        hover = { background = _pane, textColor = ArenaUiTheme.Muted },
        onHover = { background = _pane, textColor = ArenaUiTheme.Muted },
        active = { background = _pane, textColor = ArenaUiTheme.Muted },
        onActive = { background = _pane, textColor = ArenaUiTheme.Muted },
        focused = { background = _pane, textColor = ArenaUiTheme.Muted },
        onFocused = { background = _pane, textColor = ArenaUiTheme.Muted }
    };

    internal GUIStyle Label(int size = 14, Color? color = null, bool bold = false,
        TextAnchor alignment = TextAnchor.MiddleLeft) => new GUIStyle(GUI.skin.label)
    {
        font = _font, fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
        alignment = alignment, wordWrap = true,
        normal = { textColor = color ?? ArenaUiTheme.Text }
    };

    internal GUIStyle Button(bool selected = false) => new GUIStyle(GUI.skin.button)
    {
        font = _font, fontSize = 14, fontStyle = selected ? FontStyle.Bold : FontStyle.Normal,
        border = new RectOffset(1, 1, 1, 1), padding = new RectOffset(14, 14, 5, 5),
        margin = new RectOffset(3, 3, 3, 3),
        normal = { background = selected ? _selected : _button, textColor = ArenaUiTheme.Text },
        hover = { background = _hover, textColor = ArenaUiTheme.Text },
        active = { background = _selected, textColor = ArenaUiTheme.Accent },
        focused = { background = _hover, textColor = ArenaUiTheme.Text },
        onNormal = { background = _selected, textColor = ArenaUiTheme.Text },
        onHover = { background = _hover, textColor = ArenaUiTheme.Text },
        onActive = { background = _selected, textColor = ArenaUiTheme.Accent },
        onFocused = { background = _hover, textColor = ArenaUiTheme.Text }
    };

    internal GUIStyle Toggle() => new GUIStyle(GUI.skin.toggle)
    {
        // Keep borrowed native checkbox graphics and their geometry.
        font = _font, fontSize = 14,
        normal = { textColor = ArenaUiTheme.Text },
        hover = { textColor = ArenaUiTheme.Text },
        active = { textColor = ArenaUiTheme.Text },
        focused = { textColor = ArenaUiTheme.Text },
        onNormal = { textColor = ArenaUiTheme.Accent },
        onHover = { textColor = ArenaUiTheme.Accent },
        onActive = { textColor = ArenaUiTheme.Accent },
        onFocused = { textColor = ArenaUiTheme.Accent }
    };

    internal GUIStyle Notice()
    {
        var style = Label(16, alignment: TextAnchor.MiddleCenter);
        style.normal.background = _pane;
        style.border = new RectOffset(1, 1, 1, 1);
        style.padding = new RectOffset(16, 16, 8, 8);
        style.richText = false;
        return style;
    }

    private Texture2D Texture(Color fill, Color edge)
    {
        var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false)
        { name = "Arena UI surface", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
        _owned.Add(texture);
        var pixels = new Color[64];
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
                pixels[y * 8 + x] = x == 0 || y == 0 || x == 7 || y == 7 ? edge : fill;
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    public void Dispose()
    {
        foreach (var texture in _owned)
            if (texture != null) UnityEngine.Object.Destroy(texture);
        _owned.Clear();
    }
}
