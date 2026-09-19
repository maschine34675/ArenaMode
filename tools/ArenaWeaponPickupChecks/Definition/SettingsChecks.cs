using ArenaMode.Pickups;
using BepInEx.Configuration;

internal static partial class Program
{
    private static void VerifyWeaponSettingMigration()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "ArenaM60Settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var paths = new List<string>();
        try
        {
            foreach (string saved in new[] { "spartan", "SPARTAN", "custom", ArenaPickupWeapons.M60Id, "" })
            {
                string path = Path.Combine(scratch, paths.Count + ".cfg");
                paths.Add(path);
                File.WriteAllText(path, "[Arena pickups]\nPrototypeWeaponKit = " + saved
                    + "\n\n[Unrelated]\nKeep = unchanged\n");
                var config = new ConfigFile(path, false) { SaveOnConfigSet = false };
                ArenaPickupSettings.Init(config);
                string expected = saved.Equals("spartan", StringComparison.OrdinalIgnoreCase) ? ArenaPickupWeapons.M60Id : saved;
                Check(ArenaPickupSettings.WeaponKit.Value == expected,
                    "legacy default migrates to M60 while explicit custom choices survive: " + saved);
                config.Save();
                Check(File.ReadAllText(path).Contains("Keep = unchanged"), "migration preserves unrelated orphaned settings");
                var reread = new ConfigFile(path, false) { SaveOnConfigSet = false };
                ArenaPickupSettings.Init(reread);
                Check(ArenaPickupSettings.WeaponKit.Value == expected, "migration is persistent across config reload");
                // A later deliberate return to the M4 must not be overwritten on every launch.
                ArenaPickupSettings.WeaponKit.Value = "spartan";
                reread.Save();
                ArenaPickupSettings.Init(new ConfigFile(path, false) { SaveOnConfigSet = false });
                Check(ArenaPickupSettings.WeaponKit.Value == "spartan", "migration runs only once, preserving later explicit M4 selection");
            }
            string freshPath = Path.Combine(scratch, "fresh.cfg");
            paths.Add(freshPath);
            ArenaPickupSettings.Init(new ConfigFile(freshPath, false) { SaveOnConfigSet = false });
            Check(ArenaPickupSettings.WeaponKit.Value == ArenaPickupWeapons.M60Id, "fresh installs default to copied M60");
        }
        finally
        {
            // Only our individually named fixture files and empty directory.
            foreach (string path in paths) if (File.Exists(path)) File.Delete(path);
            Directory.Delete(scratch, recursive: false);
        }
    }
}
