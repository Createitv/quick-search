using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickSearch.Core;

public interface IInstallerLauncher
{
    PlatformOperationResult Launch(string installerPath);
}

public sealed class GitHubReleaseUpdateService : IApplicationUpdateService
{
    private const string UserAgent = "QuickSearch-UpdateClient";
    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repository;
    private readonly string _downloadRoot;
    private readonly IInstallerLauncher _installerLauncher;

    public GitHubReleaseUpdateService(
        HttpClient httpClient,
        string owner,
        string repository,
        string downloadRoot,
        IInstallerLauncher installerLauncher)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadRoot);
        ArgumentNullException.ThrowIfNull(installerLauncher);
        _httpClient = httpClient;
        _owner = owner;
        _repository = repository;
        _downloadRoot = downloadRoot;
        _installerLauncher = installerLauncher;
    }

    public async Task<ApplicationUpdateCheck> CheckAsync(
        AppReleaseVersion installedVersion,
        CancellationToken cancellationToken = default)
    {
        var apiUri = new Uri(
            $"https://api.github.com/repos/{Uri.EscapeDataString(_owner)}/{Uri.EscapeDataString(_repository)}/releases/latest");
        using var request = CreateRequest(apiUri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<ReleasePayload>(
            responseStream,
            cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("GitHub Release 返回了空响应。");

        if (payload.Draft || payload.Prerelease)
        {
            throw new InvalidDataException("GitHub 最新版本不是可自动安装的正式版本。");
        }

        var latestVersion = AppReleaseVersion.Parse(payload.TagName);
        if (!latestVersion.IsNewerThan(installedVersion))
        {
            return ApplicationUpdateCheck.Current(latestVersion.ToString());
        }

        var version = latestVersion.ToString();
        var installerName = $"QuickSearch-Setup-v{version}.exe";
        var checksumName = $"{installerName}.sha256";
        var installerUri = FindRequiredAsset(payload.Assets, installerName, "安装包");
        var checksumUri = FindRequiredAsset(payload.Assets, checksumName, "SHA-256 校验文件");
        var releasePageUri = RequireHttpsUri(payload.HtmlUrl, "Release 页面");

        return ApplicationUpdateCheck.Available(new ApplicationRelease(
            version,
            releasePageUri,
            installerUri,
            checksumUri));
    }

    public async Task<string> DownloadAndVerifyAsync(
        ApplicationRelease release,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        var version = AppReleaseVersion.Parse(release.Version).ToString();
        RequireHttpsUri(release.InstallerUri.AbsoluteUri, "安装包");
        RequireHttpsUri(release.ChecksumUri.AbsoluteUri, "SHA-256 校验文件");

        using var checksumRequest = CreateRequest(release.ChecksumUri);
        using var checksumResponse = await _httpClient.SendAsync(
            checksumRequest,
            cancellationToken);
        checksumResponse.EnsureSuccessStatusCode();
        var checksumText = await checksumResponse.Content.ReadAsStringAsync(cancellationToken);
        var expectedHash = ParseSha256(checksumText);

        var versionDirectory = Path.Combine(_downloadRoot, $"v{version}");
        Directory.CreateDirectory(versionDirectory);
        var installerName = $"QuickSearch-Setup-v{version}.exe";
        var installerPath = Path.Combine(versionDirectory, installerName);
        var temporaryPath = installerPath + $".{Guid.NewGuid():N}.download";

        try
        {
            using var installerRequest = CreateRequest(release.InstallerUri);
            using var installerResponse = await _httpClient.SendAsync(
                installerRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            installerResponse.EnsureSuccessStatusCode();
            var contentLength = installerResponse.Content.Headers.ContentLength;
            await using (var source = await installerResponse.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    totalRead += read;
                    if (contentLength is > 0)
                    {
                        progress?.Report((double)totalRead / contentLength.Value);
                    }
                }
            }

            await using var downloaded = File.OpenRead(temporaryPath);
            var actualHash = Convert.ToHexString(
                await SHA256.HashDataAsync(downloaded, cancellationToken));
            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("安装包 SHA-256 校验失败，已取消安装。");
            }

            File.Move(temporaryPath, installerPath, overwrite: true);
            progress?.Report(1);
            return installerPath;
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    public PlatformOperationResult LaunchInstaller(string installerPath) =>
        _installerLauncher.Launch(installerPath);

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(ProductInfoHeaderValue.Parse(UserAgent));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private static Uri FindRequiredAsset(
        IReadOnlyList<ReleaseAssetPayload> assets,
        string expectedName,
        string description)
    {
        var asset = assets.SingleOrDefault(candidate =>
            string.Equals(candidate.Name, expectedName, StringComparison.Ordinal));
        if (asset is null)
        {
            throw new InvalidDataException($"Release 缺少必需的{description}：{expectedName}");
        }

        return RequireHttpsUri(asset.DownloadUrl, description);
    }

    private static Uri RequireHttpsUri(string value, string description)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{description}地址不是安全的 HTTPS 链接。");
        }

        return uri;
    }

    private static string ParseSha256(string value)
    {
        var hash = (value ?? string.Empty)
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (hash is null
            || hash.Length != 64
            || hash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("SHA-256 校验文件格式无效。");
        }

        return hash;
    }

    private sealed record ReleasePayload(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] IReadOnlyList<ReleaseAssetPayload> Assets);

    private sealed record ReleaseAssetPayload(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl);
}
