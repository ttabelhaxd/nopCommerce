using Autofac.Extensions.DependencyInjection;
using Nop.Core.Configuration;
using Nop.Core.Infrastructure;
using Nop.Web.Framework.Infrastructure.Extensions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs; 

namespace Nop.Web;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration.AddJsonFile(NopConfigurationDefaults.AppSettingsFilePath, true, true);
        if (!string.IsNullOrEmpty(builder.Environment?.EnvironmentName))
        {
            var path = string.Format(NopConfigurationDefaults.AppSettingsEnvironmentFilePath, builder.Environment.EnvironmentName);
            builder.Configuration.AddJsonFile(path, true, true);
        }
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.ConfigureApplicationSettings(builder);

        // ----- Logging with OpenTelemetry -----
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        builder.Logging.AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService("nopcommerce-service", serviceVersion: "1.0.0"));

            // Logs for ASP.NET Core
            options.AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri("http://telemetry_service:4318/v1/logs");
                otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            });

            // Exports logs to console (for debugging)
            options.AddConsoleExporter();

            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
        });

        // ----- Traces and Metrics with OpenTelemetry -----
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.AddSource("nopCommerce.OrderFlow")
                    .AddSource("nopCommerce.Catalog")
                    .SetResourceBuilder(ResourceBuilder.CreateDefault()
                        .AddService("nopCommerce", serviceVersion: "1.0.0"))
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = ctx => 
                            !ctx.Request.Path.StartsWithSegments("/health") &&
                            !ctx.Request.Path.StartsWithSegments("/metrics");
                    })
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation(options =>
                    {
                        options.SetDbStatementForText = true;
                    })
                    .AddProcessor(new SensitiveDataProcessor())
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri("http://telemetry_service:4317"); 
                        options.Protocol = OtlpExportProtocol.Grpc;
                    });
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddMeter("nopCommerce.OrderMetrics")
                       .AddPrometheusExporter();
            });

        var appSettings = Singleton<AppSettings>.Instance;
        var useAutofac = appSettings.Get<CommonConfig>().UseAutofac;

        if (useAutofac)
            builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
        else
        {
            builder.Host.UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = false;
                options.ValidateOnBuild = true;
            });
        }

        builder.Services.ConfigureApplicationServices(builder);

        var app = builder.Build();

        // Prometheus scraping endpoint
        app.UseOpenTelemetryPrometheusScrapingEndpoint();

        app.ConfigureRequestPipeline();
        await app.PublishAppStartedEventAsync();

        await app.RunAsync();
    }
}