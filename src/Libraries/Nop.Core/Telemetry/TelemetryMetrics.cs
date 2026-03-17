using System.Diagnostics.Metrics;

namespace Nop.Core.Telemetry
{
    public static class TelemetryMetrics
    {
        private static readonly Meter _meter = new Meter("NopCommerce.Custom", "1.0.0");

        public static readonly Counter<long> OrdersPlaced = _meter.CreateCounter<long>("orders_placed_total", description: "Total number of orders placed");
        public static readonly Counter<long> ProductsViewed = _meter.CreateCounter<long>("products_viewed_total", description: "Total number of products viewed");
        public static readonly Counter<long> PaymentsProcessed = _meter.CreateCounter<long>("payments_processed_total", description: "Total number of payments processed");
    }
}