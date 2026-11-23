using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Veyro;

internal static class ReleaseCheck
{
    internal const string InstallerName = "Veyro-Setup.exe";
    internal sealed record Asset(string Tag, Version Version, long Size, byte[] Sha256);

    internal static async Task<Asset?> LatestAsync(CancellationToken token)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 256 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Veyro-UpdateCheck");
        var json = await client.GetByteArrayAsync("https://api.github.com/repos/officialFengi/Veyro/releases/latest", token);
        using var document = JsonDocument.Parse(json);
        return FindUpdate(document.RootElement, typeof(ReleaseCheck).Assembly.GetName().Version!);
    }

    internal static async Task<int> RunAsync()
    {
        try
        {
            var asset = await LatestAsync(CancellationToken.None);
            if (asset is not null) Console.WriteLine(asset.Tag);
            return 0;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException
                                      or InvalidOperationException or InvalidDataException or FormatException or System.Collections.Generic.KeyNotFoundException)
        {
            return 1;
        }
    }

    internal static Asset? FindUpdate(JsonElement release, Version installed)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString();
        if (string.IsNullOrEmpty(tag)) return null;
        var number = tag[0] is 'v' or 'V' ? tag[1..] : tag;
        foreach (var c in number)
            if (!char.IsAsciiDigit(c) && c != '.') return null;
        if (!Version.TryParse(number, out var version)) return null;
        version = new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
        if (version <= installed) return null;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != InstallerName || asset.GetProperty("state").GetString() != "uploaded") continue;
            long size = asset.GetProperty("size").GetInt64();
            if (size <= 0) continue;
            var digest = asset.GetProperty("digest").GetString();
            if (digest?.Length != 71 || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("В релизе отсутствует контрольная сумма установщика.");
            return new Asset(tag, version, size, Convert.FromHexString(digest.AsSpan(7)));
        }
        return null;
    }
}
