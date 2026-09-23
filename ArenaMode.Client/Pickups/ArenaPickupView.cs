using ArenaMode.Hud;
using EFT.CameraControl;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArenaMode.Pickups;

/// <summary>Owns only marker geometry; pooled weapon models belong to their native handle.</summary>
internal sealed class ArenaPickupView : IDisposable
{
    private readonly GameObject _root, _symbol, _beam;
    private readonly TextMeshPro _label;
    private readonly Material _material, _beamMaterial;
    private readonly Vector3 _floor;
    private readonly float _yaw;
    private readonly string _name;
    private float _nextText;

    internal ArenaPickupView(ArenaPickupPoint point, Vector3 floor)
    {
        _floor = floor; _yaw = point.Yaw;
        _name = point.Kind switch { ArenaPickupKind.DoubleDamage => "DOUBLE DAMAGE", ArenaPickupKind.Shield => "SHIELD", ArenaPickupKind.JumpBoots => "JUMP BOOTS", ArenaPickupKind.InfiniteAmmo => "INFINITE AMMO", ArenaPickupKind.Health => "HEALTH +75", _ => "WEAPON" };
        Color color = point.Kind switch
        {
            ArenaPickupKind.DoubleDamage => new Color(1f, .25f, .7f),
            ArenaPickupKind.Shield => new Color(.25f, .8f, 1f),
            ArenaPickupKind.JumpBoots => new Color(.4f, 1f, .3f),
            ArenaPickupKind.InfiniteAmmo => new Color(1f, .35f, .06f),
            ArenaPickupKind.Health => new Color(.95f, .95f, 1f),
            _ => new Color(1f, .75f, .2f)
        };
        _root = new GameObject("Arena pickup marker " + point.Id);
        try
        {
            _root.transform.position = floor;
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("No marker shader available.");
            _material = new Material(shader) { color = color };
            var ringGo = new GameObject("Pickup ring");
            ringGo.transform.SetParent(_root.transform, false);
            var ring = ringGo.AddComponent<LineRenderer>();
            ring.useWorldSpace = false; ring.loop = true; ring.positionCount = 32;
            ring.startWidth = ring.endWidth = .035f; ring.sharedMaterial = _material;
            ring.shadowCastingMode = ShadowCastingMode.Off; ring.receiveShadows = false;
            for (int i = 0; i < 32; i++)
            {
                float angle = i * Mathf.PI * 2f / 32f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * .42f, .06f, Mathf.Sin(angle) * .42f));
            }
            // Sprites/Default supports vertex alpha and ordinary scene depth. Do
            // not use an overlay shader: walls must still occlude the beacon.
            var beamShader = Shader.Find("Sprites/Default");
            float beamLength = ArenaPickupGeometry.BeamLength(floor);
            if (beamShader != null && beamLength > .1f)
            {
                _beamMaterial = new Material(beamShader) { color = Color.white };
                _beam = new GameObject("Pickup beacon");
                _beam.transform.SetParent(_root.transform, false);
                _beam.SetActive(false);
                AddBeam("Glow", .3f, .28f);
                AddBeam("Core", .055f, .9f);

                void AddBeam(string name, float width, float opacity)
                {
                    var go = new GameObject(name);
                    go.transform.SetParent(_beam.transform, false);
                    var line = go.AddComponent<LineRenderer>();
                    line.useWorldSpace = false; line.positionCount = 2;
                    line.SetPosition(0, Vector3.up * .15f);
                    line.SetPosition(1, Vector3.up * (.15f + beamLength));
                    line.startWidth = width; line.endWidth = width * .4f;
                    line.startColor = new Color(color.r, color.g, color.b, opacity);
                    line.endColor = new Color(color.r, color.g, color.b, 0f);
                    line.sharedMaterial = _beamMaterial;
                    line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
                }
            }
            else if (beamShader == null)
                Plugin.ModLogger.LogWarning("Arena pickup beacon unavailable: Sprites/Default shader missing; ordinary marker retained.");
            if (point.Kind != ArenaPickupKind.Weapon)
            {
                _symbol = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _symbol.name = "Pickup symbol";
                var collider = _symbol.GetComponent<Collider>();
                if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
                _symbol.transform.SetParent(_root.transform, false);
                _symbol.transform.localScale = point.Kind == ArenaPickupKind.Shield ? new Vector3(.32f, .42f, .12f) : Vector3.one * .3f;
                var renderer = _symbol.GetComponent<Renderer>();
                renderer.sharedMaterial = _material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                if (point.Kind == ArenaPickupKind.Health)
                {
                    _symbol.transform.localScale = Vector3.one;
                    renderer.enabled = false;
                    foreach (var size in new[] { new Vector3(.36f, .12f, .1f), new Vector3(.12f, .36f, .1f) })
                    {
                        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        bar.transform.SetParent(_symbol.transform, false);
                        bar.transform.localScale = size;
                        var solid = bar.GetComponent<Collider>();
                        if (solid != null) { solid.enabled = false; UnityEngine.Object.Destroy(solid); }
                        var surface = bar.GetComponent<Renderer>();
                        surface.sharedMaterial = _material;
                        surface.shadowCastingMode = ShadowCastingMode.Off; surface.receiveShadows = false;
                    }
                }
            }
            var labelGo = new GameObject("Pickup label");
            labelGo.transform.SetParent(_root.transform, false);
            labelGo.transform.localPosition = Vector3.up * 1.2f;
            _label = labelGo.AddComponent<TextMeshPro>();
            _label.font = HudStyle.Font; _label.fontSize = 2.4f; _label.alignment = TextAlignmentOptions.Center;
            _label.color = color; _label.richText = false; _label.enableWordWrapping = false;
            _label.rectTransform.sizeDelta = new Vector2(4f, .7f);
            _label.text = _name;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void Tick(ArenaPickupState state, bool preparing, GameObject weapon)
    {
        if (_root == null) return;
        bool available = state.Phase == ArenaPickupPhase.Available && !preparing;
        var camera = CameraManager.Exist ? CameraManager.Instance.Camera : null;
        bool visible = state.Phase != ArenaPickupPhase.Retired && camera != null
            && (camera.transform.position - _floor).sqrMagnitude < 6400f;
        _root.SetActive(visible);
        if (_beam != null) _beam.SetActive(available);
        if (_symbol != null)
        {
            _symbol.SetActive(available);
            _symbol.transform.localPosition = Vector3.up * (.65f + Mathf.Sin(Time.time * 2f) * .08f);
            _symbol.transform.localRotation = Quaternion.Euler(0, Time.time * 35f, 35f);
        }
        // Never parent this object or destroy it here: native inventory owns its pool lifecycle.
        if (weapon != null && available)
        {
            weapon.transform.position = _floor + Vector3.up * (.65f + Mathf.Sin(Time.time * 2f) * .08f);
            weapon.transform.rotation = Quaternion.Euler(0, _yaw + Time.time * 25f, 0);
        }
        if (camera != null) _label.transform.rotation = camera.transform.rotation;
        if (Time.unscaledTime < _nextText) return;
        _nextText = Time.unscaledTime + .2f;
        _label.text = preparing ? _name + "\nLOADING" : state.Phase switch
        {
            ArenaPickupPhase.Cooldown => _name + "\n" + Mathf.CeilToInt(Mathf.Max(0, state.ReadyAt - Time.time)) + "s",
            ArenaPickupPhase.Pending => _name + "\nCOLLECTING",
            _ => _name
        };
    }

    public void Dispose()
    {
        if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); }
        if (_material != null) UnityEngine.Object.Destroy(_material);
        if (_beamMaterial != null) UnityEngine.Object.Destroy(_beamMaterial);
    }
}
