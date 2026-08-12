using System.Net;
using System.Text;

namespace QuickSearch.Core.Tests;

public sealed class GitHubReleaseUpdateServiceTests
{
    [Fact]
    public async Task CheckAsync_UsesLatestReleaseRedirectWithoutCallingRateLimitedApi()
    {
        Uri? requestedUri = null;
        using var client = new HttpClient(new StubHttpHandler(request =>
        {
            requestedUri = request.RequestUri;
            return RedirectedRelease("0.0.3");
        }));
        var service = new GitHubReleaseUpdateService(
            client,
            "Createitv",
            "quick-search",
            Path.GetTempPath(),
            new FakeInstallerLauncher());

        var result = await service.CheckAsync(AppReleaseVersion.Parse("0.0.2"));

        Assert.Equal(
            "https://github.com/Createitv/quick-search/releases/latest",
            requestedUri?.AbsoluteUri);
        Assert.DoesNotContain("api.github.com", requestedUri?.Host ?? string.Empty);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("0.0.3", result.LatestVersion);
        Assert.Equal(
            "https://github.com/Createitv/quick-search/releases/download/v0.0.3/QuickSearch-Setup-v0.0.3.exe",
            result.Release?.InstallerUri.AbsoluteUri);
        Assert.Equal(
            "https://github.com/Createitv/quick-search/releases/download/v0.0.3/QuickSearch-Setup-v0.0.3.exe.sha256",
            result.Release?.ChecksumUri.AbsoluteUri);
    }

    [Fact]
    public async Task CheckAsync_WhenInstalledVersionIsCurrent_DoesNotOfferUpdate()
    {
        using var client = new HttpClient(new StubHttpHandler(_ =>
            RedirectedRelease("0.0.8")));
        var service = new GitHubReleaseUpdateService(
            client,
            "Createitv",
            "quick-search",
            Path.GetTempPath(),
            new FakeInstallerLauncher());

        var result = await service.CheckAsync(AppReleaseVersion.Parse("0.0.8"));

        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("0.0.8", result.LatestVersion);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task CheckAsync_WhenGitHubRejectsRequest_UsesFriendlyMessage(
        HttpStatusCode statusCode)
    {
        using var client = new HttpClient(new StubHttpHandler(_ =>
            new HttpResponseMessage(statusCode)));
        var service = new GitHubReleaseUpdateService(
            client,
            "Createitv",
            "quick-search",
            Path.GetTempPath(),
            new FakeInstallerLauncher());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CheckAsync(AppReleaseVersion.Parse("0.0.8")));

        Assert.Contains("GitHub", exception.Message);
        Assert.DoesNotContain("Response status code", exception.Message);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_DeletesTemporaryFileWhenHashDoesNotMatch()
    {
        var downloadRoot = Path.Combine(
            Path.GetTempPath(),
            $"quick-search-update-test-{Guid.NewGuid():N}");
        var release = CreateRelease("0.0.3");
        var handler = new StubHttpHandler(request =>
            request.RequestUri == release.ChecksumUri
                ? Text(new string('0', 64))
                : Binary("not-the-expected-installer"u8.ToArray()));
        using var client = new HttpClient(handler);
        var launcher = new FakeInstallerLauncher();
        var service = new GitHubReleaseUpdateService(
            client,
            "Createitv",
            "quick-search",
            downloadRoot,
            launcher);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.DownloadAndVerifyAsync(release));

            Assert.False(Directory.Exists(downloadRoot)
                && Directory.EnumerateFiles(downloadRoot, "*.exe", SearchOption.AllDirectories).Any());
            Assert.Equal(0, launcher.LaunchCalls);
        }
        finally
        {
            if (Directory.Exists(downloadRoot))
            {
                Directory.Delete(downloadRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_PublishesInstallerOnlyAfterHashMatches()
    {
        var downloadRoot = Path.Combine(
            Path.GetTempPath(),
            $"quick-search-update-test-{Guid.NewGuid():N}");
        var release = CreateRelease("0.0.3");
        const string expectedHash =
            "204676736cea68d6411da9d3aa3fab0a5e70b023ba30cd560cfa9c8e7250f4df";
        var handler = new StubHttpHandler(request =>
            request.RequestUri == release.ChecksumUri
                ? Text($"{expectedHash}  QuickSearch-Setup-v0.0.3.exe")
                : Binary("installer-bytes"u8.ToArray()));
        using var client = new HttpClient(handler);
        var service = new GitHubReleaseUpdateService(
            client,
            "Createitv",
            "quick-search",
            downloadRoot,
            new FakeInstallerLauncher());

        try
        {
            var installerPath = await service.DownloadAndVerifyAsync(release);

            Assert.Equal(
                Path.Combine(
                    downloadRoot,
                    "v0.0.3",
                    "QuickSearch-Setup-v0.0.3.exe"),
                installerPath);
            Assert.Equal("installer-bytes", await File.ReadAllTextAsync(installerPath));
            Assert.Empty(Directory.EnumerateFiles(
                downloadRoot,
                "*.download",
                SearchOption.AllDirectories));
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
        new Uri($"https://github.com/Createitv/quick-search/releases/download/v{version}/QuickSearch-Setup-v{version}.exe"),
        new Uri($"https://github.com/Createitv/quick-search/releases/download/v{version}/QuickSearch-Setup-v{version}.exe.sha256"));

    private static HttpResponseMessage RedirectedRelease(string version) => new(HttpStatusCode.OK)
    {
        RequestMessage = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://github.com/Createitv/quick-search/releases/tag/v{version}")
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
        public int LaunchCalls { get; private set; }

        public PlatformOperationResult Launch(string installerPath)
        {
            LaunchCalls++;
            return PlatformOperationResult.Succeeded();
        }
    }
}
