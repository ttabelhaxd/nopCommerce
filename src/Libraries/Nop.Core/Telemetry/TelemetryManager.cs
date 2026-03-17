using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

namespace Nop.Core.Telemetry
{
    public sealed class TelemetryCoordinator : IDisposable
    {
        private static readonly Lazy<TelemetryCoordinator> _lazyInstance =
            new Lazy<TelemetryCoordinator>(() => new TelemetryCoordinator());

        private readonly TracerProvider _tracerProvider;
        private readonly MeterProvider _meterProvider;
        private bool _isDisposed;

        public static TelemetryCoordinator Current => _lazyInstance.Value;

        private TelemetryCoordinator()
        {
            Console.WriteLine("[Telemetry] Initializing OpenTelemetry...");

            try
            {
                var serviceName = "nopcommerce-service";
                var serviceVersion = "5.0.1";
                var otlpEndpoint = "http://telemetry_service:4317";

                var resource = ResourceBuilder.CreateDefault()
                    .AddService(
                        serviceName: serviceName,
                        serviceVersion: serviceVersion,
                        serviceInstanceId: Environment.MachineName)
                    .AddAttributes(new[]
                    {
                        new KeyValuePair<string, object>("deployment.environment", "production"),
                        new KeyValuePair<string, object>("host.name", Environment.MachineName)
                    });

                Console.WriteLine("[Telemetry] Building TracerProvider...");

                _tracerProvider = Sdk.CreateTracerProviderBuilder()
                    .SetResourceBuilder(resource)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                    })
                    .AddSqlClientInstrumentation(options =>
                    {
                        options.SetDbStatementForText = false;
                        options.RecordException = true;
                        options.EnableConnectionLevelAttributes = true;
                    })
                    .AddSource("Nop.OrderProcessing") 
                    .AddSource("NopCommerce.Custom")
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                        options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    })
                    .Build();

                Console.WriteLine("[Telemetry] TracerProvider ready.");

                Console.WriteLine("[Telemetry] Building MeterProvider...");

                _meterProvider = Sdk.CreateMeterProviderBuilder()
                    .SetResourceBuilder(resource)
                    .AddHttpClientInstrumentation()
                    .AddMeter("NopCommerce.Custom")
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                        options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    })
                    .Build();

                Console.WriteLine("[Telemetry] MeterProvider ready.");
                Console.WriteLine("[Telemetry] OpenTelemetry initialization completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Telemetry] Failed to initialize OpenTelemetry.");
                Console.WriteLine($"[Telemetry] Exception: {ex.Message}");
                Console.WriteLine($"[Telemetry] StackTrace: {ex.StackTrace}");
                throw;
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            Console.WriteLine("[Telemetry] Disposing TelemetryCoordinator...");

            try
            {
                _tracerProvider?.Dispose();
                _meterProvider?.ForceFlush();
                _meterProvider?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Telemetry] Error during dispose: {ex.Message}");
            }
            finally
            {
                _isDisposed = true;
                Console.WriteLine("[Telemetry] TelemetryCoordinator disposed.");
            }
        }
    }
}
