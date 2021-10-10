using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Text;

namespace FastFileExchange
{
    /// <summary>
    /// Keeps track of files and their versions. Deletes expired data.
    /// </summary>
    internal class FastFileRepository
    {
        public FastFileRepository(FastFileExchangeHandlerOptions options)
        {
            _options = options;

            Task.Run(CleanupTaskAsync);
        }

        private readonly FastFileExchangeHandlerOptions _options;

        /// <summary>
        /// Attempts to get the most recent version of a file.
        /// </summary>
        public bool TryGet(string filePath, [NotNullWhen(returnValue: true)] out FastFile? file)
        {
            if (!_files.TryGetValue(filePath, out var entry))
            {
                FastFileRepositoryMetrics.NotFoundFiles.Inc();
                file = null;
                return false;
            }

            entry.LastAccess = DateTimeOffset.UtcNow;
            entry.IncrementAccessCount();

            file = entry.File;
            FastFileRepositoryMetrics.FoundFiles.Inc();
            return true;
        }

        /// <summary>
        /// Creates a new version of a file. This version will be returned to all new "get" calls, even if it is not yet completely uploaded.
        /// </summary>
        public FastFile Create(string filePath, string contentType)
        {
            var expirationThreshold = _options.PatternSpecificExpirationThresholds
                .Where(pair => pair.Key.IsMatch(filePath))
                .Select(pair => pair.Value)
                .SingleOrDefault(_options.DefaultExpirationThreshold);

            var file = new FastFile(filePath, contentType);
            var entry = new StoredFile(filePath, file, expirationThreshold);

            StoredFile AddCore(string key, StoredFile newEntry) => newEntry;
            StoredFile UpdateCore(string key, StoredFile oldValue, StoredFile newEntry) { FastFileRepositoryMetrics.OverwrittenFiles.Inc(); return newEntry; }
            _files.AddOrUpdate(filePath, AddCore, UpdateCore, entry);

            _files[filePath] = entry;
            FastFileRepositoryMetrics.CreatedFiles.Inc();
            FastFileRepositoryMetrics.StoredFiles.Set(_files.Count);

            return file;
        }

        /// <summary>
        /// Deletes all versions of a file, marking them as unavailable. Ongoing transfers will still finish.
        /// </summary>
        public void Delete(string filePath)
        {
            if (_files.TryRemove(filePath, out _))
            {
                FastFileRepositoryMetrics.DeletedFiles.Inc();
                FastFileRepositoryMetrics.StoredFiles.Set(_files.Count);
            }
        }

        // We only store the most recent version of the file here.
        // Old versions get dropped as references and die as soon as the last request using them ends.
        private readonly ConcurrentDictionary<string, StoredFile> _files = new();

        private sealed record StoredFile(string FilePath, FastFile File, TimeSpan ExpirationThreshold)
        {
            public DateTimeOffset LastAccess { get; set; } = DateTimeOffset.UtcNow;

            public long AccessCount => _accessCount;
            private long _accessCount;
            public void IncrementAccessCount() => Interlocked.Increment(ref _accessCount);

            public DateTimeOffset ExpiresAt => LastAccess + ExpirationThreshold;
        }

        private async Task CleanupTaskAsync()
        {
            while (true)
            {
                var now = DateTimeOffset.UtcNow;

                foreach (var item in _files)
                {
                    if (item.Value.ExpiresAt < now)
                    {
                        if (_files.TryRemove(item))
                            FastFileRepositoryMetrics.ExpiredFiles.Inc();
                    }
                }

                FastFileRepositoryMetrics.StoredFiles.Set(_files.Count);

                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }

        public async Task WriteDiagnosticDumpAsync(HttpContext context)
        {
            var now = DateTimeOffset.UtcNow;

            // We just want an easy to read ordered list.
            var orderedSnapshot = _files.ToList().OrderBy(x => x.Key).ToList();

            foreach (var item in orderedSnapshot)
            {
                var timeToExpiration = item.Value.ExpiresAt - now;

                var link = new Uri(new Uri(UriHelper.GetEncodedUrl(context.Request).TrimEnd('/') + "/"), "../files" + item.Key);

                var line = FormattableString.Invariant($"<a href='{link}'>{item.Key}</a> [{item.Value.File.ContentType}]; length {item.Value.File.Length:N0}; accessed {item.Value.AccessCount:N0} times; expires in {timeToExpiration.TotalSeconds:F2} seconds.<br/>");
                
                await context.Response.WriteAsync(line, context.RequestAborted);
                await context.Response.WriteAsync(Environment.NewLine, context.RequestAborted);
            }

            await context.Response.WriteAsync(FormattableString.Invariant($"Total {_files.Count:N0} stored files."), context.RequestAborted);
        }
    }
}
