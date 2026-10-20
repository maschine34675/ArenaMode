using ArenaStart;

try
{
    if (args.Length == 3 && args[0] is "plan" or "prepare")
    {
        var plan = EnvironmentCopy.Plan(args[1], args[2]);
        Console.WriteLine($"Copy-only Arena environment: {plan.Files.Count} files, {plan.Bytes / 1073741824d:F2} GiB");
        Console.WriteLine($"Source (read only): {plan.Source}");
        Console.WriteLine($"Destination (must not exist): {plan.Destination}");
        Console.WriteLine("Only SPT + ArenaMode + Redeploy.Core. No player profiles, launcher settings, BepInEx configs or optional mods imported.");
        if (args[0] == "prepare")
        {
            EnvironmentCopy.Prepare(plan, Console.WriteLine);
            Console.WriteLine("ARENA_ENVIRONMENT_READY");
        }
    }
    else if (args.Length == 2 && args[0] == "verify")
    {
        var manifest = EnvironmentCopy.Verify(args[1], Console.WriteLine);
        Console.WriteLine($"ARENA_ENVIRONMENT_VERIFIED ({manifest.Files.Count} files; internal account only)");
    }
    else if (args.Length == 2 && args[0] is "start" or "smoke")
    {
        await RuntimeHost.Run(args[1], args[0] == "smoke");
    }
    else
    {
        Console.WriteLine("""
            ArenaStart prototype (Windows / SPT 4.1)
              plan    <installed-SPT-root> <new-separate-Arena-root>
              prepare <installed-SPT-root> <new-separate-Arena-root>
              verify  <Arena-root>
              smoke   <Arena-root>   Boot server, create/check private account, stop server.
              start   <Arena-root>   Boot server and native SPT Launcher; close both game
                                    and launcher, then press Enter here to stop server.
            Normal SPT/server/launcher must be closed. This tool never imports player
            profiles, moves original mods, downloads files or updates an existing copy.
            First game start uses native character creation for the private Arena account.
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
