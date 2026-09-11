using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace ArenaMode.Audio;

/// <summary>Small PCM16 WAVs avoid async decoding during a hit. Managed samples
/// survive EFT's device reset; Unity clips are validated and rebuilt on demand.</summary>
internal sealed class HitmarkerPcmClip
{
    private readonly string _name;
    private readonly float[] _samples;
    private readonly int _rate;
    private AudioClip _runtime;

    private HitmarkerPcmClip(string name, float[] samples, int rate)
    { _name = name; _samples = samples; _rate = rate; }

    internal static HitmarkerPcmClip Load(string path)
    {
        // Bounded before allocation, including user replacements. Deliberately
        // support only mono PCM16, <=1 second; never load arbitrary large audio.
        using var stream = File.OpenRead(path);
        if (stream.Length < 44 || stream.Length > 200000)
            throw new InvalidDataException("Expected a short mono PCM16 WAV (maximum 200 KB).");
        using var reader = new BinaryReader(stream, Encoding.ASCII);
        string Tag() => Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (Tag() != "RIFF" || reader.ReadUInt32() != stream.Length - 8 || Tag() != "WAVE")
            throw new InvalidDataException("Invalid RIFF/WAVE header.");
        int rate = 0;
        byte[] pcm = null;
        while (stream.Position < stream.Length)
        {
            if (stream.Length - stream.Position < 8) throw new InvalidDataException("Truncated WAV chunk.");
            string tag = Tag();
            uint size = reader.ReadUInt32();
            long end = stream.Position + size;
            long paddedEnd = end + (size & 1);
            if (paddedEnd > stream.Length) throw new InvalidDataException("WAV chunk exceeds file.");
            if (tag == "fmt ")
            {
                if (rate != 0 || size < 16 || reader.ReadUInt16() != 1 || reader.ReadUInt16() != 1)
                    throw new InvalidDataException("Only one mono PCM format is supported.");
                rate = reader.ReadInt32();
                int byteRate = reader.ReadInt32();
                int align = reader.ReadUInt16(), bits = reader.ReadUInt16();
                if (rate < 8000 || rate > 48000 || byteRate != rate * 2 || align != 2 || bits != 16)
                    throw new InvalidDataException("Expected 8-48 kHz mono PCM16 WAV.");
            }
            else if (tag == "data")
            {
                if (pcm != null || size == 0 || (size & 1) != 0)
                    throw new InvalidDataException("Invalid or duplicate PCM chunk.");
                pcm = reader.ReadBytes(checked((int)size));
            }
            stream.Position = paddedEnd;
        }
        if (rate == 0 || pcm == null || pcm.Length / 2 > rate)
            throw new InvalidDataException("Missing format/data or audio longer than one second.");
        var samples = new float[pcm.Length / 2];
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(pcm[i * 2] | pcm[i * 2 + 1] << 8) / 32768f;
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }
        if (peak <= 0.00001f) throw new InvalidDataException("WAV contains no signal.");
        return new HitmarkerPcmClip(Path.GetFileNameWithoutExtension(path), samples, rate);
    }

    internal AudioClip GetRuntime()
    {
        if (Valid(_runtime)) return _runtime;
        ResetRuntime();
        var clip = AudioClip.Create("Arena hitmarker " + _name, _samples.Length, 1, _rate, false);
        try
        {
            if (clip == null) throw new InvalidOperationException("AudioClip.Create returned null.");
            clip.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            if (!clip.SetData(_samples, 0) || !Valid(clip))
                throw new InvalidOperationException("Runtime PCM upload or metadata invalid.");
            var readback = new float[_samples.Length];
            if (!clip.GetData(readback, 0)) throw new InvalidOperationException("Runtime PCM readback failed.");
            float peak = 0f;
            foreach (float value in readback)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                    throw new InvalidOperationException("Non-finite runtime PCM.");
                peak = Math.Max(peak, Math.Abs(value));
            }
            if (peak <= 0.00001f) throw new InvalidOperationException("Runtime PCM has no signal.");
            _runtime = clip;
            return clip;
        }
        catch
        {
            if (clip != null) UnityEngine.Object.Destroy(clip);
            throw;
        }
    }

    private bool Valid(AudioClip clip)
        => clip != null && clip.loadState == AudioDataLoadState.Loaded
            && clip.samples == _samples.Length && clip.channels == 1 && clip.frequency == _rate
            && Math.Abs(clip.length - _samples.Length / (float)_rate) < 0.001f;

    internal void ResetRuntime()
    {
        var old = _runtime;
        _runtime = null;
        if (old != null) UnityEngine.Object.Destroy(old);
    }
}
