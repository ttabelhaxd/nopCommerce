using System.Diagnostics.Metrics;

namespace Nop.Core.Telemetry
{
    public static class TelemetryMetrics
    {
        private static readonly Meter _meter = new Meter("NopCommerce.Custom", "1.0.0");

        // Counters
        public static readonly Counter<long> OrdersPlaced = _meter.CreateCounter<long>("orders_placed_total",
            description: "Total number of orders placed");
        public static readonly Counter<long> OrderFailures = _meter.CreateCounter<long>("order_failures_total",
            description: "Number of failed orders by stage");
        public static readonly Counter<long> PaymentFailures = _meter.CreateCounter<long>("payment_failures_total",
            description: "Number of failed payments");

        // Histograms (latência em ms)
        public static readonly Histogram<long> CheckoutDuration = _meter.CreateHistogram<long>("checkout_duration_ms", "ms",
            description: "End-to-end checkout duration");
        public static readonly Histogram<long> PaymentProcessingDuration = _meter.CreateHistogram<long>("payment_processing_duration_ms", "ms",
            description: "Payment processing duration");
        public static readonly Histogram<long> OrderEndToEndDuration = _meter.CreateHistogram<long>("order_end_to_end_duration_ms", "ms",
            description: "Order processing end-to-end duration");
        public static readonly Histogram<long> BasketValidationDuration = _meter.CreateHistogram<long>("basket_validation_duration_ms", "ms",
            description: "Basket validation duration");
    }
}
