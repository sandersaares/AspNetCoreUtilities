using Prometheus;

namespace FastFileExchange
{
    internal static class FastFileMetrics
    {
        public static readonly Counter FailedUploads = Metrics.CreateCounter("ffx_failed_uploads_total", "Number of file versions that were not fully uploaded.");
        public static readonly Counter CompletedUploads = Metrics.CreateCounter("ffx_completed_uploads_total", "Number of file versions that were fully uploaded.");

        public static readonly Counter FailedDownloads = Metrics.CreateCounter("ffx_failed_file_version_downloads_total", "Number of download requests that tried to download a file version that failed to fully upload.");
        public static readonly Counter CompletedDownloads = Metrics.CreateCounter("ffx_completed_downloads_total", "Number of download requests that were fully satisfied.");

        public static readonly Counter CompleteFilesAtDownloadStart = Metrics.CreateCounter("ffx_downloads_started_with_complete_file_total", "Number of download requests that started when the file was already completed.");
        public static readonly Counter IncompleteFilesAtDownloadStart = Metrics.CreateCounter("ffx_downloads_started_with_incomplete_file_total", "Number of download requests that started when the file was not yet completed.");
    }
}
