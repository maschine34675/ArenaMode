using System.Reflection;
using ArenaMode.Server.Rewards;
using HarmonyLib;
using SPTarkov.Server.Core.Utils;

namespace ArenaMode.Server.Patches;

// Captures what SPT's own write path does to a profile (see ProfileWriteProof). Plain static patch classes, not
// AbstractPatch: the priorities must be exact and are verified through Harmony.GetPatchInfo after patching.
//
// FileUtil.WriteFileAsync(string, string, CancellationToken) is `async`: it encodes the text and calls the byte[]
// overload before its first await, so a prefix of the inner call runs in the sync segment on the same thread.
//   - the input is taken at the string overload with Priority.First (before any other prefix can change it),
//   - the output at the byte[] overload with Priority.Last (after every other prefix),
//   - the finalizer on the string overload pairs them even if an earlier prefix skipped or threw.
// Parameters are bound by position (__0, __1): the installed SPT may name them differently from the reference source.
// Another mod at Priority.First or Last on these methods makes the capture differ from reality; the comparison then
// fails closed (the entry is declined as without this hook).
internal static class ArenaProfileWriteCapturePatches
{
    internal const string StringWriteHarmonyId = nameof(ArenaProfileWriteCaptureStringWriteInput);
    internal const string ByteWriteHarmonyId = nameof(ArenaProfileWriteCaptureByteWriteOutput);

    private static Harmony? _stringHarmony;
    private static Harmony? _byteHarmony;

    internal static MethodInfo? StringTarget => typeof(FileUtil).GetMethod(nameof(FileUtil.WriteFileAsync),
        BindingFlags.Instance | BindingFlags.Public, Type.DefaultBinder,
        [typeof(string), typeof(string), typeof(CancellationToken)], null);

    internal static MethodInfo? ByteTarget => typeof(FileUtil).GetMethod(nameof(FileUtil.WriteFileAsync),
        BindingFlags.Instance | BindingFlags.Public, Type.DefaultBinder,
        [typeof(string), typeof(byte[]), typeof(CancellationToken)], null);

    internal static bool TryEnable() => TryEnable(StringTarget, ByteTarget);

    // Test seam: the targets are parameters so a failure can be forced.
    internal static bool TryEnable(MethodInfo? stringTarget, MethodInfo? byteTarget)
    {
        ProfileWriteProof.MarkReady(false);
        try
        {
            if (stringTarget is null || byteTarget is null) throw new MissingMethodException("FileUtil.WriteFileAsync overload not found");
            Disable();
            _stringHarmony = new Harmony(StringWriteHarmonyId);
            _byteHarmony = new Harmony(ByteWriteHarmonyId);
            _stringHarmony.Patch(stringTarget,
                prefix: new HarmonyMethod(typeof(ArenaProfileWriteCaptureStringWriteInput).GetMethod(
                    nameof(ArenaProfileWriteCaptureStringWriteInput.Prefix))!) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(ArenaProfileWriteCaptureStringWriteInput).GetMethod(
                    nameof(ArenaProfileWriteCaptureStringWriteInput.Finalizer))!) { priority = Priority.Last });
            _byteHarmony.Patch(byteTarget,
                prefix: new HarmonyMethod(typeof(ArenaProfileWriteCaptureByteWriteOutput).GetMethod(
                    nameof(ArenaProfileWriteCaptureByteWriteOutput.Prefix))!) { priority = Priority.Last });
            VerifyInstalled(stringTarget, byteTarget);
            ProfileWriteProof.MarkReady(true);
            LogForeign(stringTarget, byteTarget);
            return true;
        }
        catch (Exception exception)
        {
            Disable();
            ProfileWriteProof.MarkReady(false);
            ArenaContext.Logger?.Warning("[ArenaMode] Profile write proof unavailable; Arena keeps the strict save comparison.");
            ArenaContext.Logger?.Debug($"[ArenaMode] Profile write proof failure: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    internal static void Disable()
    {
        ProfileWriteProof.MarkReady(false);
        try { _stringHarmony?.UnpatchSelf(); } catch { }
        try { _byteHarmony?.UnpatchSelf(); } catch { }
        _stringHarmony = null;
        _byteHarmony = null;
    }

    private static void VerifyInstalled(MethodInfo stringTarget, MethodInfo byteTarget)
    {
        var stringInfo = Harmony.GetPatchInfo(stringTarget);
        var byteInfo = Harmony.GetPatchInfo(byteTarget);
        bool input = stringInfo is not null && stringInfo.Prefixes.Any(patch => patch.owner == StringWriteHarmonyId
            && patch.priority == Priority.First && patch.PatchMethod.Name == nameof(ArenaProfileWriteCaptureStringWriteInput.Prefix));
        bool pairing = stringInfo is not null && stringInfo.Finalizers.Any(patch => patch.owner == StringWriteHarmonyId
            && patch.priority == Priority.Last && patch.PatchMethod.Name == nameof(ArenaProfileWriteCaptureStringWriteInput.Finalizer));
        bool output = byteInfo is not null && byteInfo.Prefixes.Any(patch => patch.owner == ByteWriteHarmonyId
            && patch.priority == Priority.Last && patch.PatchMethod.Name == nameof(ArenaProfileWriteCaptureByteWriteOutput.Prefix));
        if (!input || !pairing || !output) throw new InvalidOperationException("profile write capture patches are not installed as designed");
    }

    // Counts only, no names: a foreign prefix on the write path or a foreign postfix on the unpatched comparison
    // is the first thing to look at when a capture disagrees with the disk.
    private static void LogForeign(MethodInfo stringTarget, MethodInfo byteTarget)
    {
        try
        {
            if (ArenaContext.Logger is null) return;
            int stringPrefixes = ForeignPrefixes(stringTarget), bytePrefixes = ForeignPrefixes(byteTarget);
            var same = typeof(RewardRuntimeFiles).GetMethod(nameof(RewardRuntimeFiles.SameProfile),
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            int postfixes = same is null ? 0 : Harmony.GetPatchInfo(same)?.Postfixes.Count ?? 0;
            ArenaContext.Logger.Debug($"[ArenaMode] Profile write proof enabled; foreign prefixes: text write {stringPrefixes}, "
                + $"byte write {bytePrefixes}; foreign postfixes on the profile comparison: {postfixes}.");
        }
        catch { }
    }

    private static int ForeignPrefixes(MethodInfo target)
        => Harmony.GetPatchInfo(target)?.Prefixes.Count(patch => patch.owner != StringWriteHarmonyId && patch.owner != ByteWriteHarmonyId) ?? 0;
}

internal static class ArenaProfileWriteCaptureStringWriteInput
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(string __0, string __1) => ProfileWriteProof.Begin(__0, __1);

    // No exception parameter and no return value: an exception still propagates unchanged.
    [HarmonyPriority(Priority.Last)]
    public static void Finalizer() => ProfileWriteProof.End();
}

internal static class ArenaProfileWriteCaptureByteWriteOutput
{
    [HarmonyPriority(Priority.Last)]
    public static void Prefix(string __0, byte[] __1) => ProfileWriteProof.Seal(__0, __1);
}
