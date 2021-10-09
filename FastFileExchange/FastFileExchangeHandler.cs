using Koek;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System.Net;

namespace FastFileExchange
{
    internal class FastFileExchangeHandler
    {
        public FastFileExchangeHandler(FastFileExchangeHandlerOptions options)
        {
            _options = options;

            _repository = new FastFileRepository(options);
        }

        private readonly FastFileExchangeHandlerOptions _options;

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
                // TODO: Do we need optimistic retries here, to wait N milliseconds for the file to become available, to overcome jitter? It can help, sometimes.
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await context.Response.BodyWriter.CompleteAsync();
                return;
            }

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.Headers.ContentType = file.ContentType;
            context.Response.Headers.ContentLength = null;

            try
            {
                await file.CopyToAsync(context.Response.BodyWriter, context.RequestAborted);
            }
            catch (IncompleteFileException)
            {
                // Not much we can do here. Abort it un-gracefully to attempt to signal a problem.
                context.Abort();
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
                maxRequestBodySizeFeature.MaxRequestBodySize = 16 * 1024 * 1024 * 64; // TODO: Fixme

            var file = _repository.Create(filePath, context.Request.Headers.ContentType.SingleOrDefault("application/octet-stream"));

            await file.CopyFromAsync(context.Request.BodyReader, context.RequestAborted);

            context.Response.StatusCode = (int)HttpStatusCode.Created;
            await context.Response.BodyWriter.CompleteAsync();
        }

        private async Task HandleDelete(string filePath, HttpContext context)
        {
            _repository.Delete(filePath);

            context.Response.StatusCode = (int)HttpStatusCode.NoContent;
            await context.Response.BodyWriter.CompleteAsync();
        }
    }
}
