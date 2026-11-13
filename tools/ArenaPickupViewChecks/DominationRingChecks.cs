using ArenaMode.Hud;
using ArenaMode.Modes;
using EFT.CameraControl;
using UnityEngine;

internal static partial class Program
{
    private static bool SameColor(Color a, Color b) => Math.Abs(a.r-b.r)<.0001f
        && Math.Abs(a.g-b.g)<.0001f && Math.Abs(a.b-b.b)<.0001f && Math.Abs(a.a-b.a)<.0001f;

    // Independent statement of the letter size law: the glyph (a capital is .175 m high at label scale 1)
    // keeps exactly 3 % of the screen height; the visible height at distance d is 2 d tan(fov / 2).
    private static float ExpectedLetterScale(float distance, float fov)
        => .03f * 2f * distance * MathF.Tan(fov * MathF.PI / 180f / 2f) / .175f;

    private static void DominationRings()
    {
        foreach (bool fallback in new[] { false, true })
        {
            Reset();
            if (fallback) Shader.Missing.Add("Sprites/Default");
            var camera = new GameObject("camera").AddComponent<Camera>();
            camera.fieldOfView = 60f;
            CameraManager.Instance = new() { Camera = camera };
            var view = new DominationPointView("A", new Vector3(1, 2, 3), 4f, true);
            var root = GameObject.All.Single(g => g.name == "Arena Domination point A");
            var ring = root.GetComponent<LineRenderer>();
            var label = root.GetComponentsInChildren<TMPro.TextMeshPro>(true).Single();
            var chip = GameObject.All.Single(g => g.name == "Control point chip");
            var chipMaterial = chip.GetComponent<Renderer>().sharedMaterial;
            var fontMaterial = HudStyle.Font.material;
            var labelClone = label.fontSharedMaterial;
            Check(labelClone != fontMaterial && !labelClone.Destroyed && !fontMaterial.Destroyed,
                "objective outline gave the label its own copy of the font material");
            Check(!root.activeSelf, "objective marker waits for camera before display");
            Check(ring.loop && ring.positionCount == 64 && ring.startWidth >= .1f,
                "objective ground ring remains a bounded visible circumference");
            Check(ring.sharedMaterial.Shader.name == (fallback ? "Unlit/Color" : "Sprites/Default"),
                "objective ground ring uses expected shader path");
            Check(chipMaterial != ring.sharedMaterial && chipMaterial.Shader.name == ring.sharedMaterial.Shader.name,
                "objective chip has its own material on the ring's shader path");
            Check(label.outlineWidth >= .2f && label.outlineColor.r == 0f && label.outlineColor.g == 0f
                && label.outlineColor.b == 0f && label.outlineColor.a == 1f,
                "objective letter has a dark outline");
            Check(chip.transform.parent == label.transform && chip.transform.localPosition.z > 0f
                && chip.transform.localScale.x > .15f && chip.transform.localScale.y > .175f,
                "objective chip is a child of the letter, larger than the glyph and behind it");
            var solid = chip.GetComponent<Collider>();
            Check(solid == null || solid.Destroyed, "objective chip has no collider");
            foreach (var entry in new[]
            {
                (new DominationPointState(), new Color(.08f,.08f,.08f,.9f), "neutral"),
                (new DominationPointState{OwnerUsec=true}, new Color(0f,.015f,.25f,.9f), "USEC"),
                (new DominationPointState{OwnerUsec=false}, new Color(.25f,0f,0f,.9f), "BEAR"),
                (new DominationPointState{OwnerUsec=true,Contested=true}, new Color(.25f,.2f,0f,.9f), "contested")
            })
            {
                view.Tick(entry.Item1);
                Check(SameColor(ring.startColor, entry.Item2) && SameColor(ring.endColor, entry.Item2),
                    "objective ring vertex tint follows " + entry.Item3);
                Check(SameColor(ring.sharedMaterial.color, fallback ? entry.Item2 : Color.white),
                    "objective material tint follows the shader contract " + entry.Item3);
                Check(root.activeSelf, "objective ring visible with active camera");
                Check(SameColor(label.color, new Color(.95f, .96f, .97f)),
                    "objective letter is near-white " + entry.Item3);
                Check(SameColor(chipMaterial.color, entry.Item2),
                    "objective chip colour follows the owner " + entry.Item3);
            }
            // The label sits 2.2 m above the floor point: (1, 4.2, 3). Camera on the +X axis at distance d.
            foreach (float distance in new[] { 20f, 50f })
            {
                camera.transform.position = new Vector3(1f + distance, 4.2f, 3f);
                view.Tick(new DominationPointState());
                float expected = ExpectedLetterScale(distance, 60f);
                Check(Math.Abs(label.transform.localScale.x - expected) < .001f
                    && label.transform.localScale.x == label.transform.localScale.y
                    && label.transform.localScale.y == label.transform.localScale.z,
                    "objective letter scale follows the distance law at " + distance + " m");
            }
            camera.fieldOfView = 30f;
            camera.transform.position = new Vector3(51f, 4.2f, 3f);
            view.Tick(new DominationPointState());
            Check(Math.Abs(label.transform.localScale.x - ExpectedLetterScale(50f, 30f)) < .001f,
                "objective letter scale follows the camera field of view");
            camera.fieldOfView = 60f;
            camera.transform.position = new Vector3(3f, 4.2f, 3f);
            view.Tick(new DominationPointState());
            Check(label.transform.localScale.x == 1.5f, "objective letter scale respects the minimum clamp");
            camera.transform.position = new Vector3(201f, 4.2f, 3f);
            view.Tick(new DominationPointState());
            Check(label.transform.localScale.x == 14f, "objective letter scale respects the maximum clamp");
            view.Dispose();
            Check(root.Destroyed && ring.sharedMaterial.Destroyed, "objective ring private resources released");
            Check(label.gameObject.Destroyed && chip.Destroyed && chipMaterial.Destroyed,
                "objective letter, chip and chip material released on dispose");
            Check(labelClone.Destroyed, "objective letter's cloned font material released on dispose");
            Check(!fontMaterial.Destroyed && HudStyle.Font.material == fontMaterial,
                "objective dispose leaves the font asset's own material alone");

            // Ported map: no label object, no chip object, no second material; ring unchanged.
            int materialsBefore = Material.All.Count;
            var ported = new DominationPointView("B", new Vector3(5, 0, 5), 4f, false);
            var portedRoot = GameObject.All.Single(g => g.name == "Arena Domination point B");
            Check(portedRoot.GetComponentsInChildren<TMPro.TextMeshPro>(true).Length == 0
                && portedRoot.GetComponentsInChildren<Transform>(true).Length == 1
                && GameObject.All.Count(g => g.name == "Control point chip") == 1
                && GameObject.All.Count(g => g.name == "Control point label") == 1,
                "objective ported view creates no label and no chip");
            Check(Material.All.Count - materialsBefore == 1, "objective ported view owns only the ring material");
            ported.Tick(new DominationPointState { OwnerUsec = false });
            var portedRing = portedRoot.GetComponent<LineRenderer>();
            Check(portedRoot.activeSelf && SameColor(portedRing.startColor, new Color(.25f, 0f, 0f, .9f)),
                "objective ported view keeps its ring");
            ported.Dispose();
            Check(portedRoot.Destroyed && portedRing.sharedMaterial.Destroyed, "objective ported view releases the ring");
        }
    }
}
