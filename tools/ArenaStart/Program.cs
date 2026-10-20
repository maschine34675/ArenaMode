using ArenaStart;

try
{
    var diagnostics = args.Length > 0 && args[^1] == "--diagnostics";
    if (diagnostics) args = args[..^1];
    if (args.Length == 2 && args[0] == "play")
    {
        var root = ManagedEnvironment.Ensure(args[1], diagnostics, Console.WriteLine);
        await RuntimeHost.Run(root, smoke: false);
    }
    else if (args.Length == 2 && args[0] == "rebuild")
    {
        await BaseRebuild.Rebuild(args[1], diagnostics, Console.WriteLine);
        Console.WriteLine("ARENA_BASE_REBUILD_PASS");
    }
    else if (args.Length == 3 && args[0] is "plan" or "prepare" or "plan-copy" or "prepare-copy")
    {
        var plan = EnvironmentCopy.Plan(args[1], args[2], shareAssets: !args[0].EndsWith("-copy"), diagnostics);
        Console.WriteLine($"Arena environment: {plan.Files.Count} files; {plan.PrivateBytes / 1073741824d:F2} GiB private, " +
            $"{plan.SharedBytes / 1073741824d:F2} GiB shared content");
        Console.WriteLine($"Source (contents unchanged; shared files gain a hardlink): {plan.Source}");
        Console.WriteLine($"Destination (must not exist): {plan.Destination}");
        Console.WriteLine("SPT + ArenaMode + Redeploy.Core and approved installed AI packages. No player profiles, launcher settings or mod configurations imported.");
        Console.WriteLine(RecommendedModPackage.Summary(plan.Source));
        if (args[0] is "prepare" or "prepare-copy")
        {
            EnvironmentCopy.Prepare(plan, Console.WriteLine);
            Console.WriteLine("ARENA_ENVIRONMENT_READY");
        }
    }
    else if (!diagnostics && args.Length == 2 && args[0] == "verify")
    {
        var anchor = EnvironmentCopy.Root(args[1]);
        using var control = BaseRebuild.Acquire(anchor);
        BaseRebuild.RequireNoPending(anchor);
        var owner = BaseRebuild.Read<Manifest>(anchor, EnvironmentCopy.ManifestPath);
        var active = owner.Schema == 2 ? BaseRebuild.Resolve(anchor, owner.Source, owner.Diagnostics) : anchor;
        var manifest = EnvironmentCopy.Verify(active, Console.WriteLine,
            runningLease: active == anchor ? control.SafeFileHandle : null);
        Console.WriteLine($"ARENA_ENVIRONMENT_VERIFIED ({manifest.Files.Count} files; internal account only)");
    }
    else if (!diagnostics && args.Length == 2 && args[0] is "start" or "smoke")
    {
        await RuntimeHost.Run(args[1], args[0] == "smoke");
    }
    else
    {
        Console.WriteLine("""
            ArenaStart prototype (Windows / SPT 4.1)
              play    <installed-SPT-root> [--diagnostics]
                      Prepare/reuse <installed-SPT-root>-Arena and open its native launcher.
              rebuild <installed-SPT-root> [--diagnostics]
                      Prepare/check a new base, preserve internal account/settings, activate.
                      Previous runtimes remain on disk; shared assets are not a backup.
              plan    <installed-SPT-root> <new-separate-Arena-root>
              prepare <installed-SPT-root> <new-separate-Arena-root>
                      Uses shared assets on the SAME NTFS volume. No full-copy fallback.
              plan-copy / prepare-copy <source> <destination>
                      Explicit full-copy reference mode (large disk requirement).
              verify  <Arena-root>
              smoke   <Arena-root>   Boot server, create/check private account, stop server.
              start   <Arena-root>   Boot server and native SPT Launcher; close both game
                                    and launcher, then press Enter here to stop server.
            Normal SPT/server/launcher must be closed. This tool never imports player
            profiles, moves original mods or downloads files. Before each start it refreshes
            approved ArenaMode/Core/audio/kit files and installed SAIN/APBS/ABPS/BigBrain/Waypoints
            packages from normal SPT with rollback protection. Mod settings start separately.
            After updating normal SPT, use rebuild for the Arena base; unrelated mods stay excluded.
            --diagnostics at the end of play/plan/prepare additionally admits the installed
            UiBridge DLL for native testing. No original bridge configuration is imported.
            Shared content is write/delete-locked while the starter runs. Do not force-close it.
            First game start uses native character creation for the private Arena account.
            Each start copies available native game settings from the original SPT folder;
            no settings write back. Missing source files leave Arena settings unchanged.
            """);
        return 2;
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Arena start refused: {error.Message}");
    return 1;
}
