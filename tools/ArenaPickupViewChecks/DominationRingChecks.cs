using ArenaMode.Hud;
using ArenaMode.Modes;
using EFT.CameraControl;
using UnityEngine;

internal static partial class Program
{
    private static bool SameColor(Color a, Color b) => Math.Abs(a.r-b.r)<.0001f
        && Math.Abs(a.g-b.g)<.0001f && Math.Abs(a.b-b.b)<.0001f && Math.Abs(a.a-b.a)<.0001f;

    private static void DominationRings()
    {
        foreach (bool fallback in new[] { false, true })
        {
            Reset();
            if (fallback) Shader.Missing.Add("Sprites/Default");
            CameraManager.Instance = new() { Camera = new GameObject("camera").AddComponent<Camera>() };
            var view = new DominationPointView("A", new Vector3(1, 2, 3), 4f);
            var root = GameObject.All.Single(g => g.name == "Arena Domination point A");
            var ring = root.GetComponent<LineRenderer>();
            Check(!root.activeSelf, "objective marker waits for camera before display");
            Check(ring.loop && ring.positionCount == 64 && ring.startWidth >= .1f,
                "objective ground ring remains a bounded visible circumference");
            Check(ring.sharedMaterial.Shader.name == (fallback ? "Unlit/Color" : "Sprites/Default"),
                "objective ground ring uses expected shader path");
            foreach (var entry in new[]
            {
                (new DominationPointState(), new Color(.08f,.08f,.08f,.9f), "neutral", new Color(.85f,.88f,.9f)),
                (new DominationPointState{OwnerUsec=true}, new Color(0f,.015f,.25f,.9f), "USEC", ArenaMode.Effects.ArenaTeamPalette.UsecUi),
                (new DominationPointState{OwnerUsec=false}, new Color(.25f,0f,0f,.9f), "BEAR", ArenaMode.Effects.ArenaTeamPalette.BearUi),
                (new DominationPointState{OwnerUsec=true,Contested=true}, new Color(.25f,.2f,0f,.9f), "contested", new Color(1f,.8f,.25f))
            })
            {
                view.Tick(entry.Item1);
                Check(SameColor(ring.startColor, entry.Item2) && SameColor(ring.endColor, entry.Item2),
                    "objective ring vertex tint follows " + entry.Item3);
                Check(SameColor(ring.sharedMaterial.color, fallback ? entry.Item2 : Color.white),
                    "objective material tint follows the shader contract " + entry.Item3);
                Check(root.activeSelf, "objective ring visible with active camera");
                Check(SameColor(root.GetComponentsInChildren<TMPro.TextMeshPro>(true).Single().color, entry.Item4),
                    "objective label retains readable UI tint " + entry.Item3);
            }
            view.Dispose();
            Check(root.Destroyed && ring.sharedMaterial.Destroyed, "objective ring private resources released");
        }
    }
}
