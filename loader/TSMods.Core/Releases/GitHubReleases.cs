using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TSMods.Core.Releases;

public sealed record RemoteMod(string ModName, string Version, string Tag, string AssetName, string DownloadUrl, DateTimeOffset PublishedAt, string Notes);

public sealed partial class GitHubReleases(HttpClient http, string repo)
{
    public string Repo { get; } = repo;

    public async Task<IReadOnlyList<RemoteMod>> ListAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases?per_page=100");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TSMods", "0.1"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var releases = await JsonSerializer.DeserializeAsync<List<Release>>(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct) ?? [];

        return releases
            .Where(r => !r.Draft)
            .SelectMany(r => r.Assets
                .Where(a => a.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(a => new RemoteMod(
                    ModNameFromAsset(a.Name),
                    VersionFromTag(r.TagName),
                    r.TagName,
                    a.Name,
                    a.BrowserDownloadUrl,
                    r.PublishedAt ?? DateTimeOffset.MinValue,
                    r.Body ?? "")))
            .OrderBy(m => m.ModName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(m => m.PublishedAt)
            .ToList();
    }

    public async Task<string> DownloadAsync(RemoteMod mod, string directory, CancellationToken ct = default)
    {
        var target = Path.Combine(directory, mod.Tag, mod.AssetName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var source = await http.GetStreamAsync(mod.DownloadUrl, ct);
        await using var file = File.Create(target);
        await source.CopyToAsync(file, ct);
        return target;
    }

    public static string TagFor(string modShortName, string version) => $"{modShortName.ToLowerInvariant()}-v{version}";

    private static string ModNameFromAsset(string assetName) => Path.GetFileNameWithoutExtension(assetName).Split('.')[^1];

    private static string VersionFromTag(string tag) => TagVersionPattern().Match(tag) is { Success: true } m ? m.Groups[1].Value : tag;

    [GeneratedRegex(@"v(\d+(\.\d+)*([-+].*)?)$")]
    private static partial Regex TagVersionPattern();

    private sealed record Release(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
        [property: JsonPropertyName("assets")] List<Asset> Assets);

    private sealed record Asset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
