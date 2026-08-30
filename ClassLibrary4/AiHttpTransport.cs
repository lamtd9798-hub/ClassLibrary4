#nullable disable
using System;
using System.Net;
using System.Net.Http;

namespace ClassLibrary4
{
    /// <summary>
    /// Shared HTTP transport only. Business responsibilities stay in License/Sync/Dataset/
    /// Graph/Export services; this class must never become an AiCloudClient god object.
    /// </summary>
    internal static class AiHttpTransport
    {
        private static readonly Lazy<HttpClient> SharedLazy =
            new Lazy<HttpClient>(CreateClient, isThreadSafe: true);

        public static HttpClient Shared => SharedLazy.Value;

        private static HttpClient CreateClient()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 16,
                ConnectTimeout = TimeSpan.FromSeconds(15)
            };

            return new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(45)
            };
        }
    }
}
