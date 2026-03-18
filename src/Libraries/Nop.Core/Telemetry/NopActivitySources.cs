using System.Diagnostics;

namespace Nop.Core.Telemetry
{
    public static class NopActivitySources
    {
        public static readonly ActivitySource Checkout = new("Nop.Checkout");
        public static readonly ActivitySource OrderProcessing = new("Nop.OrderProcessing");
        public static readonly ActivitySource Payment = new("Nop.Payment");
    }
}
