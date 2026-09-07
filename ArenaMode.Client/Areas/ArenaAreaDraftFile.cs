using System;
using System.IO;
using System.Text;

namespace ArenaMode.Areas;

internal static class ArenaAreaDraftFile
{
    /// <summary>Publish one complete new file. No map IDs or user text become a path.</summary>
    public static string Write(string directory, string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("The draft is empty.");
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        var filename = "area-draft-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
        var temporary = Path.Combine(directory, filename + ".tmp");
        var result = Path.Combine(directory, filename + ".json");
        bool created = false;
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                using var writer = new StreamWriter(file, new UTF8Encoding(false));
                writer.Write(json);
            }
            File.Move(temporary, result); // never overwrite an existing capture
            created = false;
            return result;
        }
        finally { if (created && File.Exists(temporary)) File.Delete(temporary); }
    }
}
