using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArenaMode.Combat.Fire;

/// <summary>Procedural placeholder art/audio; owns no pooled EFT assets.</summary>
internal sealed class ArenaFireView : IDisposable
{
    private GameObject _root;
    private Material _material;
    private Texture2D _texture;
    private AudioClip _clip;
    private AudioSource _audio;
    private ParticleSystem _particles;
    private Light _light;
    private readonly IReadOnlyList<Vector3> _cells;
    private readonly System.Random _random = new();
    private float _next;

    internal ArenaFireView(IReadOnlyList<Vector3> cells)
    {
        _cells = cells;
        try
        {
            _root = new GameObject("Arena ground fire"); _root.transform.position = cells[0];
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("Fire sprite shader unavailable");
            _texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            _texture.name = "Arena procedural flame"; _texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                float dx = (x - 15.5f) / 15.5f, dy = (y - 15.5f) / 15.5f;
                float alpha = Mathf.Clamp01(1f - dx * dx - dy * dy);
                pixels[y * 32 + x] = new Color(1, 1, 1, alpha * alpha);
            }
            _texture.SetPixels(pixels); _texture.Apply(false, true);
            _material = new Material(shader) { mainTexture = _texture };
            _particles = _root.AddComponent<ParticleSystem>(); _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _particles.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 768; main.startLifetime = .65f; main.startSize = .55f; main.startSpeed = 0;
            var emission = _particles.emission; emission.enabled = false;
            var shape = _particles.shape; shape.enabled = false;
            var colors = _particles.colorOverLifetime; colors.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(1f, .9f, .2f), 0),
                new GradientColorKey(new Color(1f, .25f, .02f), .55f), new GradientColorKey(new Color(.2f, .07f, .03f), 1) },
                new[] { new GradientAlphaKey(.95f, 0), new GradientAlphaKey(.8f, .5f), new GradientAlphaKey(0, 1) });
            colors.color = gradient;
            var renderer = _root.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = _material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            _particles.Play();
            var lamp = new GameObject("Fire light"); lamp.transform.SetParent(_root.transform, false); lamp.transform.localPosition = Vector3.up * .5f;
            _light = lamp.AddComponent<Light>(); _light.color = new Color(1f, .35f, .07f); _light.range = 5f; _light.intensity = 1.5f;
            _light.shadows = LightShadows.None;
            // Original deterministic filtered noise/crackle, no borrowed recordings.
            const int rate = 22050; var samples = new float[rate * 2]; var rng = new System.Random(9172); float low = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float noise = (float)rng.NextDouble() * 2f - 1f;
                low += (noise - low) * .08f;
                float envelope = Mathf.Min(1f, Mathf.Min(i, samples.Length - 1 - i) / 220f);
                samples[i] = (low * .65f + (rng.NextDouble() < .001 ? noise * .4f : 0)) * envelope;
            }
            _clip = AudioClip.Create("Arena procedural fire crackle", samples.Length, 1, rate, false); _clip.SetData(samples, 0);
            _audio = _root.AddComponent<AudioSource>(); _audio.clip = _clip; _audio.loop = true; _audio.spatialBlend = 1f;
            _audio.volume = .65f; _audio.minDistance = 1f; _audio.maxDistance = 18f; _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.Play();
            Tick(ArenaFirePolicy.Lifetime);
        }
        catch { Dispose(); throw; }
    }
    internal void Tick(float remaining)
    {
        if (_root == null) return;
        float fade = Mathf.Clamp01(remaining / .5f);
        _light.intensity = (1.25f + .2f * Mathf.Sin(Time.time * 21f)) * fade;
        _audio.volume = .65f * fade;
        if (Time.time < _next) return; _next = Time.time + .08f;
        foreach (var cell in _cells)
        {
            var p = new ParticleSystem.EmitParams { position = cell + Vector3.up * .08f
                + new Vector3((float)_random.NextDouble() * .5f - .25f, 0, (float)_random.NextDouble() * .5f - .25f),
                velocity = Vector3.up * (1f + (float)_random.NextDouble()), startSize = .55f + .3f * (float)_random.NextDouble(),
                startLifetime = .5f + .2f * (float)_random.NextDouble(), startColor = new Color(1, 1, 1, fade) };
            _particles.Emit(p, 1);
        }
    }
    public void Dispose()
    {
        if (_audio != null) _audio.Stop();
        if (_root != null) UnityEngine.Object.Destroy(_root);
        if (_material != null) UnityEngine.Object.Destroy(_material);
        if (_texture != null) UnityEngine.Object.Destroy(_texture);
        if (_clip != null) UnityEngine.Object.Destroy(_clip);
        _root = null; _material = null; _texture = null; _clip = null;
    }
}
