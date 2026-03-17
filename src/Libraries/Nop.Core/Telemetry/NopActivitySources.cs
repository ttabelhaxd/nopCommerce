using System.Diagnostics;

namespace Nop.Core.Telemetry
{
    public static class NopActivitySources
    {
        public static readonly ActivitySource OrderProcessing =
            new ActivitySource("NopCommerce.OrderProcessing");
    }
}
