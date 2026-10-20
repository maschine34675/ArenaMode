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
    else if (args.Length == 3 && args[0] is "plan" or "prepare" or "plan-copy" or "prepare-copy")
    {
        var plan = EnvironmentCopy.Plan(args[1], args[2], shareAssets: !args[0].EndsWith("-copy"), diagnostics);
        Console.WriteLine($"Arena environment: {plan.Files.Count} files; {plan.PrivateBytes / 1073741824d:F2} GiB private, " +
            $"{plan.SharedBytes / 1073741824d:F2} GiB shared content");
        Console.WriteLine($"Source (contents unchanged; shared files gain a hardlink): {plan.Source}");
        Console.WriteLine($"Destination (must not exist): {plan.Destination}");
        Console.WriteLine("Only SPT + ArenaMode + Redeploy.Core. No player profiles, launcher settings, BepInEx configs or optional mods imported.");
        if (args[0] is "prepare" or "prepare-copy")
        {
            EnvironmentCopy.Prepare(plan, Console.WriteLine);
            Console.WriteLine("ARENA_ENVIRONMENT_READY");
        }
    }
    else if (!diagnostics && args.Length == 2 && args[0] == "verify")
    {
        var manifest = EnvironmentCopy.Verify(args[1], Console.WriteLine);
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
            approved ArenaMode/Core/audio/kit files from normal SPT with rollback protection.
            Game and SPT updates still require a new environment; unrelated mods stay excluded.
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
