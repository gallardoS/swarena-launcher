using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: SwArena.Publisher <stage-directory> <output-directory> <version> [deletion-list.txt]");
    return 1;
}

var source = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var version = args[2].Trim();
if (!Directory.Exists(source) || string.IsNullOrWhiteSpace(version))
{
    Console.Error.WriteLine("The stage directory must exist and the version cannot be empty.");
    return 2;
}

var payloadRoot = Path.Combine(output, "files", SafeSegment(version));
Directory.CreateDirectory(payloadRoot);
var manifestFiles = new List<ManifestFile>();

foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).OrderBy(x => x))
{
    var relative = Path.GetRelativePath(source, sourceFile).Replace('\\', '/');
    if (relative.Split('/').Any(segment => segment.Equals(".gitignore", StringComparison.OrdinalIgnoreCase)))
        continue;
    ValidateRelativePath(relative);
    var destination = Path.Combine(payloadRoot, relative.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.Copy(sourceFile, destination, true);

    await using var stream = File.OpenRead(sourceFile);
    var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    var urlPath = string.Join('/', relative.Split('/').Select(Uri.EscapeDataString));
    manifestFiles.Add(new(relative, $"files/{Uri.EscapeDataString(version)}/{urlPath}", stream.Length, sha256));
    Console.WriteLine($"+ {relative}");
}

var deletes = new List<string>();
if (args.Length >= 4)
{
    foreach (var line in await File.ReadAllLinesAsync(args[3]))
    {
        var value = line.Trim().Replace('\\', '/');
        if (value.Length == 0 || value.StartsWith('#')) continue;
        ValidateRelativePath(value);
        deletes.Add(value);
    }
}

var manifest = new UpdateManifest(version, DateTimeOffset.UtcNow, manifestFiles, deletes);
var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
Directory.CreateDirectory(output);
var temporaryManifest = Path.Combine(output, "manifest.json.tmp");
await File.WriteAllTextAsync(temporaryManifest, json);
File.Move(temporaryManifest, Path.Combine(output, "manifest.json"), true);
Console.WriteLine($"\nManifest {version}: {manifestFiles.Count} files, {manifestFiles.Sum(x => x.Size):N0} bytes.");
return 0;

static string SafeSegment(string value)
{
    if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value is "." or "..")
        throw new ArgumentException("The version contains invalid characters.");
    return value;
}

static void ValidateRelativePath(string path)
{
    if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Split('/').Any(x => x is ".." or "." or ""))
        throw new InvalidDataException($"Invalid path: {path}");
}

internal sealed record ManifestFile(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("sha256")] string Sha256);

internal sealed record UpdateManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("publishedAt")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("files")] IReadOnlyList<ManifestFile> Files,
    [property: JsonPropertyName("delete")] IReadOnlyList<string> Delete);
