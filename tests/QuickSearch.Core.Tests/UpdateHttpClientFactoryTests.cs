using System.Net;
using System.Net.Http;

namespace QuickSearch.Core.Tests;

public sealed class UpdateHttpClientFactoryTests
{
    [Fact]
    public void CreateHandler_UsesSystemProxyForUpdateRequests()
    {
        var proxy = new WebProxy("http://127.0.0.1:7890");

        using var handler = UpdateHttpClientFactory.CreateHandler(proxy);

        Assert.True(handler.UseProxy);
        Assert.Same(proxy, handler.Proxy);
        Assert.Same(CredentialCache.DefaultCredentials, handler.DefaultProxyCredentials);
        Assert.Equal(
            DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            handler.AutomaticDecompression);
    }

    [Fact]
    public void CreateClient_AppliesBoundedUpdateTimeout()
    {
        using var client = UpdateHttpClientFactory.Create(new WebProxy("http://127.0.0.1:7890"));

        Assert.Equal(TimeSpan.FromMinutes(5), client.Timeout);
    }
}
