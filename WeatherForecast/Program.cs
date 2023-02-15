using JustEat.StatsD;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using NLog.Extensions.Logging;
using NLog.Web;

var builder = WebApplication.CreateBuilder(args);


var serviceName = "WeatherForecast";

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddLogging();

var resourceBuilder = ResourceBuilder
    .CreateDefault()
    .AddService(serviceName: serviceName, serviceVersion: "1.0.0");
var meter = new Meter(serviceName);
//var counter = meter.CreateCounter<long>("app.request-counter");

builder.Services.AddStatsD(
    (provider) =>
    {
        var logger = provider.GetRequiredService<ILogger<Program>>();

        return new StatsDConfiguration()
        {
            Host = "otel-collector",
            Port = 8125,
            Prefix = serviceName,
            OnError = ex =>
            {
                logger.LogError("exception while sending statsd metrics: {exception}", ex);
                return true;
            }
        };
    });


builder.Configuration.AddJsonFile("appsettings.json", true, true);

builder.Services.AddOpenTelemetryTracing(
    builder => {
        builder
        .AddConsoleExporter()
        .AddOtlpExporter(opt => {
            opt.Endpoint = new Uri("http://otel-collector:4317");
            opt.Protocol = OtlpExportProtocol.Grpc;
        })
        .AddSource("OtelDemoAspNetCore")
        .SetResourceBuilder(
            resourceBuilder)
        .AddAspNetCoreInstrumentation();
    });

builder.Services.AddOpenTelemetryMetrics(
    b => {
        b.AddOtlpExporter(opt =>
            {
                opt.Endpoint = new Uri("http://otel-collector:4317");
                opt.Protocol = OtlpExportProtocol.Grpc;
            })
            .AddConsoleExporter()
            .AddMeter(meter.Name)
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation();

    });

//
// builder.Host.UseSerilog((ctx, cfg) =>
// {
//     cfg.ReadFrom.Configuration(ctx.Configuration)
//     .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Debug);
// });

builder.Logging
    .ClearProviders()
    .SetMinimumLevel(LogLevel.Trace)
    .AddConfiguration(builder.Configuration.GetSection("Logging"));

NLog.LogManager.Configuration = new NLogLoggingConfiguration(builder.Configuration.GetSection("Logging:NLog"));

builder.WebHost.UseNLog();

builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = HttpLoggingFields.All;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", ([FromServices] ILogger<Program> logger, [FromServices] IStatsDPublisher stats) =>
{
    stats.Increment("weatherforecast.httpget");
    var stopWatch = Stopwatch.StartNew();

    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();

    logger.LogInformation("returning forecast: {forecast}", forecast);

    stopWatch.Stop();

    var statName = "weatherforecast.Success";
    stats.Timing(stopWatch.Elapsed, statName);


    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
