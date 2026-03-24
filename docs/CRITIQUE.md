# CRITIQUE.md

---

## What nopCommerce Made Easy — and What It Fought Back With

### The wins

**DI everywhere, no surprises.** The codebase is wired entirely through ASP.NET Core's container. Adding `ILogger<T>` to `OrderProcessingService`, `PaymentService`, `ShoppingCartService`, and `ProductService` meant one extra constructor parameter per class. `NopActivitySources` being a static class with thread-safe `ActivitySource` and `Meter` instances meant it could be referenced directly without registering anything. No service locator patterns, no static singletons to work around — the container just delivered what was needed.

**The HTTP layer was free.** `Nop.Web` is a standard ASP.NET Core host. Registering `OpenTelemetry.Instrumentation.AspNetCore` produced a root span for every inbound request at zero cost. Every manually-started span in `CheckoutController` or `OrderController` became a child of that root automatically, because the OTel SDK propagates `Activity.Current` through the async call chain without any extra effort. The entire presentation layer needed only one span per action — the framework did the rest.

**EF Core covered the data layer without touching it.** `OpenTelemetry.Instrumentation.EntityFrameworkCore` hooks into EF Core's diagnostic listener and emits a child span for every SQL command. Every `UpdateProductAsync` inside `OrderProcessingService` already shows up as a child of `OrderProcessing.Inventory` in Jaeger. Not a single line of instrumentation code exists in `Nop.Data`.

**`IEventPublisher` is a map of what matters.** Every meaningful state transition in nopCommerce — order placed, payment processed, inventory adjusted — publishes a domain event. That is exactly the list of things worth tracing. If `IEventPublisher` were trace-aware, a single change to one method would cover every side-effect in the system automatically.

### The friction

**`OrderProcessingService` has no internal structure to instrument against.** Over two thousand lines, one class, one primary method that mixes orchestration, validation, payment coordination, inventory reservation, and notification dispatch. There is no natural sub-boundary to hang a span on — every sub-span (`OrderProcessing.Basket`, `OrderProcessing.Billing`, `OrderProcessing.Customer`, `OrderProcessing.Payment`, `OrderProcessing.Inventory`) had to be placed explicitly inside protected helper methods. A more decomposed service class would have produced the same span hierarchy for free.

**Zero telemetry conventions to build on.** nopCommerce ships with no OTel infrastructure at all. Every naming decision — `Service.Operation` vs `service.operation`, what tags are safe to attach, what granularity constitutes signal vs noise — had to be made from scratch. This is not a code cost, it is a design cost, and it is real.

**`IEventPublisher` silently loses trace context.** The event bus resolves and invokes consumers via reflection with no awareness of `Activity.Current`. Any span started inside an `OrderPlacedEvent` consumer — email dispatch, loyalty points — appears in Jaeger as a disconnected root with no parent. The trace for a completed order is structurally incomplete without the consumer side-effects, and there is no error anywhere to signal this. It only becomes visible when you try to follow a trace end-to-end and notice that half the work is missing.

**PII is on every object in the services layer.** `OrderProcessingService` and `PaymentService` pass around objects that carry order totals, customer IDs, payment method identifiers, and billing details side by side. Every tagging decision required checking what else lived on the same object. `SensitiveDataProcessor` handles this at the framework level so the risk does not leak through, but the discipline of checking before tagging cannot be automated away.

---

## What Should Change — and Whether It Is Worth It

**Fix `IEventPublisher` first.** Five lines in a single method in `Nop.Core`. Capture `Activity.Current` before the consumer dispatch loop and restore it inside each invocation:

```csharp
public async Task PublishAsync<T>(T eventMessage)
{
    var parentActivity = Activity.Current;
    var consumers = _subscriptionService.GetSubscriptions<T>();
    foreach (var consumer in consumers)
    {
        Activity.Current = parentActivity;
        await consumer.HandleEventAsync(eventMessage);
    }
}
```

Without this, everything that happens after `PublishAsync(new OrderPlacedEvent(order))` — notifications, downstream integrations, loyalty points — is invisible in traces. This is the highest-value change in the entire codebase for observability, and the cost is negligible.

**Replace `InventoryLevel` histogram with an `ObservableGauge`.** A `Histogram<int>` records the statistical distribution of observed values — appropriate for durations and sizes. Current stock quantity is a point-in-time scalar. A histogram of stock quantities will produce misleading percentile panels in Grafana and will not support alert rules of the form `inventory_level{product_id="42"} < 5`. The right type is `ObservableGauge<int>`, which Prometheus scrapes on demand. This is a one-line change in `NopActivitySources` and a small adjustment to the call site in `ProductService`.

**Move `NopActivitySources` to `Nop.Core`.** Right now it lives in `Nop.Services`, which means any class outside that assembly that needs to record a span or a metric has a dependency on a sibling namespace for something with no business logic in it. `Nop.Core` is where cross-cutting infrastructure already lives. Moving it there costs nothing and makes the dependency direction correct.

**Consolidate the redundant failure counters.** There are currently three failure signals: `orders.failed` (in `PaymentService`), `order_failures_total` with `flow_stage=payment` (in `OrderProcessingService`), and `payment_failures_total` (also in `PaymentService`). The first and third increment on the exact same condition. In practice this means an alert could fire on one counter and not the other for the same event, which creates confusion about which metric to trust. A single `order_failures_total` labelled by stage covers all cases without duplication.

**Do not decompose `OrderProcessingService`.** Splitting it into smaller orchestration units would clean up the span boundaries without explicit sub-spans, but the refactoring risk against two thousand lines of business logic is not justified by the observability gain. The current sub-span structure already provides enough resolution to diagnose failures. The refactoring belongs on a different backlog.

---

## Where the Code Had to Change — and How the Impact Was Minimised

**`Program.cs`** had to be touched to configure the OTLP log exporter. There is no other place to wire `ILogger<T>` → OTel Collector → Loki. The change adds `builder.Logging.AddOpenTelemetry(...)` with an OTLP HTTP exporter. The middleware pipeline and every service registration are identical to the original.

**`Nop.Web.csproj`** received new NuGet package references for OTel instrumentation and exporters. Pure addition, no behavioural change. All packages instrument through middleware and activity listeners — they do not patch method bodies.

**Six service and controller methods** each received a `using var activity = NopActivitySources.ActivitySource.StartActivity(...)` wrapper and structured log calls. The rule applied throughout was: instrument the boundary, not the interior. The wrapper goes at the outermost entry point of the method, before any business logic. Error handlers re-throw unconditionally after marking the span as failed — no error behaviour changes, no control flow changes. If making a method observable required modifying its interior, that was treated as a sign to find a different instrumentation point.