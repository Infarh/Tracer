using System.Text.Json.Serialization;

namespace Tracer;

public class ReleaseInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    public Version? Version => TagName != null && Version.TryParse(TagName.TrimStart('v'), out var version) ? version : null;

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public IReadOnlyList<Asset> Assets { get; set; } = [];

    public class Asset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? DownloadUrl { get; set; }

        public async Task DownloadAsync(string DestinationPath)
        {
            if (string.IsNullOrWhiteSpace(DownloadUrl))
                throw new InvalidOperationException("DownloadUrl empty.");

            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.Add(new("TracerUpdater", "1.0"));

            try
            {
                var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using var content_stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var file_stream = File.Create(DestinationPath);

                await content_stream.CopyToAsync(file_stream).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Downloading release error to {DownloadUrl}.", ex);
            }
        }

        public override string ToString() => $"{Name}({Size})[{UpdatedAt}]";
    }

    public override string ToString() => $"Release {Version}";
}