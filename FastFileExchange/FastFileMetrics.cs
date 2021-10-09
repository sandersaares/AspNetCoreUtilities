using Prometheus;

namespace FastFileExchange
{
    internal static class FastFileMetrics
    {
        public static readonly Counter CompletedUploads = Metrics.CreateCounter("ffx_completed_uploads_total", "Number of file versions that were fully uploaded.");
        public static readonly Counter CompletedDownloads = Metrics.CreateCounter("ffx_completed_downloads_total", "Number of download requests that were fully satisfied.");
    }
}
