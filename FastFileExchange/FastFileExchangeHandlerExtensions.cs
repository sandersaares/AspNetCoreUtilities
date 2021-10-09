using Microsoft.AspNetCore.Builder;

namespace FastFileExchange
{
    public static class FastFileExchangeHandlerExtensions
    {
        public static void RunFastFileExchange(this IApplicationBuilder app, FastFileExchangeHandlerOptions? options = null)
        {
            options ??= FastFileExchangeHandlerOptions.Default;

            var exchange = new FastFileExchangeHandler(options);

            app.Map("/files", x => x.Run(exchange.HandleFileRequestAsync));
            app.Map("/diagnostics", x => x.Run(exchange.HandleDiagnosticsRequestAsync));
        }
    }
}
