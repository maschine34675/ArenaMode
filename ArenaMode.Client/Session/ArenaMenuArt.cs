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

    internal static Texture2D Splash => SplashFile.Get();
    internal static Texture2D Hall => HallFile.Get();
    internal static Texture2D Barrier => BarrierFile.Get();
    internal static Texture2D Fence => FenceFile.Get();

    internal static void Enable()
    {
        if (!ArenaSessionMode.GateActive || _patches != null) return;
        _patches = new Harmony(Plugin.PluginGuid + ".sessionart");
        Try("install startup image", () => ArenaBootSplash.Install(_patches));
        Try("install menu background", () => ArenaMenuBackdrop.Install(_patches));
    }

    internal static void Shutdown()
    {
        if (_patches == null) return;
        Try("remove menu art hooks", _patches.UnpatchSelf);
        Try("detach startup image", ArenaBootSplash.Shutdown);
        Try("detach menu background", ArenaMenuBackdrop.Shutdown);
        ReleaseSplash();
        ReleaseMenu();
        _patches = null;
    }

    internal static void ReleaseSplash() => SplashFile.Release();

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
