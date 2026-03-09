using System.Diagnostics;

namespace Nop.Core.Telemetry
{
    public static class NopActivitySources
    {
        public static readonly ActivitySource Orders =
            new ActivitySource("NopCommerce.Custom.Orders");

        public static readonly ActivitySource Basket =
            new ActivitySource("NopCommerce.Custom.Basket");

        public static readonly ActivitySource Payment =
            new ActivitySource("NopCommerce.Custom.Payment");
    }
}
