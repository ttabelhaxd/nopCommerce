using System.Diagnostics.Metrics;

namespace Nop.Core.Telemetry
{
    public static class TelemetryMetrics
    {
        private static readonly Meter _meter = new Meter("NopCommerce.Custom", "1.0.0");

        // Métricas de negócio
        public static readonly Counter<long> OrdersPlaced =
            _meter.CreateCounter<long>("orders_placed_total", description: "Total number of orders placed");

        public static readonly Counter<long> PaymentFailures =
            _meter.CreateCounter<long>("payment_failures_total", description: "Number of failed payment attempts");

        public static readonly Counter<long> OrderFailures =
            _meter.CreateCounter<long>("order_failures_total", description: "Number of failed orders at any stage");

        // Latências em ms (para histogramas/gráficos)
        public static readonly Histogram<long> BasketValidationDuration =
            _meter.CreateHistogram<long>("basket_validation_duration_ms", "ms",
                description: "Time taken to validate basket");

        public static readonly Histogram<long> PaymentProcessingDuration =
            _meter.CreateHistogram<long>("payment_processing_duration_ms", "ms",
                description: "Time taken to process payment");

        public static readonly Histogram<long> OrderEndToEndDuration =
            _meter.CreateHistogram<long>("order_end_to_end_duration_ms", "ms",
                description: "End-to-end time from order start to completion");
    }
}
