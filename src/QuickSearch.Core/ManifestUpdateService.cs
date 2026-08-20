using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickSearch.Core;

public sealed class ManifestUpdateService : IApplicationUpdateService
{
    private const string UserAgent = "QuickSearch-UpdateClient";
    private readonly HttpClient _httpClient;
    private readonly Uri _manifestUri;
    private readonly string _downloadRoot;
    private readonly IInstallerLauncher _installerLauncher;

    public ManifestUpdateService(
        HttpClient httpClient,
        Uri manifestUri,
        string downloadRoot,
        IInstallerLauncher installerLauncher)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(manifestUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadRoot);
        ArgumentNullException.ThrowIfNull(installerLauncher);
        _httpClient = httpClient;
        _manifestUri = RequireHttpsUri(manifestUri.AbsoluteUri, "更新清单");
        _downloadRoot = downloadRoot;
        _installerLauncher = installerLauncher;
    }

    public async Task<ApplicationUpdateCheck> CheckAsync(
        AppReleaseVersion installedVersion,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(_manifestUri, "application/json");
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        EnsureSuccessfulResponse(response, "检查更新");

        await using var manifestStream =
            await response.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(
            manifestStream,
            cancellationToken: cancellationToken);
        if (manifest is null)
        {
            throw new InvalidDataException("更新清单内容为空。");
        }

        var latestVersion = AppReleaseVersion.Parse(manifest.Version);
        if (!latestVersion.IsNewerThan(installedVersion))
        {
            return ApplicationUpdateCheck.Current(latestVersion.ToString());
        }

        return ApplicationUpdateCheck.Available(new ApplicationRelease(
            latestVersion.ToString(),
            RequireHttpsUri(manifest.ReleaseNotesUrl, "更新说明"),
            RequireHttpsUri(manifest.InstallerUrl, "安装包"),
            RequireHttpsUri(manifest.ChecksumUrl, "SHA-256 校验文件")));
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
        EnsureSuccessfulResponse(checksumResponse, "下载校验文件");
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
            EnsureSuccessfulResponse(installerResponse, "下载安装包");
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

            string actualHash;
            await using (var downloaded = File.OpenRead(temporaryPath))
            {
                actualHash = Convert.ToHexString(
                    await SHA256.HashDataAsync(downloaded, cancellationToken));
            }

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

    private static HttpRequestMessage CreateRequest(
        Uri uri,
        string acceptMediaType = "application/octet-stream")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(ProductInfoHeaderValue.Parse(UserAgent));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptMediaType));
        return request;
    }

    private static void EnsureSuccessfulResponse(
        HttpResponseMessage response,
        string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{operation}失败：更新服务器返回 HTTP {(int)response.StatusCode}。");
    }

    private static Uri RequireHttpsUri(string? value, string description)
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

    private sealed record UpdateManifest(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("releaseNotesUrl")] string ReleaseNotesUrl,
        [property: JsonPropertyName("installerUrl")] string InstallerUrl,
        [property: JsonPropertyName("checksumUrl")] string ChecksumUrl);
}
