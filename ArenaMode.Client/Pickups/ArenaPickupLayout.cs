using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ArenaMode.Pickups;

[JsonConverter(typeof(StringEnumConverter))]
internal enum ArenaPickupKind { DoubleDamage, Shield, Weapon }

internal sealed class ArenaPickupPoint
{
    public string Id;
    public ArenaPickupKind Kind;
    public float X, Y, Z, Yaw;
    public string KitId;
    public float RespawnSeconds = 30f;
}

internal sealed class ArenaPickupLayout
{
    public string MapId;
    // null means the full map only, never every sub-area on that map.
    public string AreaId;
    public List<ArenaPickupPoint> Points = new();
}

internal sealed class ArenaPickupLayoutFile
{
    public int Version = 1;
    public List<ArenaPickupLayout> Layouts = new();
}

/// <summary>Bounded, per-match configuration. No scene objects or live inventory here.</summary>
internal static class ArenaPickupLayouts
{
    private static readonly JsonSerializerSettings Json = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        MissingMemberHandling = MissingMemberHandling.Error,
        MaxDepth = 12
    };

    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal static ArenaPickupLayoutFile Parse(string text)
    {
        if (text == null || text.Length > 512 * 1024) throw new InvalidDataException("Pickup layout exceeds 512 KiB.");
        var file = JsonConvert.DeserializeObject<ArenaPickupLayoutFile>(text, Json);
        Validate(file);
        return file;
    }

    internal static void Validate(ArenaPickupLayoutFile file)
    {
        if (file?.Version != 1 || file.Layouts == null || file.Layouts.Count > 32)
            throw new InvalidDataException("Expected pickup layout version 1, at most 32 layouts.");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layout in file.Layouts)
        {
            if (layout == null || string.IsNullOrWhiteSpace(layout.MapId) || layout.MapId.Length > 80
                || (layout.AreaId != null && (string.IsNullOrWhiteSpace(layout.AreaId) || layout.AreaId.Length > 120))
                || !keys.Add(layout.MapId + "\n" + (layout.AreaId ?? "")) || layout.Points == null || layout.Points.Count > 64)
                throw new InvalidDataException("Invalid or duplicate pickup layout.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var point in layout.Points)
            {
                if (point == null || string.IsNullOrWhiteSpace(point.Id) || point.Id.Length > 80 || !ids.Add(point.Id)
                    || !Enum.IsDefined(typeof(ArenaPickupKind), point.Kind)
                    || !Finite(point.X) || !Finite(point.Y) || !Finite(point.Z) || !Finite(point.Yaw)
                    || Math.Abs(point.X) > 100000 || Math.Abs(point.Y) > 100000 || Math.Abs(point.Z) > 100000
                    || !Finite(point.RespawnSeconds) || point.RespawnSeconds < 10 || point.RespawnSeconds > 600
                    || point.Kind == ArenaPickupKind.Weapon && (string.IsNullOrWhiteSpace(point.KitId) || point.KitId.Length > 100))
                    throw new InvalidDataException("Invalid pickup point in " + layout.MapId + ".");
            }
        }
    }

    internal static ArenaPickupLayout Select(ArenaPickupLayoutFile file, string map, string area)
        => file.Layouts.FirstOrDefault(layout => string.Equals(layout.MapId, map, StringComparison.OrdinalIgnoreCase)
            && string.Equals(layout.AreaId, area, StringComparison.Ordinal));

    internal static ArenaPickupLayoutFile Factory(string kit)
    {
        var file = new ArenaPickupLayoutFile();
        foreach (string map in new[] { "factory4_day", "factory4_night" })
            file.Layouts.Add(new ArenaPickupLayout
            {
                MapId = map,
                Points = new List<ArenaPickupPoint>
                {
                    // Literal initial candidates from native Bot-only day/night markers;
                    // the live scene must still prove floor/nav/clearance before display.
                    new() { Id = "factory-damage", Kind = ArenaPickupKind.DoubleDamage, X = 35.924f, Y = 4.55418539f, Z = 35.339f },
                    new() { Id = "factory-shield", Kind = ArenaPickupKind.Shield, X = -32.087f, Y = -2.581647f, Z = 29.9733315f },
                    new() { Id = "factory-weapon", Kind = ArenaPickupKind.Weapon, X = -5.89f, Y = .08f, Z = -27.46f, KitId = kit, RespawnSeconds = 45f }
                }
            });
        Validate(file);
        return file;
    }

    internal static string Serialize(ArenaPickupLayoutFile file)
    {
        Validate(file);
        return JsonConvert.SerializeObject(file, Formatting.Indented, Json);
    }

    internal static string Export(string directory, ArenaPickupLayoutFile file)
    {
        string text = Serialize(file);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "pickup-draft-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream)) writer.Write(text);
        return path;
    }
}
