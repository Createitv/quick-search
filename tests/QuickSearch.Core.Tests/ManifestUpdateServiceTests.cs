using System.Net;
using System.Text;

namespace QuickSearch.Core.Tests;

public sealed class ManifestUpdateServiceTests
{
    [Fact]
    public async Task CheckAsync_ReadsCloudflareManifestWithoutGitHubApi()
    {
        Uri? requestedUri = null;
        using var client = new HttpClient(new StubHttpHandler(request =>
        {
            requestedUri = request.RequestUri;
            return Json("""
                {
                  "version": "0.1.2",
                  "releaseNotesUrl": "https://github.com/Createitv/quick-search/releases/tag/v0.1.2",
                  "installerUrl": "https://quick-search-updates.xfy150150.workers.dev/releases/v0.1.2/QuickSearch-Setup-v0.1.2.exe",
                  "checksumUrl": "https://quick-search-updates.xfy150150.workers.dev/releases/v0.1.2/QuickSearch-Setup-v0.1.2.exe.sha256"
                }
                """);
        }));
        var service = new ManifestUpdateService(
            client,
            new Uri("https://quick-search-updates.xfy150150.workers.dev/latest.json"),
            Path.GetTempPath(),
            new FakeInstallerLauncher());

        var result = await service.CheckAsync(AppReleaseVersion.Parse("0.1.1"));

        Assert.Equal(
            "https://quick-search-updates.xfy150150.workers.dev/latest.json",
            requestedUri?.AbsoluteUri);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("0.1.2", result.LatestVersion);
        Assert.Equal(
            "https://quick-search-updates.xfy150150.workers.dev/releases/v0.1.2/QuickSearch-Setup-v0.1.2.exe",
            result.Release?.InstallerUri.AbsoluteUri);
    }

    [Fact]
    public async Task CheckAsync_WhenManifestVersionIsCurrent_DoesNotOfferUpdate()
    {
        using var client = new HttpClient(new StubHttpHandler(_ => Json("""
            {
              "version": "0.1.2",
              "releaseNotesUrl": "https://github.com/Createitv/quick-search/releases/tag/v0.1.2",
              "installerUrl": "https://quick-search-updates.xfy150150.workers.dev/releases/v0.1.2/QuickSearch-Setup-v0.1.2.exe",
              "checksumUrl": "https://quick-search-updates.xfy150150.workers.dev/releases/v0.1.2/QuickSearch-Setup-v0.1.2.exe.sha256"
            }
            """)));
        var service = new ManifestUpdateService(
            client,
            new Uri("https://quick-search-updates.xfy150150.workers.dev/latest.json"),
            Path.GetTempPath(),
            new FakeInstallerLauncher());

        var result = await service.CheckAsync(AppReleaseVersion.Parse("0.1.2"));

        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("0.1.2", result.LatestVersion);
    }

    [Fact]
    public async Task CheckAsync_WhenManifestUsesInsecureInstallerUrl_RejectsManifest()
    {
        using var client = new HttpClient(new StubHttpHandler(_ => Json("""
            {
              "version": "0.1.2",
              "releaseNotesUrl": "https://github.com/Createitv/quick-search/releases/tag/v0.1.2",
              "installerUrl": "http://example.test/QuickSearch-Setup-v0.1.2.exe",
              "checksumUrl": "https://quick-search-updates.xfy150150.workers.dev/releases/v0.1.2/QuickSearch-Setup-v0.1.2.exe.sha256"
            }
            """)));
        var service = new ManifestUpdateService(
            client,
            new Uri("https://quick-search-updates.xfy150150.workers.dev/latest.json"),
            Path.GetTempPath(),
            new FakeInstallerLauncher());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => service.CheckAsync(AppReleaseVersion.Parse("0.1.1")));

        Assert.Contains("HTTPS", exception.Message);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_PublishesInstallerOnlyAfterHashMatches()
    {
        var downloadRoot = Path.Combine(
            Path.GetTempPath(),
            $"quick-search-manifest-update-test-{Guid.NewGuid():N}");
        var release = CreateRelease("0.1.2");
        const string expectedHash =
            "204676736cea68d6411da9d3aa3fab0a5e70b023ba30cd560cfa9c8e7250f4df";
        var handler = new StubHttpHandler(request =>
            request.RequestUri == release.ChecksumUri
                ? Text($"{expectedHash}  QuickSearch-Setup-v0.1.2.exe")
                : Binary("installer-bytes"u8.ToArray()));
        using var client = new HttpClient(handler);
        var service = new ManifestUpdateService(
            client,
            new Uri("https://quick-search-updates.xfy150150.workers.dev/latest.json"),
            downloadRoot,
            new FakeInstallerLauncher());

        try
        {
            var installerPath = await service.DownloadAndVerifyAsync(release);

            Assert.Equal(
                Path.Combine(
                    downloadRoot,
                    "v0.1.2",
                    "QuickSearch-Setup-v0.1.2.exe"),
                installerPath);
            Assert.Equal("installer-bytes", await File.ReadAllTextAsync(installerPath));
        }
        finally
        {
            if (Directory.Exists(downloadRoot))
            {
                Directory.Delete(downloadRoot, recursive: true);
            }
        }
    }

    private static ApplicationRelease CreateRelease(string version) => new(
        version,
        new Uri($"https://github.com/Createitv/quick-search/releases/tag/v{version}"),
        new Uri($"https://quick-search-updates.xfy150150.workers.dev/releases/v{version}/QuickSearch-Setup-v{version}.exe"),
        new Uri($"https://quick-search-updates.xfy150150.workers.dev/releases/v{version}/QuickSearch-Setup-v{version}.exe.sha256"));

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Text(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "text/plain")
    };

    private static HttpResponseMessage Binary(byte[] value) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(value)
    };

    private sealed class StubHttpHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class FakeInstallerLauncher : IInstallerLauncher
    {
        public PlatformOperationResult Launch(string installerPath) =>
            PlatformOperationResult.Succeeded();
    }
}
