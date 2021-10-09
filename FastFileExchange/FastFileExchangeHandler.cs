using Koek;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using System.Net;

namespace FastFileExchange
{
    internal class FastFileExchangeHandler
    {
        public FastFileExchangeHandler(FastFileExchangeHandlerOptions options, ILogger logger)
        {
            _options = options;
            _logger = logger;

            _repository = new FastFileRepository(options);
        }

        private readonly FastFileExchangeHandlerOptions _options;
        private readonly ILogger _logger;

        private static readonly string[] SupportedMethods = new[] { "GET", "POST", "DELETE" };

        public async Task HandleFileRequestAsync(HttpContext context)
        {
            if (!SupportedMethods.Contains(context.Request.Method))
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return;
            }

            // The module is case-insensitive, so consider the lowercase path the authoritative file path.
            var filePath = context.Request.Path.ToString().ToLowerInvariant();

            if (context.Request.Method == "GET")
                await HandleGet(filePath, context);
            else if (context.Request.Method == "POST")
                await HandlePost(filePath, context);
            else if (context.Request.Method == "DELETE")
                await HandleDelete(filePath, context);
            else
                throw new UnreachableCodeException();
        }

        private readonly FastFileRepository _repository;

        private async Task HandleGet(string filePath, HttpContext context)
        {
            // Do not buffer anything - we may be streaming the data live (depending on if the file is already fully uploaded or not).
            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            // Never cache, because the files may be created/overwritten at any time.
            context.Response.Headers.Add("Cache-Control", "no-cache");
            context.Response.Headers.Add("Expires", "-1");

            if (!_repository.TryGet(filePath, out var file))
            {
                _logger.LogDebug("Not found: {filePath}", filePath);

                // TODO: Do we need optimistic retries here, to wait N milliseconds for the file to become available, to overcome jitter? It can help, sometimes.
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await context.Response.BodyWriter.CompleteAsync();
                return;
            }

            _logger.LogDebug("Found: {filePath}", filePath);

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.Headers.ContentType = file.ContentType;
            context.Response.Headers.ContentLength = null;

            try
            {
                await file.CopyToAsync(context.Response.BodyWriter, context.RequestAborted);

                _logger.LogDebug("Provided complete file: {filePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to retrieve file {filePath} with error {error}", filePath, Helpers.Debug.GetAllExceptionMessages(ex));

                if (context.Response.HasStarted)
                {
                    // Not much we can do here. Abort it un-gracefully to attempt to signal a problem.
                    context.Abort();
                }
                else
                {
                    // Not sure what happened but it is probably our fault - client can't mess up downloads very much.
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                }
            }
            finally
            {
                await context.Response.BodyWriter.CompleteAsync();
            }
        }

        private async Task HandlePost(string filePath, HttpContext context)
        {
            var maxRequestBodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            
            if (maxRequestBodySizeFeature != null)
                maxRequestBodySizeFeature.MaxRequestBodySize = 16 * 1024 * 1024;

            var file = _repository.Create(filePath, context.Request.Headers.ContentType.SingleOrDefault("application/octet-stream"));

            try
            {
                await file.CopyFromAsync(context.Request.BodyReader, context.RequestAborted);

                context.Response.StatusCode = (int)HttpStatusCode.Created;
                _logger.LogInformation("Upload completed for {filePath}.", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Upload failed for {filePath} with error {error}", filePath, Helpers.Debug.GetAllExceptionMessages(ex));

                if (context.Response.HasStarted)
                    return; // Nothing we can do anymore.

                // Assume it is client error - with uploads, this is most likely.
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            }
            finally
            {
                await context.Response.BodyWriter.CompleteAsync();
            }
        }

        private async Task HandleDelete(string filePath, HttpContext context)
        {
            try
            {
                _repository.Delete(filePath);

                context.Response.StatusCode = (int)HttpStatusCode.NoContent;

                _logger.LogInformation("Deleted {filePath}.", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete failed for {filePath} with error {error}", filePath, Helpers.Debug.GetAllExceptionMessages(ex));

                if (context.Response.HasStarted)
                    return; // Nothing we can do anymore.

                // Not sure what happened but it is probably our fault - client can't mess up deletes very much.
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            finally
            {
                await context.Response.BodyWriter.CompleteAsync();
            }
        }

        public async Task HandleDiagnosticsRequestAsync(HttpContext context)
        {
            if (context.Request.Method != "GET")
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return;
            }

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "text/plain";

            await _repository.WriteDiagnosticDumpAsync(context.Response.BodyWriter, context.RequestAborted);
        }
    }
}
