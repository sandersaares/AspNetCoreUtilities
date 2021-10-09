using FastFileExchange;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

var services = builder.Services;

services.AddCors();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseCors(policy =>
{
    policy.AllowAnyOrigin();
    policy.AllowAnyMethod();
    policy.AllowAnyHeader();
});

app.UseRouting();

app.MapMetrics();

app.UseHttpMetrics();

app.Map("/ffx", ffx => ffx.RunFastFileExchange());

app.Run();
