using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArenaMode.Hud;

internal enum ArenaUiIcon
{
    Kill, Death, Assist, Headshot, AssaultRifle, Carbine, Sniper, Marksman,
    Machinegun, Shotgun, Pistol, Smg, Grenade, Melee, Bleeding, Fire, TeamPanel, BarbedWire,
}

/// <summary>Private, bounded, cached textures. Original EFT Arena files are documented separately.</summary>
internal sealed class ArenaUiAssets : IDisposable
{
    internal readonly struct Spec
    {
        internal readonly string File;
        internal readonly int Width, Height;
        internal Spec(string file, int width, int height) { File = file; Width = width; Height = height; }
    }
    internal static Spec Describe(ArenaUiIcon icon) => icon switch
    {
        ArenaUiIcon.Kill => new Spec("kill.png", 20, 20),
        ArenaUiIcon.Death => new Spec("death.png", 20, 20),
        ArenaUiIcon.Assist => new Spec("assist.png", 20, 20),
        ArenaUiIcon.Headshot => new Spec("headshot.png", 20, 20),
        ArenaUiIcon.AssaultRifle => new Spec("assault-rifle.png", 42, 14),
        ArenaUiIcon.Carbine => new Spec("carbine.png", 40, 18),
        ArenaUiIcon.Sniper => new Spec("sniper.png", 43, 11),
        ArenaUiIcon.Marksman => new Spec("marksman.png", 40, 10),
        ArenaUiIcon.Machinegun => new Spec("machinegun.png", 37, 16),
        ArenaUiIcon.Shotgun => new Spec("shotgun.png", 39, 16),
        ArenaUiIcon.Pistol => new Spec("pistol.png", 22, 15),
        ArenaUiIcon.Smg => new Spec("smg.png", 25, 18),
        ArenaUiIcon.Grenade => new Spec("grenade.png", 21, 20),
        ArenaUiIcon.Melee => new Spec("melee.png", 23, 20),
        ArenaUiIcon.Bleeding => new Spec("bleeding.png", 20, 20),
        ArenaUiIcon.BarbedWire => new Spec("barbed-wire.png", 21, 18),
        ArenaUiIcon.Fire => new Spec("fire.png", 13, 18),
        ArenaUiIcon.TeamPanel => new Spec("team-panel.png", 688, 107),
        _ => throw new ArgumentOutOfRangeException(nameof(icon)),
    };

    private readonly Dictionary<ArenaUiIcon, Texture2D> _textures = new();
    private readonly Dictionary<ArenaUiIcon, Sprite> _sprites = new();
    private readonly Func<string, Stream> _open;
    private bool _disposed;
    internal ArenaUiAssets(Func<string, Stream> open = null)
        => _open = open ?? (file => typeof(ArenaUiAssets).Assembly.GetManifestResourceStream("ArenaMode.Ui." + file));

    internal Texture2D Texture(ArenaUiIcon icon)
    {
        if (_disposed) return null;
        if (_textures.TryGetValue(icon, out var cached)) return cached;
        Texture2D texture = null;
        try
        {
            var spec = Describe(icon);
            using var stream = _open(spec.File);
            if (stream == null || stream.Length < 33 || stream.Length > 256 * 1024)
                throw new InvalidDataException("missing or oversized PNG");
            var bytes = new byte[(int)stream.Length];
            int read = 0;
            while (read < bytes.Length)
            {
                int count = stream.Read(bytes, read, bytes.Length - read);
                if (count <= 0) throw new EndOfStreamException();
                read += count;
            }
            if (!MatchesHeader(bytes, spec)) throw new InvalidDataException("unexpected PNG dimensions");
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes, false) || texture.width != spec.Width || texture.height != spec.Height)
                throw new InvalidDataException("PNG decode failed");
            if (icon == ArenaUiIcon.TeamPanel)
            {
                // Original bytes stay unchanged. Neutralize the borrowed blue
                // panel in our private texture so configured team hues stay true.
                var pixels = texture.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte value = (byte)Math.Max(pixels[i].r, Math.Max(pixels[i].g, pixels[i].b));
                    pixels[i] = new Color32(value, value, value, pixels[i].a);
                }
                texture.SetPixels32(pixels);
            }
            texture.name = "Arena UI " + spec.File;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Apply(false, true);
        }
        catch (Exception ex)
        {
            if (texture != null) Object.Destroy(texture);
            texture = null;
            Plugin.ModLogger?.LogWarning("Arena UI asset unavailable (text retained): " + icon + ": " + ex.Message);
        }
        _textures[icon] = texture; // Cache failure too: never retry/spam every frame.
        return texture;
    }

    internal Sprite Sprite(ArenaUiIcon icon)
    {
        if (_disposed) return null;
        if (_sprites.TryGetValue(icon, out var cached)) return cached;
        var texture = Texture(icon);
        Sprite sprite = null;
        try
        {
            if (texture != null) sprite = UnityEngine.Sprite.Create(texture,
                new Rect(0f, 0f, texture.width, texture.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
        }
        catch (Exception ex) { Plugin.ModLogger?.LogWarning("Arena UI sprite unavailable: " + icon + ": " + ex.Message); }
        _sprites[icon] = sprite;
        return sprite;
    }

    internal static bool MatchesHeader(byte[] data, Spec spec)
    {
        if (data == null || data.Length < 33) return false;
        byte[] magic = { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 };
        for (int i = 0; i < magic.Length; i++) if (data[i] != magic[i]) return false;
        int Int(int index) => (data[index] << 24) | (data[index + 1] << 16) | (data[index + 2] << 8) | data[index + 3];
        return Int(16) == spec.Width && Int(20) == spec.Height;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var sprite in _sprites.Values) if (sprite != null) Object.Destroy(sprite);
        foreach (var texture in _textures.Values) if (texture != null) Object.Destroy(texture);
        _sprites.Clear(); _textures.Clear();
    }
}

/// <summary>Killfeed resources die with its own canvas, including partial HUD construction failure.</summary>
internal sealed class ArenaUiAssetOwner : MonoBehaviour
{
    internal readonly ArenaUiAssets Assets = new();
    private void OnDestroy() => Assets.Dispose();
}
