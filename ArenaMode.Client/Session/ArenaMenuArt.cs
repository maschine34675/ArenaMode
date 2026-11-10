using System;
using System.IO;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArenaMode.Session;

internal static class ArenaMenuArt
{
    // Startup and main menu precede a raid. Only the verified private game process installs
    // these presentation hooks or decodes art. Normal EFT uses its own startup and menus.
    private static Harmony _patches;
    private static readonly ImageFile SplashFile = new("splash.jpg");
    private static readonly ImageFile HallFile = new("hall.jpg");
    private static readonly ImageFile BarrierFile = new("barrier.png");
    private static readonly ImageFile FenceFile = new("fence.png");

    private static int _splashUsers;

    // The native splash fades over the environment during boot. Both surfaces borrow the same
    // decoded texture until their individual owners have finished drawing it.
    internal sealed class SplashLease : IDisposable
    {
        internal Texture2D Texture { get; private set; }
        internal SplashLease(Texture2D texture) { Texture = texture; }
        public void Dispose()
        {
            if (ReferenceEquals(Texture, null)) return;
            Texture = null;
            if (--_splashUsers == 0) SplashFile.Release();
        }
    }

    internal static SplashLease AcquireSplash()
    {
        var texture = SplashFile.Get();
        if (texture == null) return null;
        var lease = new SplashLease(texture);
        ++_splashUsers;
        return lease;
    }
    internal static Texture2D Hall => HallFile.Get();
    internal static Texture2D Barrier => BarrierFile.Get();
    internal static Texture2D Fence => FenceFile.Get();

    internal static void Enable()
    {
        if (!ArenaSessionMode.GateActive || _patches != null) return;
        _patches = new Harmony(Plugin.PluginGuid + ".sessionart");
        Try("install startup image", () => ArenaBootSplash.Install(_patches));
        Try("install menu loading image", () => ArenaMenuTransition.Install(_patches));
        Try("install menu background", () => ArenaMenuBackdrop.Install(_patches));
    }

    internal static void Shutdown()
    {
        if (_patches == null) return;
        Try("remove menu art hooks", _patches.UnpatchSelf);
        Try("detach startup image", ArenaBootSplash.Shutdown);
        Try("detach menu loading image", ArenaMenuTransition.Shutdown);
        Try("detach menu background", ArenaMenuBackdrop.Shutdown);
        ReleaseMenu();
        _patches = null;
    }

    internal static Rect CoverUv(Vector2 view, float aspect, bool home = false)
    {
        // Match the parallax panel's overscan during the native home fade.
        float margin = home ? view.y * (ArenaMenuBackdrop.OverscanAt1080 / 1080f) : 0f;
        float fullHeight = Mathf.Max(view.y + 2f * margin, (view.x + 2f * margin) / aspect);
        float width = view.x / (aspect * fullHeight), height = view.y / fullHeight;
        return new Rect((1f - width) * .5f, (1f - height) * .5f, width, height);
    }

    internal static void ReleaseMenu()
    {
        HallFile.Release();
        BarrierFile.Release();
        FenceFile.Release();
    }

    private static void Try(string operation, Action action)
    {
        try { action(); }
        catch (Exception ex) { Plugin.ModLogger?.LogWarning($"Arena session art could not {operation}: {ex.Message}"); }
    }

    private sealed class ImageFile(string name)
    {
        private Texture2D _texture;
        private bool _failed;

        internal Texture2D Get()
        {
            if (_texture != null || _failed || _patches == null || !ArenaSessionMode.GateActive) return _texture;
            Texture2D candidate = null;
            try
            {
                using var input = typeof(ArenaMenuArt).Assembly.GetManifestResourceStream("ArenaMode.SessionMenu." + name)
                    ?? throw new FileNotFoundException(name);
                using var buffer = new MemoryStream((int)input.Length);
                input.CopyTo(buffer);
                candidate = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!candidate.LoadImage(buffer.GetBuffer(), true)) throw new InvalidDataException(name);
                candidate.name = "Arena session " + name;
                candidate.wrapMode = TextureWrapMode.Clamp;
                candidate.filterMode = FilterMode.Bilinear;
                _texture = candidate;
            }
            catch (Exception ex)
            {
                if (candidate != null) Object.Destroy(candidate);
                _failed = true;
                Plugin.ModLogger?.LogWarning($"Arena session image {name} could not load: {ex.Message}");
            }
            return _texture;
        }

        internal void Release()
        {
            if (_texture != null) Object.Destroy(_texture);
            _texture = null;
        }
    }
}
