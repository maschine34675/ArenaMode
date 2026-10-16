using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using ArenaMode.Hud;

// Real embedded bytes/metadata, independent of the Unity ownership doubles.
internal static class AssetContracts
{
    internal static int Verify(string clientAssembly)
    {
        int checks=0;
        void Need(bool value,string reason){if(!value)throw new Exception("CHECK: "+reason);checks++;}
        var assembly=typeof(ArenaUiAssets).Assembly;
        byte[] Resource(string name)
        {
            using var stream=assembly.GetManifestResourceStream(name)??throw new Exception("Missing resource "+name);
            using var output=new MemoryStream();stream.CopyTo(output);return output.ToArray();
        }
        var resources=assembly.GetManifestResourceNames().Where(n=>n.StartsWith("ArenaMode.Ui.",StringComparison.Ordinal))
            .ToDictionary(n=>n,Resource,StringComparer.Ordinal);
        using var retired=JsonDocument.Parse(Resource("Checks.RetiredArtwork.json"));
        var retiredHashes=retired.RootElement.GetProperty("assets").EnumerateArray()
            .Select(e=>e.GetProperty("sha256").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(retiredHashes.Count==18,"exact retired artwork rejection catalog");
        Validate(resources,"harness");
        if(!string.IsNullOrEmpty(clientAssembly))
        {
            // Read metadata, never load Unity/EFT or execute the candidate DLL.
            using var file=File.OpenRead(clientAssembly);using var pe=new PEReader(file);
            var metadata=pe.GetMetadataReader();var directory=pe.PEHeaders.CorHeader!.ResourcesDirectory;
            var block=pe.GetSectionData(directory.RelativeVirtualAddress);
            var candidate=new Dictionary<string,byte[]>(StringComparer.Ordinal);
            foreach(var handle in metadata.ManifestResources)
            {
                var entry=metadata.GetManifestResource(handle);string name=metadata.GetString(entry.Name);
                bool ui=name.StartsWith("ArenaMode.Ui.",StringComparison.Ordinal);
                if(ui)Need(entry.Implementation.IsNil,"client UI resource embedded "+name);
                if(!entry.Implementation.IsNil)continue;
                var reader=block.GetReader(checked((int)entry.Offset),block.Length-checked((int)entry.Offset));
                int length=reader.ReadInt32();byte[] embedded=reader.ReadBytes(length);
                Need(!retiredHashes.Contains(Convert.ToHexString(SHA256.HashData(embedded))),"retired bytes absent from every client resource "+name);
                if(ui)candidate.Add(name,embedded);
            }
            Validate(candidate,"client");
            foreach(string name in new[]{"NOTICE.md","SOURCES.json","MIT-LICENSE.txt"})
            {
                string path=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(clientAssembly))!,"ArenaMode","Ui",name);
                Need(File.Exists(path)&&File.ReadAllBytes(path).SequenceEqual(resources["ArenaMode.Ui."+name]),"readable output notice matches embedded "+name);
            }
            Console.WriteLine("STAGED CLIENT UI RESOURCES VERIFIED");
        }
        return checks;

        void Validate(Dictionary<string,byte[]> bytes,string target)
        {
            var specs=Enum.GetValues<ArenaUiIcon>().Select(ArenaUiAssets.Describe).ToArray();
            var expected=specs.Select(s=>"ArenaMode.Ui."+s.File)
                .Concat(new[]{"ArenaMode.Ui.NOTICE.md","ArenaMode.Ui.SOURCES.json","ArenaMode.Ui.MIT-LICENSE.txt"}).ToHashSet(StringComparer.Ordinal);
            Need(expected.SetEquals(bytes.Keys),target+" exact embedded UI resource set");
            using var manifest=JsonDocument.Parse(bytes["ArenaMode.Ui.SOURCES.json"]);
            Need(manifest.RootElement.GetProperty("schemaVersion").GetInt32()==2,target+" provenance schema");
            var entries=manifest.RootElement.GetProperty("assets").EnumerateArray().ToArray();
            Need(entries.Length==18&&entries.Select(e=>e.GetProperty("file").GetString()).Distinct().Count()==18,target+" manifest has eighteen unique assets");
            foreach(var spec in specs)
            {
                var entry=entries.Single(e=>e.GetProperty("file").GetString()==spec.File);
                byte[] png=bytes["ArenaMode.Ui."+spec.File];string hash=Convert.ToHexString(SHA256.HashData(png));
                Need(!retiredHashes.Contains(hash),target+" original artwork excluded "+spec.File);
                Need(string.Equals(hash,entry.GetProperty("sha256").GetString(),StringComparison.OrdinalIgnoreCase),target+" manifest hash "+spec.File);
                var dimensions=entry.GetProperty("pixelSize").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
                Need(dimensions.SequenceEqual(new[]{spec.Width,spec.Height})&&ArenaUiAssets.MatchesHeader(png,spec),target+" manifest loader dimensions "+spec.File);
                if(target=="client")Need(png.SequenceEqual(resources["ArenaMode.Ui."+spec.File]),"candidate matches checked artwork "+spec.File);
            }
            if(target=="client")foreach(string name in new[]{"NOTICE.md","SOURCES.json","MIT-LICENSE.txt"})
                Need(bytes["ArenaMode.Ui."+name].SequenceEqual(resources["ArenaMode.Ui."+name]),"candidate notices match current "+name);
        }
    }
}
