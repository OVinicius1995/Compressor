using System.Net;
using System.Net.Http;
using System.Text.Json;

internal static class ReleaseUpdates
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };
    internal static Version Current { get; } = Version.Parse(typeof(ReleaseUpdates).Assembly.GetName().Version!.ToString(3));

    internal static async Task<Version?> LatestAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/OVinicius1995/Compressor/releases/latest");
        request.Headers.UserAgent.ParseAdd($"JiraVideoCompressor/{Current}");
        using var response = await Client.SendAsync(request).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        string? tag = release.RootElement.GetProperty("tag_name").GetString();
        if (tag is null || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version) || tag != $"v{version.ToString()}" || version.Build < 0 || version.Revision >= 0)
            throw new InvalidDataException("A versão publicada tem um formato desconhecido.");
        return version;
    }
}
