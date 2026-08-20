using System.Net;

namespace QuickSearch.Core;

public static class UpdateHttpClientFactory
{
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromMinutes(5);

    public static HttpClient Create(IWebProxy? systemProxy = null)
    {
        var handler = CreateHandler(systemProxy ?? HttpClient.DefaultProxy);
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = UpdateTimeout
        };
    }

    public static HttpClientHandler CreateHandler(IWebProxy systemProxy)
    {
        ArgumentNullException.ThrowIfNull(systemProxy);
        return new HttpClientHandler
        {
            UseProxy = true,
            Proxy = systemProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            AutomaticDecompression =
                DecompressionMethods.GZip
                | DecompressionMethods.Deflate
                | DecompressionMethods.Brotli,
            AllowAutoRedirect = true
        };
    }
}
