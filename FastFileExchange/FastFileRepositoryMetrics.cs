using Prometheus;

namespace FastFileExchange
{
    internal static class FastFileRepositoryMetrics
    {
        public static readonly Counter CreatedFiles = Metrics.CreateCounter("ffx_created_files_total", "Number of file versions that have been created.");
        public static readonly Counter NotFoundFiles = Metrics.CreateCounter("ffx_not_found_files_total", "Number of file versions that were requested but were not found.");
        public static readonly Counter FoundFiles = Metrics.CreateCounter("ffx_found_files_total", "Number of file versions that were requested and found.");
        public static readonly Counter ExpiredFiles = Metrics.CreateCounter("ffx_expired_files_total", "Number of file versions that have expired.");
        public static readonly Counter DeletedFiles = Metrics.CreateCounter("ffx_deleted_files_total", "Number of file versions that have been explicitly deleted.");
        public static readonly Counter OverwrittenFiles = Metrics.CreateCounter("ffx_overwritten_files_total", "Number of file versions that have been overwritten by a newer version.");
    }
}
