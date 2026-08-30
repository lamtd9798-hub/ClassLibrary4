#nullable disable
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ClassLibrary4
{
    /// <summary>
    /// Shared transport with separate business service boundaries.
    /// This intentionally does NOT become another AiCloudClient god object.
    /// Existing clients may migrate one responsibility at a time.
    /// </summary>
    internal abstract class MepCloudServiceBase
    {
        protected HttpClient Http => AiHttpTransport.Shared;

        protected Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default)
        {
            return Http.SendAsync(request, cancellationToken);
        }
    }

    internal sealed class LicenseService : MepCloudServiceBase
    {
        public new Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default)
            => base.SendAsync(request, cancellationToken);
    }

    internal sealed class SyncService : MepCloudServiceBase
    {
        public new Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default)
            => base.SendAsync(request, cancellationToken);
    }

    internal sealed class DatasetService : MepCloudServiceBase
    {
        public new Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default)
            => base.SendAsync(request, cancellationToken);
    }

    internal sealed class GraphService : MepCloudServiceBase
    {
        public new Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default)
            => base.SendAsync(request, cancellationToken);
    }

    internal sealed class ExportService : MepCloudServiceBase
    {
        public new Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default)
            => base.SendAsync(request, cancellationToken);
    }

    internal sealed class MepCloudServices
    {
        public LicenseService License { get; } = new LicenseService();
        public SyncService Sync { get; } = new SyncService();
        public DatasetService Dataset { get; } = new DatasetService();
        public GraphService Graph { get; } = new GraphService();
        public ExportService Export { get; } = new ExportService();
    }
}
