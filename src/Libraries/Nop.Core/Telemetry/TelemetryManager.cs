using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System;

namespace Nop.Core.Telemetry
{
    public sealed class TelemetryManager : IDisposable
    {
        private static readonly Lazy<TelemetryManager> _instance =
            new Lazy<TelemetryManager>(() => new TelemetryManager());

        private readonly TracerProvider _tracerProvider;
        private bool _disposed;

        public static TelemetryManager Instance => _instance.Value;

        private TelemetryManager()
        {
            // Config por environment variables (docker-compose)
            var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") 
                              ?? "nopcommerce-service";
            var serviceVersion = Environment.GetEnvironmentVariable("OTEL_SERVICE_VERSION")
                                ?? "5.0.0";
            var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
                               ?? "http://telemetry_service:4317";
            var samplingRatioEnv = Environment.GetEnvironmentVariable("OTEL_TRACES_SAMPLER_ARG");
            var samplingRatio = 1.0;
            if (!string.IsNullOrWhiteSpace(samplingRatioEnv))
            {
                double.TryParse(samplingRatioEnv, out samplingRatio);
            }

            _tracerProvider = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(
                    ResourceBuilder.CreateDefault()
                        .AddService(
                            serviceName: serviceName,
                            serviceVersion: serviceVersion,
                            serviceInstanceId: Environment.MachineName)
                )
                .SetSampler(new TraceIdRatioBasedSampler(samplingRatio))
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
                .AddSource("NopCommerce.Custom")  // All the sources for the activities
                .AddSource("NopCommerce.Custom.Orders")
                .AddSource("NopCommerce.Custom.Basket")
                .AddSource("NopCommerce.Custom.Payment")
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                    options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                })
                .Build();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _tracerProvider?.Dispose();
                _disposed = true;
            }
        }
    }
}
