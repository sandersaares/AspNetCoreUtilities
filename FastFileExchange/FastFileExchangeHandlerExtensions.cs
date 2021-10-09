using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FastFileExchange
{
    public static class FastFileExchangeHandlerExtensions
    {
        public static void RunFastFileExchange(this IApplicationBuilder app, FastFileExchangeHandlerOptions? options = null)
        {
            options ??= FastFileExchangeHandlerOptions.Default;

            var logger = app.ApplicationServices.GetService<ILoggerFactory>()!.CreateLogger<FastFileExchangeHandler>();

            var exchange = new FastFileExchangeHandler(options, logger);

            app.Map("/files", x => x.Run(exchange.HandleFileRequestAsync));
            app.Map("/diagnostics", x => x.Run(exchange.HandleDiagnosticsRequestAsync));
        }
    }
}
