using ArenaMode.Combat.Fire;

int checks = 0;
void Check(bool okay, string label) { checks++; if (!okay) throw new Exception("FAILED: " + label); }
void Pcm(float[] pcm, double lowSeconds, double highSeconds, string label)
{
    Check(pcm.Length >= lowSeconds * ArenaFireSound.Rate && pcm.Length <= highSeconds * ArenaFireSound.Rate, label + " duration");
    Check(pcm.All(float.IsFinite), label + " finite samples");
    Check(pcm.Max(x => Math.Abs(x)) < .95, label + " headroom");
    double rms = Math.Sqrt(pcm.Average(x => (double)x * x));
    Check(rms > .01 && rms < .3, label + " audible energy without overload");
    Check(Math.Abs(pcm.Average(x => (double)x)) < .002, label + " DC offset");
}
var ignition = ArenaFireSound.BreakAndIgnite(); var burn = ArenaFireSound.Burn(); var end = ArenaFireSound.Extinguish();
Pcm(ignition, .8, 1.1, "ignition"); Pcm(burn, 3.9, 4.1, "burn"); Pcm(end, .4, .6, "extinguish");
Check(ignition[0] == 0 && ignition[^1] == 0 && end[0] == 0 && end[^1] == 0, "one-shot click-free endpoints");
var differences = burn.Zip(burn.Skip(1), (a, b) => Math.Abs(a - b)).Order().ToArray();
Check(Math.Abs(burn[^1] - burn[0]) <= differences[(int)(differences.Length * .99)], "loop seam not an impulse");
Check(burn.SequenceEqual(ArenaFireSound.Burn()), "original synthesis reproducible");
int rejects = 0;
foreach (var bad in new[] { new float[ArenaFireSound.Rate], Enumerable.Repeat(float.NaN, ArenaFireSound.Rate).ToArray(), Enumerable.Repeat(1f, ArenaFireSound.Rate).ToArray() })
{
    try { Pcm(bad, .8, 1.1, "negative control"); }
    catch (Exception ex) when (ex.Message.StartsWith("FAILED:")) { rejects++; }
}
Check(rejects == 3, "validator rejects silence, NaN and clipping controls");
if (args.Length == 1)
{
    Directory.CreateDirectory(args[0]);
    foreach (var (name, pcm) in new[] { ("break-ignite", ignition), ("burn", burn), ("extinguish", end) })
    {
        using var writer = new BinaryWriter(File.Create(Path.Combine(args[0], name + ".wav")));
        writer.Write("RIFF"u8); writer.Write(36 + pcm.Length * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(ArenaFireSound.Rate);
        writer.Write(ArenaFireSound.Rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(pcm.Length * 2);
        foreach (float sample in pcm) writer.Write((short)Math.Round(sample * short.MaxValue));
    }
}
Console.WriteLine($"ARENA_MOLOTOV_PRESENTATION_PASSED: {checks} assertions, three invalid-audio controls; no audition claim");
