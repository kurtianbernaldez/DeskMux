using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
namespace DeskMux.Core;
public sealed class PackageManifest
{
    public string Product {get;set;}="DeskMux";
    public string Version {get;set;}="";
    public string BuiltUtc {get;set;}="";
    public Dictionary<string,string> Files {get;set;}=[];
}
public static class UpdatePackage
{
    public static PackageManifest Verify(string folder)
    {
        var manifest=JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(Path.Combine(folder,"build.json")))??throw new InvalidDataException("Missing build information.");
        if(manifest.Product!="DeskMux"||manifest.Files==null||!manifest.Files.ContainsKey("DeskMux.exe"))throw new InvalidDataException("This is not a DeskMux update.");
        var root=Path.GetFullPath(folder)+Path.DirectorySeparatorChar;
        foreach(var (name,hash) in manifest.Files) {
            var path=Path.GetFullPath(Path.Combine(folder,name));
            if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||name.Split('/','\\').Any(p=>p.Equals("Data",StringComparison.OrdinalIgnoreCase)||p.Equals("portable.mode",StringComparison.OrdinalIgnoreCase)||p.StartsWith("sessions.json",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Unsafe package path.");
            using var input=File.OpenRead(path);
            if(!Convert.ToHexString(SHA256.HashData(input)).Equals(hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update file failed verification: "+name);
        }
        return manifest;
    }
    public static string Extract(string archive,string destination)
    {
        using(var zip=ZipFile.OpenRead(archive)) {
            if(zip.Entries.Count>5000||zip.Entries.Sum(e=>e.Length)>1024L*1024*1024)throw new InvalidDataException("Update package is too large.");
            zip.ExtractToDirectory(destination);
        }
        var folders=Directory.GetFiles(destination,"build.json",SearchOption.AllDirectories).Select(Path.GetDirectoryName).ToArray();
        if(folders.Length!=1)throw new InvalidDataException("Choose a ZIP containing one DeskMux build.");
        Verify(folders[0]!);return folders[0]!;
    }
}
