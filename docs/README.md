# nopCommerce — Observability & Telemetry

This branch adds full observability to nopCommerce's order placement flow using OpenTelemetry, Prometheus, Grafana, Jaeger, and Loki. Every layer of the application is covered — from the HTTP entry point down to individual SQL statements — without modifying any business logic.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Instrumented Flow Diagram](#instrumented-flow-diagram)
3. [Prerequisites](#prerequisites)
4. [Getting Started](#getting-started)
5. [Dashboards & UIs](#dashboards--uis)
6. [Load Testing](#load-testing)
7. [Stack Endpoints](#stack-endpoints)
8. [Project Structure](#project-structure)
9. [Metrics Reference](#metrics-reference)
10. [Teardown](#teardown)

---

## Architecture Overview

nopCommerce uses an Onion Architecture. Instrumentation follows the same layering — spans are started at each layer's entry point, not scattered throughout business logic.

```
┌─────────────────────────────────────────────────────────────────┐
│                         Nop.Web (HTTP)                          │
│         CheckoutController · OrderController                    │
│         ShoppingCartController · ProductController              │
│                                                                 │
│   Auto: AspNetCore instrumentation (root HTTP span per request) │
│   Manual: one span per controller action                        │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                   Nop.Services (Business Logic)                 │
│   OrderProcessingService · ShoppingCartService                  │
│   PaymentService · ProductService                               │
│                                                                 │
│   Manual: sub-spans for each stage of the order pipeline        │
│   Metrics: counters, histograms and durations (see below)       │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Nop.Data (Persistence)                     │
│            Auto: EF Core instrumentation (one span per query)   │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                  SQL Server (nopcommerce_mssql_server)          │
└─────────────────────────────────────────────────────────────────┘
```

**Telemetry routing:**
- Traces → OTLP/gRPC → OTel Collector → Jaeger
- Metrics → OTLP → OTel Collector → Prometheus scrape endpoint → Grafana
- Logs → `ILogger<T>` → OTLP/HTTP → OTel Collector → Loki → Grafana

---

## Instrumented Flow Diagram

Full span hierarchy for a customer placing an order (`POST /checkout/confirm`):

```
[Browser] POST /checkout/confirm
│
├── SPAN  HTTP POST /checkout/confirm          [auto — AspNetCore]
│   └── SPAN  Checkout.ConfirmOrder            [CheckoutController]
│             tags: customer.id, order.flowstage
│             metric: checkout_duration_ms (always recorded, success or failure)
│
│       └── SPAN  ShoppingCart.Get             [ShoppingCartService]
│
│       └── SPAN  OrderProcessing.PlaceOrder   [OrderProcessingService]
│                 tags: customer.id, store.id, order.flow_stage
│                 metric: orders.started++ on entry
│
│           ├── SPAN  OrderProcessing.Basket   [ValidateAndPrepareCartAsync]
│           ├── SPAN  OrderProcessing.Customer [PrepareAndValidateCustomerAsync]
│           ├── SPAN  OrderProcessing.Billing  [PrepareAndValidateBillingAddressAsync]
│           │
│           └── SPAN  OrderProcessing.PlaceOrder.Core
│               │
│               ├── SPAN  OrderProcessing.Payment   [GetProcessPaymentResultAsync]
│               │         metric: order_failures_total{stage=payment} on failure
│               │
│               │   └── SPAN  Payment.Process       [PaymentService]
│               │             tags: payment.method, customer.id, order.total
│               │             metric: orders.failed++ on failure
│               │             metric: payment_failures_total++ on failure
│               │
│               ├── SPAN  OrderProcessing.Inventory [MoveShoppingCartItemsToOrderItemsAsync]
│               │
│               │   └── SPAN  Inventory.Adjust      [ProductService — per order item]
│               │             tags: product.id, quantity.change, inventory.before
│               │             metric: inventory.level recorded
│               │
│               │       └── SPAN  EF Core / SQL — UpdateProduct  [auto — EF Core]
│               │
│               └── metric: orders.placed_total++ on success
│                   metric: order.value (histogram of OrderTotal)
│
└── [All spans exported via OTLP to OTel Collector]
```

### PII Redaction

`SensitiveDataProcessor` intercepts every span at export time and replaces the value of any tag whose key matches `email`, `password`, `credit`, `card`, `address`, `phone`, `ssn`, or `payment` with `[REDACTED]`. This runs before any data reaches Jaeger, Prometheus, or Loki.

---

## Prerequisites

| Dependency | Version | Notes |
|---|---|---|
| Docker Desktop | 4.x+ | Mandatory |
| Docker Compose | v2+ | Bundled with Docker Desktop |
| .NET SDK | 9.0 | Only needed to run or build outside Docker |
| k6 | 0.49+ | Only needed to run the load test locally |

---

## Getting Started

The entire stack — application, database, and observability backend — runs from a single compose file.

**1. Create the Docker network (first time only):**

```bash
docker network create nopcommerce_observability
```

**2. Build and start everything:**

```bash
docker compose up -d --build
```

| Container | Role | Exposed ports |
|---|---|---|
| `nopcommerce` | Application | `80` |
| `nopcommerce_mssql_server` | SQL Server 2019 Express | internal only |
| `opentelemetry_collector` | Receives all telemetry and fans out to backends | `4317`, `4318`, `9464` |
| `jaeger` | Trace storage and UI | `16686`, `14250` |
| `prometheus` | Metrics store | `9090` |
| `grafana` | Dashboards | `3000` |
| `loki` | Log aggregation | `3100` |

Allow 20–30 seconds for containers to become healthy before opening the application.

**3. First-run database setup:**

On first boot, nopCommerce redirects to `http://localhost/install`. Use the connection string below and complete the wizard:

```
Server=nopcommerce_mssql_server;Database=nopCommerce_Database;
User Id=sa;Password=nopCommerce_db_password;TrustServerCertificate=true
```

The store is available at `http://localhost` once setup completes.

---

## Dashboards & UIs

### Grafana — `http://localhost:3000`

Login: `admin` / `admin`

To import the dashboards, go to **Dashboards → Import** and upload each file below, selecting the listed data source when prompted:

| File | Data source |
|---|---|
| `dashboard/metrics_dashboard.json` | Prometheus |
| `dashboard/traces_dashboard.json` | Jaeger |
| `dashboard/logs_dashboard.json` | Loki |

Panels included:

| Panel | Signal |
|---|---|
| Order placement rate | `orders.started` per minute |
| Checkout success rate | `orders.placed_total` / `orders.started` |
| Failures by stage | `order_failures_total` broken down by `flow_stage` |
| Payment failures | `payment_failures_total` |
| Checkout latency (P50/P95/P99) | `checkout_duration_ms` histogram |
| Order value distribution | `order.value` histogram |
| Inventory levels | `inventory.level` by `product.id` |
| HTTP error rate | 4xx/5xx on the checkout endpoint |
| Live log stream | Structured logs from Loki, filterable by level |

### Jaeger — `http://localhost:16686`

Select service `nopCommerce.OrderFlow`, pick an operation (e.g. `Checkout.ConfirmOrder`), and click **Find Traces**. A successful order trace shows the full span hierarchy from HTTP root through to individual SQL queries.

### Prometheus — `http://localhost:9090`

Some useful PromQL queries to get started:

```promql
# Failure rate over 5-minute windows
rate(order_failures_total[5m]) / rate(orders_started_total[5m])

# P99 checkout latency
histogram_quantile(0.99, rate(checkout_duration_ms_bucket[5m]))

# P99 order value
histogram_quantile(0.99, rate(order_value_bucket[5m]))

# Payment failure rate
rate(payment_failures_total[5m])
```

---

## Load Testing

The script at `load_tests/order-flow.js` drives the full checkout flow using [k6](https://k6.io).

**With Docker (no local k6 install):**

```bash
docker run --rm -i --network nopcommerce_observability grafana/k6 run - < load_tests/order-flow.js
```

**Locally:**

```bash
k6 run --vus 20 --duration 2m load_tests/order-flow.js
```

```bash
k6 run --vus 20 --duration 2m load_tests/error-test.js
```

Run the test and open Grafana at `http://localhost:3000` simultaneously — the dashboard metrics respond in real time. Jaeger at `http://localhost:16686` will show individual traces as they arrive.

---

## Stack Endpoints

| Service | URL |
|---|---|
| nopCommerce | http://localhost |
| Grafana | http://localhost:3000 — `admin` / `admin` |
| Jaeger | http://localhost:16686 |
| Prometheus | http://localhost:9090 |
| Loki | http://localhost:3100 |
| OTel Collector (gRPC) | http://localhost:4317 |
| OTel Collector (HTTP) | http://localhost:4318 |
| OTel Collector metrics | http://localhost:9464 |

---

## Project Structure

```
nopCommerce/
├── src/
│   ├── Libraries/
│   │   └── Nop.Services/
│   │       ├── NopActivitySources.cs              # Central definition of ActivitySource,
│   │       │                                      # Meter, and all custom metrics
│   │       ├── Catalog/
│   │       │   └── ProductService.cs              # Inventory.Adjust span + inventory.level
│   │       ├── Orders/
│   │       │   ├── OrderProcessingService.cs      # PlaceOrder span hierarchy + order metrics
│   │       │   └── ShoppingCartService.cs         # ShoppingCart.Get / ShoppingCart.Add spans
│   │       └── Payments/
│   │           └── PaymentService.cs              # Payment.Process span + failure metrics
│   └── Presentation/
│       └── Nop.Web/
│           ├── Program.cs                         # OTel logging + OTLP exporter registration
│           ├── SensitiveDataProcessor.cs          # PII redaction at export time
│           └── Controllers/
│               ├── CheckoutController.cs          # Checkout.ConfirmOrder span + duration metric
│               ├── OrderController.cs             # Order.Details / CustomerOrders / Cancel /
│               │                                  # ReOrder / RePostPayment spans
│               └── ShoppingCartController.cs      # ShoppingCart.AddToCart span
├── load_tests/
│   ├── order-flow.js                              # k6 load test
│   └── error-test.js                              # k6 test for failure scenarios
│
├── docs/
│   ├── dashboards/
│   │   ├── screenshots/                           # Static images for documentation
│   │   ├── metrics_dashboard.json
│   │   ├── logs_dashboard.json
│   │   └── traces_dashboard.json
|   |
│   ├── diagrams/
│   │   └── order_flow_diagram.png                 # Visual representation of the instrumented customer places an order flow
|   |
│   ├── CRITIQUE.md                                # Architectural critique
│   ├── Architecture_Analysis.md                   # Deep dive into code structure and instrumentation
│   └── README.md                                  # Project overview and setup
|
├── docker-compose.yml                             # Full stack in one file
├── otel-config.yml                                # OTel Collector pipeline
└── prometheus.yml                                 # Prometheus scrape config
```

---

## Metrics Reference

| Metric | Type | Why it exists |
|---|---|---|
| `orders.started` | Counter | Counts every entry into `PlaceOrderAsync`. A flatline under load means the checkout pipeline is not being reached at all — useful to distinguish application failures from infrastructure failures. |
| `orders.placed_total` | Counter | Counts successfully persisted orders, labelled with `payment_status`. Together with `orders.started`, this gives the real checkout conversion rate. |
| `orders.failed` | Counter | Incremented inside `PaymentService` on a failed payment response. Isolated from the broader failure counter so a payment gateway alert can fire independently. |
| `order_failures_total` | Counter | Covers all failure modes, labelled with `flow_stage` (`payment`, `order_core`, `lock`) and the specific `reason` or `error_type`. A spike at `lock` stage means burst traffic is hitting the minimum order interval; a spike at `payment` means the gateway is degrading. |
| `payment_failures_total` | Counter | Dedicated counter for payment plugin failures, allowing a payment-only alert independent of other order errors. |
| `checkout_duration_ms` | Histogram | End-to-end checkout latency from `ConfirmOrder` entry to browser redirect, tagged with `stage=confirm.failed` on error. A rising P99 with a stable P50 points to tail latency from a downstream dependency. |
| `order.value` | Histogram | Distribution of `OrderTotal` per completed order. Detects pricing anomalies (unexpected £0.00 orders), tracks revenue throughput, and can back an SLO on checkout basket value. |
| `inventory.level` | Histogram | Stock quantity recorded after each `AdjustInventoryAsync` call, tagged with `product.id`. Low values on high-velocity products signal stockout risk before it becomes visible to customers. |

---

## Teardown

```bash
# Stop all containers, keep data volumes intact
docker compose down

# Stop and wipe everything including volumes
docker compose down -v
docker network rm nopcommerce_observability
```