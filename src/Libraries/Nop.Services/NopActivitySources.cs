using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Nop.Services
{
    public static class NopActivitySources
    {
        public const string SERVICE_NAME = "nopCommerce";
        public static readonly ActivitySource ActivitySource = new(SERVICE_NAME + ".OrderFlow");
        public static readonly Meter Meter = new(SERVICE_NAME + ".OrderMetrics");

        // Contadores (Counters)
        public static Counter<int> OrdersStarted = Meter.CreateCounter<int>(
            "orders.started", 
            description: "Number of order placements started");
            
        public static Counter<int> OrdersPlaced = Meter.CreateCounter<int>(
            "orders.placed_total", 
            description: "Total number of orders placed successfully");

        public static Counter<int> OrdersFailed = Meter.CreateCounter<int>(
            "orders.failed", 
            description: "Number of failed order placements");
            
        public static Counter<int> OrderFailures = Meter.CreateCounter<int>(
            "order_failures_total",
            description: "Number of failed orders by stage (payment, validation, etc)");
            
        public static Counter<int> PaymentFailures = Meter.CreateCounter<int>(
            "payment_failures_total",
            description: "Number of failed payments");

        // Histogramas (duração em ms)
        public static Histogram<long> CheckoutDuration = Meter.CreateHistogram<long>(
            "checkout_duration_ms", 
            unit: "ms", 
            description: "End-to-end checkout duration (from confirm button to redirect)");
            
        public static Histogram<long> BasketValidationDuration = Meter.CreateHistogram<long>(
            "basket_validation_duration_ms", 
            unit: "ms", 
            description: "Basket validation duration during order placement");
            
        public static Histogram<long> PaymentProcessingDuration = Meter.CreateHistogram<long>(
            "payment_processing_duration_ms", 
            unit: "ms", 
            description: "Payment processing duration");
            
        public static Histogram<long> OrderEndToEndDuration = Meter.CreateHistogram<long>(
            "order_end_to_end_duration_ms", 
            unit: "ms", 
            description: "Order processing end-to-end duration (entire PlaceOrder method)");
            
        // Valor da encomenda
        public static Histogram<double> OrderValue = Meter.CreateHistogram<double>(
            "order.value", 
            unit: "currency", 
            description: "Total value of placed orders");
            
        // Métrica de inventário (opcional, usada no ProductService)
        public static Histogram<int> InventoryLevel = Meter.CreateHistogram<int>(
            "inventory.level", 
            unit: "items", 
            description: "Current inventory level for products");
    }
}