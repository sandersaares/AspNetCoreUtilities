using Nito.AsyncEx;
using System.IO.Pipelines;

namespace FastFileExchange
{
    /// <summary>
    /// A specific version of a file stored in the FFX. The file may be incomplete.
    /// </summary>
    internal class FastFile
    {
        public FastFile(string filePath, string contentType)
        {
            FilePath = filePath;
            ContentType = contentType;
        }

        public string FilePath { get; }
        public string ContentType { get; }

        public long Length => _content.Length;

        // We use a two-scope synchronization strategy.
        // 1) Reader/Writer lock protects access to the contents themselves, ensuring isolation between reads and writes.
        //    Reader lock is enough to also read state (because reader lock implies no writes are happening).
        // 2) State lock protects the completion/length control variables, and is pulsed when anything changes.
        //    State writes assume that contents write lock is also held.
        //    State reads do not require any other locks to be held (they just release new readers to make new read attempts).
        //    Content reads do not require the state lock to be held, as the reader/writer lock is enough.

        // Content lock must be held for any access to _contents.
        private readonly AsyncReaderWriterLock _contentLock = new();
        private readonly MemoryStream _content = new();

        // Pulsed whenever the state of the content changes.
        // The state can be:
        // 1) Individual variables below.
        // 2) Members of _contents (which also require _contentLock to be held to access).
        private readonly AsyncMonitor _stateLock = new();

        // True if the file has been completely loaded and no more bytes are incoming.
        private bool _isCompleted;

        // True if the file will never become completely loaded because something went wrong with the upload to the file exchange (broken connection).
        private bool _isFailed;

        public async Task CopyFromAsync(PipeReader reader, CancellationToken cancel)
        {
            while (true)
            {
                ReadResult result;

                try
                {
                    result = await reader.ReadAsync(cancel);
                }
                catch
                {
                    // We got cancelled or the read failed for some other reason. Either way, the file is now useless and incomplete. Mark it as such.
                    using (await _stateLock.EnterAsync())
                    {
                        _isFailed = true;

                        // State changed - signal readers.
                        _stateLock.PulseAll();

                        FastFileMetrics.FailedUploads.Inc();
                        throw;
                    }
                }

                using var contentLockHolder = await _contentLock.WriterLockAsync();
                using var stateLockHolder = await _stateLock.EnterAsync();

                try
                {
                    if (!result.Buffer.IsEmpty)
                    {
                        _content.Position = _content.Length;

                        foreach (var segment in result.Buffer)
                            _content.Write(segment.Span);

                        // More data is available - signal readers.
                        _stateLock.PulseAll();
                    }

                    // We need to set this after copying the last buffer, because IsCompleted can be set together with the last buffer.
                    if (result.IsCompleted)
                    {
                        _isCompleted = true;

                        // State changed - signal readers.
                        _stateLock.PulseAll();

                        FastFileMetrics.CompletedUploads.Inc();
                        break;
                    }
                }
                finally
                {
                    reader.AdvanceTo(result.Buffer.End);
                }
            }
        }

        // Sort of arbitrary. We need to balance buffer splitting overhead (if too small, we need many iterations) VS memory overhead (leaving most of it empty).
        private const int DesiredWriteSize = 16 * 1024;

        /// <exception cref="IncompleteFileException">Thrown if the file will never become completely available.</exception>
        public async Task CopyToAsync(PipeWriter writer, CancellationToken cancel)
        {
            using var cancelRegistration = cancel.Register(() => writer.CancelPendingFlush());

            // Important thing here is not to hold any locks while we are actually writing data to the output.
            // We need to copy the data into a buffer, then release any locks, then do the actual write to network.
            long position = 0;

            bool firstIteration = true;

            while (!cancel.IsCancellationRequested)
            {
                // Grab a suitable output buffer.
                var buffer = writer.GetMemory(DesiredWriteSize);

                // Track how many bytes were copied into the buffer, ready to be sent.
                int copiedBytes = 0;

                // If we do not copy all available data, we know there is more data and can immediately proceed without taking a lock to check.
                bool hasMoreDataForSure = false;

                // Fill it with some data. We need only the content lock for read operations.
                using (await _contentLock.ReaderLockAsync(cancel))
                {
                    // Check for failed upload as early as possible, so we do not waste time downloading
                    // 90% of a file just to discover that the last 10% will never be available.
                    if (_isFailed)
                    {
                        FastFileMetrics.FailedDownloads.Inc();
                        throw new IncompleteFileException();
                    }

                    if (firstIteration)
                    {
                        firstIteration = false;

                        if (_isCompleted)
                            FastFileMetrics.CompleteFilesAtDownloadStart.Inc();
                        else
                            FastFileMetrics.IncompleteFilesAtDownloadStart.Inc();
                    }

                    if (_content.Length > position)
                    {
                        // There is new data! Copy it to the buffer.
                        _content.Position = position;
                        copiedBytes = _content.Read(buffer.Span);
                        position += copiedBytes;

                        hasMoreDataForSure = _content.Length != _content.Position;
                    }
                }

                // Send any copied bytes.
                if (copiedBytes != 0)
                {
                    writer.Advance(copiedBytes);
                    var flushResult = await writer.FlushAsync(CancellationToken.None);

                    if (flushResult.IsCompleted || flushResult.IsCanceled)
                    {
                        // The connection is closed or the copy is canceled - nothing for us to do here anymore.
                        // More likely is a connection reset (which will surface as HttpContext.RequestAborted).
                        return;
                    }
                }

                // Wait for more data to arrive.
                if (hasMoreDataForSure)
                    continue; // No need to even wait, we know there is more data.

                using (await _stateLock.EnterAsync())
                {
                    while (true)
                    {
                        if (_content.Length > position)
                            break; // There is more data, go ahead and read it.

                        if (_isCompleted)
                        {
                            FastFileMetrics.CompletedDownloads.Inc();
                            return;
                        }

                        if (_isFailed)
                        {
                            FastFileMetrics.FailedDownloads.Inc();
                            throw new IncompleteFileException();
                        }

                        // There is no more data but we are also not done. Take a sleep until something changes.
                        await _stateLock.WaitAsync(cancel);
                    }
                }
            }
        }
    }
}
