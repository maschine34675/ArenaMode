using System;

namespace ArenaMode.Combat.Fire;

/// <summary>Original synthesis, no recordings or third-party samples.</summary>
internal static class ArenaFireSound
{
    internal const int Rate = 22050;
    internal static float[] BreakAndIgnite()
    {
        var pcm = new float[Rate];
        var rng = new Random(30419);
        double low = 0;
        for (int i = 0; i < pcm.Length; i++)
        {
            double t = i / (double)Rate, noise = rng.NextDouble() * 2 - 1;
            low += (noise - low) * .06;
            // A short brittle impact and inharmonic ringing, then a soft rush.
            double ring = (Math.Sin(t * 2 * Math.PI * 2637) + .5 * Math.Sin(t * 2 * Math.PI * 4211)
                + .3 * Math.Sin(t * 2 * Math.PI * 6719)) * Math.Exp(-t * 35);
            double crack = noise * Math.Exp(-t * 75) * .6 + ring * .12;
            double rush = t < .06 ? 0 : Math.Sin(Math.Min(1, (t - .06) / .9) * Math.PI) * low * 1.7;
            pcm[i] = (float)((crack + rush) * Math.Min(1, t * 1000));
        }
        return Finish(pcm, loop: false);
    }

    internal static float[] Burn()
    {
        var pcm = new float[Rate * 4];
        var rng = new Random(91724);
        double low = 0, mid = 0, crack = 0;
        for (int i = 0; i < pcm.Length; i++)
        {
            double t = i / (double)Rate, noise = rng.NextDouble() * 2 - 1;
            low += (noise - low) * .024;
            mid += (noise - mid) * .24;
            if (rng.NextDouble() < .0009) crack = .1 + rng.NextDouble() * .28;
            crack *= .989;
            double swell = .75 + .12 * Math.Sin(t * 2 * Math.PI * .75) + .08 * Math.Sin(t * 2 * Math.PI * 2);
            pcm[i] = (float)((low * 1.25 + mid * .25) * swell + noise * crack);
        }
        return Finish(pcm, loop: true);
    }

    internal static float[] Extinguish()
    {
        var pcm = new float[Rate / 2];
        var rng = new Random(8617);
        double low = 0;
        for (int i = 0; i < pcm.Length; i++)
        {
            double t = i / (double)pcm.Length, noise = rng.NextDouble() * 2 - 1;
            low += (noise - low) * .1;
            pcm[i] = (float)((noise - low) * .12 * Math.Sin(t * Math.PI) * (1 - t));
        }
        return Finish(pcm, loop: false);
    }

    private static float[] Finish(float[] pcm, bool loop)
    {
        double mean = 0;
        foreach (float value in pcm) mean += value;
        mean /= pcm.Length;
        for (int i = 0; i < pcm.Length; i++) pcm[i] -= (float)mean;
        if (loop)
        {
            // Join the tail to the head with a short crossfade, preserving the
            // steady burn instead of leaving a silent gap on each repetition.
            int blend = Rate / 40;
            for (int i = 0; i < blend; i++)
            {
                float mix = i / (float)(blend - 1);
                int end = pcm.Length - blend + i;
                pcm[end] = pcm[end] * (1 - mix) + pcm[i] * mix;
            }
            // Skip the head consumed by that overlap: the next repetition then
            // continues at exactly the next original sample.
            var joined = new float[pcm.Length - blend];
            Array.Copy(pcm, blend, joined, 0, joined.Length);
            pcm = joined;
        }
        else
        {
            int edge = Rate / 200;
            for (int i = 0; i < edge; i++)
            {
                float gain = i / (float)edge;
                pcm[i] *= gain;
                pcm[pcm.Length - 1 - i] *= gain;
            }
        }
        for (int i = 0; i < pcm.Length; i++) pcm[i] = Math.Max(-.8f, Math.Min(.8f, pcm[i]));
        return pcm;
    }
}
