using FastFileExchange;
using Koek;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Tests
{
    [TestClass]
    public sealed class FastFileExchangeTests
    {
        [TestMethod]
        public async Task PostThenGet_ReturnsExpectedContent()
        {
            var handler = new FastFileExchangeHandler(FastFileExchangeHandlerOptions.Default, NullLogger.Instance);

            var original = RandomNumberGenerator.GetBytes(TestFileLength);

            var uploadContext = await UploadFileAsync(handler, original);
            Assert.AreEqual((int)HttpStatusCode.Created, uploadContext.Response.StatusCode);

            var (downloadContext, downloadedFile) = await DownloadFileAsync(handler);

            Assert.AreEqual((int)HttpStatusCode.OK, downloadContext.Response.StatusCode);
            CollectionAssert.AreEqual(original, downloadedFile);
        }

        [TestMethod]
        public async Task PostAndGet_InChunks_ReturnsExpectedContent()
        {
            // When we are ready with read status, we signal this to verify status.
            var readCompleted = new AsyncAutoResetEvent(false);

            var handler = new FastFileExchangeHandler(FastFileExchangeHandlerOptions.Default, NullLogger.Instance);

            var original = RandomNumberGenerator.GetBytes(TestFileLength);

            int uploadedBytes = 0;
            int downloadedBytes = 0;

            var uploadTask = UploadFileAsync(handler, original, async uploadStatus =>
            {
                uploadedBytes = uploadStatus.UploadedBytes;

                while (true)
                {
                    // Wait for the ongoing read to complete.
                    await readCompleted.WaitAsync();

                    if (downloadedBytes > uploadedBytes)
                    {
                        throw new UnreachableCodeException();
                    }
                    else if (uploadedBytes == downloadedBytes)
                    {
                        // If we have read all the data, return from the status report to allow more to be written.
                        break;
                    }

                    // If we get here, just wait for more reads to be performed.
                }
            });

            var downloadTask = DownloadFileAsync(handler, downloadStatus =>
            {
                downloadedBytes = downloadStatus.DownloadedBytes;
                readCompleted.Set();

                return Task.CompletedTask;
            });

            await Task.WhenAll(uploadTask, downloadTask);

            var uploadContext = await uploadTask;

            Assert.AreEqual((int)HttpStatusCode.Created, uploadContext.Response.StatusCode);

            var (downloadContext, downloadedFile) = await downloadTask;

            Assert.AreEqual((int)HttpStatusCode.OK, downloadContext.Response.StatusCode);
            CollectionAssert.AreEqual(original, downloadedFile);
        }

        [TestMethod]
        public async Task IncompleteUpload_FailsUploadDownloadAndPreventsNewDownloads()
        {
            var handler = new FastFileExchangeHandler(FastFileExchangeHandlerOptions.Default, NullLogger.Instance);

            var original = RandomNumberGenerator.GetBytes(TestFileLength);

            Task<(TestHttpContext, byte[])>? firstDownload = null;

            try
            {
                await UploadFileAsync(handler, original, async statusReport =>
                {
                    if (firstDownload == null)
                    {
                        var downloadCaughtUp = new AsyncManualResetEvent();

                        // Start the first download while the upload is happening. We expect this download to break with a connection abort.
                        firstDownload = DownloadFileAsync(handler, x => { downloadCaughtUp.Set(); return Task.CompletedTask; });

                        // Wait for the download to catch up and obtain the already-uploaded chunk.
                        await downloadCaughtUp.WaitAsync();
                    }

                    throw new ContractException("Throwing to abort upload in the middle.");
                });
            }
            catch (EndOfStreamException)
            {
                // Expected, just continue and try the download.
            }

            // We expect the first download to have been aborted.
            // However, because we do not do real HTTP here, only fake pipes, there is no such thing as "connection abort" so we cannot detect it.
            // Instead, we look for the "aborted" flag on the HTTP context.
            Assert.IsNotNull(firstDownload);
            var (downloadContext1, downloadedFile1) = await firstDownload;
            Assert.IsTrue(downloadContext1.IsAborted);

            var (downloadContext2, downloadedFile2) = await DownloadFileAsync(handler);

            // A failed upload simply registers as a 404 for new download requests.
            Assert.AreEqual((int)HttpStatusCode.NotFound, downloadContext2.Response.StatusCode);
        }

        [TestMethod]
        public async Task GetNonexistingfile_ReturnsNotFound()
        {
            var handler = new FastFileExchangeHandler(FastFileExchangeHandlerOptions.Default, NullLogger.Instance);
            var (downloadContext, downloadedFile) = await DownloadFileAsync(handler);

            Assert.AreEqual((int)HttpStatusCode.NotFound, downloadContext.Response.StatusCode);
        }

        [TestMethod]
        public async Task DeleteNonexistingfile_Succeeds()
        {
            var handler = new FastFileExchangeHandler(FastFileExchangeHandlerOptions.Default, NullLogger.Instance);
            var deleteContext = await DeleteFileAsync(handler);

            Assert.AreEqual((int)HttpStatusCode.NoContent, deleteContext.Response.StatusCode);
        }

        [TestMethod]
        public async Task Delete_ActuallyDeletes()
        {
            var handler = new FastFileExchangeHandler(FastFileExchangeHandlerOptions.Default, NullLogger.Instance);

            var original = RandomNumberGenerator.GetBytes(TestFileLength);

            var uploadContext = await UploadFileAsync(handler, original);

            Assert.AreEqual((int)HttpStatusCode.Created, uploadContext.Response.StatusCode);

            var deleteContext = await DeleteFileAsync(handler);

            Assert.AreEqual((int)HttpStatusCode.NoContent, deleteContext.Response.StatusCode);

            var (downloadContext, downloadedFile) = await DownloadFileAsync(handler);

            Assert.AreEqual((int)HttpStatusCode.NotFound, downloadContext.Response.StatusCode);
        }

        [TestMethod]
        public async Task PostTwice_Overwrites()
        {
            var handler = new FastFileExchangeHandler(FastFileExchangeHandlerOptions.Default, NullLogger.Instance);

            var original1 = RandomNumberGenerator.GetBytes(TestFileLength);
            var original2 = RandomNumberGenerator.GetBytes(TestFileLength);

            var uploadContext1 = await UploadFileAsync(handler, original1);
            Assert.AreEqual((int)HttpStatusCode.Created, uploadContext1.Response.StatusCode);

            var uploadContext2 = await UploadFileAsync(handler, original2);
            Assert.AreEqual((int)HttpStatusCode.Created, uploadContext2.Response.StatusCode);

            var (downloadContext, downloadedFile) = await DownloadFileAsync(handler);

            Assert.AreEqual((int)HttpStatusCode.OK, downloadContext.Response.StatusCode);
            CollectionAssert.AreEqual(original2, downloadedFile);
        }

        private const string TestFilePath = "/foo/bar.mp4";
        private const string TestContentType = "application/mp4";
        private const int TestFileLength = 1 * 1024 * 1024;
        private const int TestChunkLength = TestFileLength / 8;

        private sealed record UploadStatusReport(int UploadedBytes);

        private async Task<TestHttpContext> UploadFileAsync(FastFileExchangeHandler handler, byte[] file, Func<UploadStatusReport, Task>? statusObserver = null)
        {
            var requestBody = new Pipe();
            var responseBody = new Pipe();

            var context = new TestHttpContext(requestBody.Reader, responseBody.Writer);

            context.Request.Path = TestFilePath;
            context.Request.Method = "POST";
            context.Request.ContentType = TestContentType;

            var writeTask = Task.Run(async delegate
            {
                var totalChunks = file.Length / TestChunkLength;
                if (file.Length % TestChunkLength != 0)
                    totalChunks++;

                for (var position = 0; position < file.Length; position += TestChunkLength)
                {
                    var remaining = file.Length - position;
                    var chunkLength = Math.Min(remaining, TestChunkLength);

                    var chunk = file.AsMemory(position, chunkLength);
                    var writeResult = await requestBody.Writer.WriteAsync(chunk);

                    if (writeResult.IsCompleted || writeResult.IsCanceled)
                        throw new EndOfStreamException("The reader stopped reading the upload stream.");

                    try
                    {
                        if (statusObserver != null)
                            await statusObserver(new UploadStatusReport(position + chunkLength));
                    }
                    catch (ContractException)
                    {
                        // Abort the request.
                        context.RequestAbortedCts.Cancel();

                        // There is a grace period involved so we actually have to wait a bit here to ensure that we do not get "admitted" within the grace period.
                        await Task.Delay(FastFileExchangeHandler.CancelUploadGracePeriod);
                        continue;
                    }
                }

                await requestBody.Writer.CompleteAsync();
            });

            await handler.HandleFileRequestAsync(context);

            await writeTask;

            return context;
        }

        private sealed record DownloadStatusReport(int DownloadedBytes);

        private async Task<(TestHttpContext, byte[])> DownloadFileAsync(FastFileExchangeHandler handler, Func<DownloadStatusReport, Task>? statusObserver = null)
        {
            var requestBody = new Pipe();
            requestBody.Writer.Complete();

            var responseBody = new Pipe();

            var context = new TestHttpContext(requestBody.Reader, responseBody.Writer);
            context.Request.Path = TestFilePath;
            context.Request.Method = "GET";

            var readTask = Task.Run(async delegate
            {
                var buffer = new MemoryStream();
                var reader = responseBody.Reader;

                while (true)
                {
                    var result = await reader.ReadAsync();

                    ((TestHttpResponse)context.Response).HasStartedInternal = true;

                    foreach (var segment in result.Buffer)
                        buffer.Write(segment.Span);

                    reader.AdvanceTo(result.Buffer.End);

                    if (statusObserver != null)
                        await statusObserver(new DownloadStatusReport((int)buffer.Length));

                    if (result.IsCompleted || result.IsCanceled)
                        break;
                }

                await responseBody.Reader.CompleteAsync();

                return buffer.ToArray();
            });

            await handler.HandleFileRequestAsync(context);

            return (context, await readTask);
        }

        private async Task<TestHttpContext> DeleteFileAsync(FastFileExchangeHandler handler)
        {
            var requestBody = new Pipe();
            requestBody.Writer.Complete();

            var responseBody = new Pipe();

            var context = new TestHttpContext(requestBody.Reader, responseBody.Writer);
            context.Request.Path = TestFilePath;
            context.Request.Method = "DELETE";

            await handler.HandleFileRequestAsync(context);

            return context;
        }

        private sealed class TestHttpContext : HttpContext
        {
            public TestHttpContext(PipeReader requestBodyReader, PipeWriter responseBodyWriter)
            {
                RequestAborted = RequestAbortedCts.Token;

                Request = new TestHttpRequest(this, requestBodyReader);
                Response = new TestHttpResponse(this, responseBodyWriter);
            }

            public override IFeatureCollection Features { get; } = new FeatureCollection();
            public override HttpRequest Request { get; }
            public override HttpResponse Response { get; }
            public override CancellationToken RequestAborted { get; set; }

            public CancellationTokenSource RequestAbortedCts = new CancellationTokenSource();

            public override ConnectionInfo Connection => throw new NotImplementedException();
            public override WebSocketManager WebSockets => throw new NotImplementedException();
            public override ClaimsPrincipal User { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public override IDictionary<object, object?> Items { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public override IServiceProvider RequestServices { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public override string TraceIdentifier { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public override ISession Session { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public bool IsAborted { get; private set; }

            public override void Abort()
            {
                IsAborted = true;
            }
        }

        private sealed class TestHttpRequest : HttpRequest
        {
            public TestHttpRequest(HttpContext owner, PipeReader bodyReader)
            {
                HttpContext = owner;
                BodyReader = bodyReader;
            }

            public override HttpContext HttpContext { get; }

            public override string Method { get; set; } = "UNKNOWN";

            public override IHeaderDictionary Headers { get; } = new HeaderDictionary();
            public override long? ContentLength { get; set; }
            public override string? ContentType { get; set; }

            public override PathString Path { get; set; }
            public override PipeReader BodyReader { get; }

            public override string Scheme { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public override bool IsHttps { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public override HostString Host { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public override PathString PathBase { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public override Microsoft.AspNetCore.Http.QueryString QueryString { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public override IQueryCollection Query { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public override string Protocol { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public override IRequestCookieCollection Cookies { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public override Stream Body { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public override bool HasFormContentType => throw new NotImplementedException();

            public override IFormCollection Form { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public override Task<IFormCollection> ReadFormAsync(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }
        }

        private sealed class TestHttpResponse : HttpResponse
        {
            public TestHttpResponse(HttpContext owner, PipeWriter bodyWriter)
            {
                HttpContext = owner;
                BodyWriter = bodyWriter;
            }

            public override HttpContext HttpContext { get; }

            public override int StatusCode { get; set; }
            public override long? ContentLength { get; set; }
            public override string ContentType { get; set; } = "application/octet-stream";

            public override IHeaderDictionary Headers { get; } = new HeaderDictionary();

            public override PipeWriter BodyWriter { get; }

            public override Stream Body { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public override IResponseCookies Cookies => throw new NotImplementedException();

            public override bool HasStarted => HasStartedInternal;

            public bool HasStartedInternal { get; set; }

            public override void OnCompleted(Func<object, Task> callback, object state)
            {
                throw new NotImplementedException();
            }

            public override void OnStarting(Func<object, Task> callback, object state)
            {
                throw new NotImplementedException();
            }

            public override void Redirect(string location, bool permanent)
            {
                throw new NotImplementedException();
            }
        }
    }
}
