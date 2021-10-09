using Prometheus;

namespace FastFileExchange
{
    internal static class FastFileRepositoryMetrics
    {
        public static readonly Counter CreatedFiles = Metrics.CreateCounter("ffx_created_file_versions_total", "Number of file versions that have been created.");
        public static readonly Counter NotFoundFiles = Metrics.CreateCounter("ffx_not_found_file_versions_total", "Number of file versions that were requested but were not found.");
        public static readonly Counter FoundFiles = Metrics.CreateCounter("ffx_found_file_versions_total", "Number of file versions that were requested and found.");
        public static readonly Counter ExpiredFiles = Metrics.CreateCounter("ffx_expired_file_versions_total", "Number of file versions that have expired.");
        public static readonly Counter DeletedFiles = Metrics.CreateCounter("ffx_deleted_file_versions_total", "Number of file versions that have been explicitly deleted.");
        public static readonly Counter OverwrittenFiles = Metrics.CreateCounter("ffx_overwritten_file_versions_total", "Number of file versions that have been overwritten by a newer version.");

        public static readonly Gauge StoredFiles = Metrics.CreateGauge("ffx_stored_files", "Number of files that have at least one version loaded in memory.");
    }
}
