using Prometheus;

namespace FastFileExchange
{
    internal static class FastFileMetrics
    {
        public static readonly Counter FailedUploads = Metrics.CreateCounter("ffx_failed_uploads_total", "Number of file versions that were not fully uploaded.");
        public static readonly Counter CompletedUploads = Metrics.CreateCounter("ffx_completed_uploads_total", "Number of file versions that were fully uploaded.");

        public static readonly Counter FailedDownloads = Metrics.CreateCounter("ffx_failed_downloads_total", "Number of download requests that were not fully completed due to a service-side issue. Downloads aborted by the client are not counted.");
        public static readonly Counter CompletedDownloads = Metrics.CreateCounter("ffx_completed_downloads_total", "Number of download requests that were fully satisfied.");
    }
}
