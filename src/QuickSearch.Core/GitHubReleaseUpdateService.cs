using System.Net.Http.Headers;
using System.Security.Cryptography;

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
        var latestReleaseUri = BuildGitHubUri("releases/latest");
        using var request = CreateRequest(latestReleaseUri, "text/html");
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        EnsureSuccessfulResponse(response, "检查更新");

        var releasePageUri = response.RequestMessage?.RequestUri
            ?? throw new InvalidDataException("GitHub 没有返回最新版本地址。");
        var tagName = ParseReleaseTag(releasePageUri);
        var latestVersion = AppReleaseVersion.Parse(tagName);
        if (!latestVersion.IsNewerThan(installedVersion))
        {
            return ApplicationUpdateCheck.Current(latestVersion.ToString());
        }

        var version = latestVersion.ToString();
        var installerName = $"QuickSearch-Setup-v{version}.exe";
        var checksumName = $"{installerName}.sha256";
        var escapedTag = Uri.EscapeDataString(tagName);
        var installerUri = BuildGitHubUri(
            $"releases/download/{escapedTag}/{Uri.EscapeDataString(installerName)}");
        var checksumUri = BuildGitHubUri(
            $"releases/download/{escapedTag}/{Uri.EscapeDataString(checksumName)}");

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

    private Uri BuildGitHubUri(string relativePath) => new(
        $"https://github.com/{Uri.EscapeDataString(_owner)}/" +
        $"{Uri.EscapeDataString(_repository)}/{relativePath}");

    private string ParseReleaseTag(Uri releasePageUri)
    {
        if (!string.Equals(
                releasePageUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                releasePageUri.Host,
                "github.com",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("GitHub 最新版本跳转到了不安全的地址。");
        }

        var expectedPrefix =
            $"/{_owner}/{_repository}/releases/tag/";
        if (!releasePageUri.AbsolutePath.StartsWith(
                expectedPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("GitHub 最新版本地址中缺少版本标签。");
        }

        var tagName = Uri.UnescapeDataString(
            releasePageUri.AbsolutePath[expectedPrefix.Length..]);
        if (string.IsNullOrWhiteSpace(tagName) || tagName.Contains('/'))
        {
            throw new InvalidDataException("GitHub 最新版本标签格式无效。");
        }

        return tagName;
    }

    private static void EnsureSuccessfulResponse(
        HttpResponseMessage response,
        string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden
            or System.Net.HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException(
                $"GitHub 暂时限制了更新请求，请稍后再试（{(int)response.StatusCode}）。");
        }

        throw new InvalidOperationException(
            $"{operation}失败：GitHub 返回 HTTP {(int)response.StatusCode}。");
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

}
