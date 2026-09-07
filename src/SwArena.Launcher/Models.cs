using System.Text.Json.Serialization;

namespace SwArena.Launcher;

public sealed record LauncherSettings(
    [property: JsonPropertyName("manifestUrl")] string ManifestUrl,
    [property: JsonPropertyName("gameExecutable")] string GameExecutable,
    [property: JsonPropertyName("launchArguments")] string LaunchArguments,
    [property: JsonPropertyName("allowOfflineLaunch")] bool AllowOfflineLaunch = false);

public sealed record UserLauncherState(
    [property: JsonPropertyName("clientPath")] string ClientPath);

public sealed record UpdateManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("publishedAt")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("files")] IReadOnlyList<ManifestFile> Files,
    [property: JsonPropertyName("delete")] IReadOnlyList<string>? Delete);

public sealed record ManifestFile(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("sha256")] string Sha256);

public sealed record UpdateProgress(string Status, string Detail, double Percentage);
