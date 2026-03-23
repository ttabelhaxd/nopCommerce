using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Http;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Shipping;
using Nop.Web.Factories;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

using System.Diagnostics;
using Nop.Services;
using Microsoft.Extensions.Logging;

namespace Nop.Web.Controllers;

[AutoValidateAntiforgeryToken]
public partial class OrderController : BasePublicController
{
    #region Fields

    protected readonly ICustomerService _customerService;
    protected readonly ILocalizationService _localizationService;
    protected readonly INotificationService _notificationService;
    protected readonly IOrderModelFactory _orderModelFactory;
    protected readonly IOrderProcessingService _orderProcessingService;
    protected readonly IOrderService _orderService;
    protected readonly IPaymentService _paymentService;
    protected readonly IPdfService _pdfService;
    protected readonly IShipmentService _shipmentService;
    protected readonly IWebHelper _webHelper;
    protected readonly IWorkContext _workContext;
    protected readonly OrderSettings _orderSettings;
    protected readonly RewardPointsSettings _rewardPointsSettings;
    private readonly ILogger<OrderController> _loggerMsft;

    #endregion

    #region Ctor

    public OrderController(ICustomerService customerService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IOrderModelFactory orderModelFactory,
        IOrderProcessingService orderProcessingService,
        IOrderService orderService,
        IPaymentService paymentService,
        IPdfService pdfService,
        IShipmentService shipmentService,
        IWebHelper webHelper,
        IWorkContext workContext,
        OrderSettings orderSettings,
        RewardPointsSettings rewardPointsSettings,
        ILogger<OrderController> loggerMsft)
    {
        _customerService = customerService;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _orderModelFactory = orderModelFactory;
        _orderProcessingService = orderProcessingService;
        _orderService = orderService;
        _paymentService = paymentService;
        _pdfService = pdfService;
        _shipmentService = shipmentService;
        _webHelper = webHelper;
        _workContext = workContext;
        _orderSettings = orderSettings;
        _rewardPointsSettings = rewardPointsSettings;
        _loggerMsft = loggerMsft;
    }

    #endregion

    #region Methods

    //My account / Orders
    public virtual async Task<IActionResult> CustomerOrders(int? pageNumber, OrderHistoryPeriods limit)
    {
        // SPAN
        using var activity = NopActivitySources.ActivitySource.StartActivity("Order.CustomerOrders", ActivityKind.Server);
        _loggerMsft.LogInformation("CustomerOrders accessed. PageNumber: {PageNumber}, Limit: {Limit}", pageNumber, limit);

        try
        {
            if (!await _customerService.IsRegisteredAsync(await _workContext.GetCurrentCustomerAsync()))
                return Challenge();

            var model = await _orderModelFactory.PrepareCustomerOrderListModelAsync(pageNumber, limit);
            return View(model);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);
            _loggerMsft.LogError(ex, "Error loading customer orders");
            throw;
        }
    }

    //My account / Recurring payments
    public virtual async Task<IActionResult> CustomerRecurringPayments()
    {
        if (!await _customerService.IsRegisteredAsync(await _workContext.GetCurrentCustomerAsync()))
            return Challenge();

        var model = await _orderModelFactory.PrepareCustomerRecurringPaymentListModelAsync();
        return View(model);
    }

    //My account / Orders / Cancel recurring order
    [HttpPost, ActionName("CustomerRecurringPayments")]
    [FormValueRequired(FormValueRequirement.StartsWith, "cancelRecurringPayment")]
    public virtual async Task<IActionResult> CancelRecurringPayment(IFormCollection form)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!await _customerService.IsRegisteredAsync(customer))
            return Challenge();

        //get recurring payment identifier
        var recurringPaymentId = 0;
        foreach (var formValue in form.Keys)
        {
            if (formValue.StartsWith("cancelRecurringPayment", StringComparison.InvariantCultureIgnoreCase))
                recurringPaymentId = Convert.ToInt32(formValue["cancelRecurringPayment".Length..]);
        }

        var recurringPayment = await _orderService.GetRecurringPaymentByIdAsync(recurringPaymentId);
        if (recurringPayment == null)
            return RedirectToRoute(NopRouteNames.Standard.CUSTOMER_RECURRING_PAYMENTS);

        if (await _orderProcessingService.CanCancelRecurringPaymentAsync(customer, recurringPayment))
        {
            var errors = await _orderProcessingService.CancelRecurringPaymentAsync(recurringPayment);

            var model = await _orderModelFactory.PrepareCustomerRecurringPaymentListModelAsync();
            model.RecurringPaymentErrors = errors.ToList();

            return View(model);
        }

        return RedirectToRoute(NopRouteNames.Standard.CUSTOMER_RECURRING_PAYMENTS);
    }

    //My account / Orders / Retry last recurring order
    [HttpPost, ActionName("CustomerRecurringPayments")]
    [FormValueRequired(FormValueRequirement.StartsWith, "retryLastPayment")]
    public virtual async Task<IActionResult> RetryLastRecurringPayment(IFormCollection form)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!await _customerService.IsRegisteredAsync(customer))
            return Challenge();

        //get recurring payment identifier
        var recurringPaymentId = 0;
        if (!form.Keys.Any(formValue => formValue.StartsWith("retryLastPayment", StringComparison.InvariantCultureIgnoreCase) &&
                                        int.TryParse(formValue[(formValue.IndexOf('_') + 1)..], out recurringPaymentId)))
            return RedirectToRoute(NopRouteNames.Standard.CUSTOMER_RECURRING_PAYMENTS);

        var recurringPayment = await _orderService.GetRecurringPaymentByIdAsync(recurringPaymentId);
        if (recurringPayment == null)
            return RedirectToRoute(NopRouteNames.Standard.CUSTOMER_RECURRING_PAYMENTS);

        if (!await _orderProcessingService.CanRetryLastRecurringPaymentAsync(customer, recurringPayment))
            return RedirectToRoute(NopRouteNames.Standard.CUSTOMER_RECURRING_PAYMENTS);

        var errors = await _orderProcessingService.ProcessNextRecurringPaymentAsync(recurringPayment);
        var model = await _orderModelFactory.PrepareCustomerRecurringPaymentListModelAsync();
        model.RecurringPaymentErrors = errors.ToList();

        return View(model);
    }

    //My account / Reward points
    public virtual async Task<IActionResult> CustomerRewardPoints(int? pageNumber)
    {
        if (!await _customerService.IsRegisteredAsync(await _workContext.GetCurrentCustomerAsync()))
            return Challenge();

        if (!_rewardPointsSettings.Enabled)
            return RedirectToRoute(NopRouteNames.General.CUSTOMER_INFO);

        var model = await _orderModelFactory.PrepareCustomerRewardPointsAsync(pageNumber);
        return View(model);
    }

    //My account / Order details page
    public virtual async Task<IActionResult> Details(int orderId)
    {
        // SPAN
        using var activity = NopActivitySources.ActivitySource.StartActivity("Order.Details", ActivityKind.Server);
        activity?.SetTag("order.id", orderId);
        _loggerMsft.LogInformation("Loading order details for OrderId: {OrderId}", orderId);

        try
        {
            var order = await _orderService.GetOrderByIdAsync(orderId);
            var customer = await _workContext.GetCurrentCustomerAsync();

            if (order == null || order.Deleted || customer.Id != order.CustomerId)
            {
                _loggerMsft.LogWarning("Order details requested for non-existent or unauthorized order. OrderId: {OrderId}, CustomerId: {CustomerId}", orderId, customer.Id);
                return Challenge();
            }

            var model = await _orderModelFactory.PrepareOrderDetailsModelAsync(order);
            return View(model);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);
            _loggerMsft.LogError(ex, "Error loading order details for OrderId {OrderId}", orderId);
            throw;
        }
    }

    //My account / Order details page / Print
    public virtual async Task<IActionResult> PrintOrderDetails(int orderId)
    {
        var order = await _orderService.GetOrderByIdAsync(orderId);
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (order == null || order.Deleted || customer.Id != order.CustomerId)
            return Challenge();

        var model = await _orderModelFactory.PrepareOrderDetailsModelAsync(order);
        model.PrintMode = true;

        return View("Details", model);
    }

    //My account / Order details page / PDF invoice
    [CheckLanguageSeoCode(ignore: true)]
    public virtual async Task<IActionResult> GetPdfInvoice(int orderId)
    {
        var order = await _orderService.GetOrderByIdAsync(orderId);
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (order == null || order.Deleted || customer.Id != order.CustomerId)
            return Challenge();

        byte[] bytes;
        await using (var stream = new MemoryStream())
        {
            await _pdfService.PrintOrderToPdfAsync(stream, order, await _workContext.GetWorkingLanguageAsync());
            bytes = stream.ToArray();
        }
        return File(bytes, MimeTypes.ApplicationPdf, string.Format(await _localizationService.GetResourceAsync("PDFInvoice.FileName"), order.CustomOrderNumber) + ".pdf");
    }

    public async Task<IActionResult> CancelOrder(int orderId)
    {
        // SPAN
        using var activity = NopActivitySources.ActivitySource.StartActivity("Order.Cancel", ActivityKind.Server);
        activity?.SetTag("order.id", orderId);
        _loggerMsft.LogInformation("Attempting to cancel order {OrderId}", orderId);

        if(!_orderSettings.AllowCustomersCancelOrders)
            return RedirectToRoute(NopRouteNames.Standard.ORDER_DETAILS, new { orderId });

        var order = await _orderService.GetOrderByIdAsync(orderId);
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (order == null || customer.Id != order.CustomerId)
            return Challenge();
        
        try
        {
            if (_orderProcessingService.CanCancelOrder(order))
                await _orderProcessingService.CancelOrderAsync(order, false);
        }
        catch
        {
            _loggerMsft.LogError("Failed to cancel order {OrderId} for customer {CustomerId}", orderId, customer.Id);
            _notificationService.ErrorNotification(await _localizationService.GetResourceAsync("Order.Cancel.Failed"));
            return RedirectToRoute(NopRouteNames.Standard.ORDER_DETAILS, new { orderId });
        }

        await _orderService.UpdateOrderAsync(order);
        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Order.Cancelled"));
        _loggerMsft.LogInformation("Order {OrderId} cancelled successfully", orderId);

        return RedirectToRoute(NopRouteNames.Standard.ORDER_DETAILS, new { orderId });
    }

    //My account / Order details page / re-order
    public virtual async Task<IActionResult> ReOrder(int orderId)
    {
        // SPAN
        using var activity = NopActivitySources.ActivitySource.StartActivity("Order.ReOrder", ActivityKind.Server);
        activity?.SetTag("order.id", orderId);
        _loggerMsft.LogInformation("Re-order requested for order {OrderId}", orderId);

        try
        {
            var order = await _orderService.GetOrderByIdAsync(orderId);
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (order == null || order.Deleted || customer.Id != order.CustomerId)
                return Challenge();

            var warnings = await _orderProcessingService.ReOrderAsync(order);

            if (warnings.Any())
            {
                _loggerMsft.LogWarning("Re-order warnings for order {OrderId}: {Warnings}", orderId, string.Join("; ", warnings));
                _notificationService.WarningNotification(await _localizationService.GetResourceAsync("ShoppingCart.ReorderWarning"));
            }

            return RedirectToRoute(NopRouteNames.General.CART);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);
            _loggerMsft.LogError(ex, "Error re-ordering for OrderId {OrderId}", orderId);
            throw;
        }
    }

    //My account / Order details page / Complete payment
    [HttpPost, ActionName("Details")]
    [FormValueRequired("repost-payment")]
    public virtual async Task<IActionResult> RePostPayment(int orderId)
    {
        // SPAN
        using var activity = NopActivitySources.ActivitySource.StartActivity("Order.RePostPayment", ActivityKind.Server);
        activity?.SetTag("order.id", orderId);
        _loggerMsft.LogInformation("Repost payment for order {OrderId}", orderId);

        try
        {
            var order = await _orderService.GetOrderByIdAsync(orderId);
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (order == null || order.Deleted || customer.Id != order.CustomerId)
                return Challenge();

            if (!await _paymentService.CanRePostProcessPaymentAsync(order))
                return RedirectToRoute(NopRouteNames.Standard.ORDER_DETAILS, new { orderId = orderId });

            var postProcessPaymentRequest = new PostProcessPaymentRequest
            {
                Order = order
            };
            await _paymentService.PostProcessPaymentAsync(postProcessPaymentRequest);

            if (_webHelper.IsRequestBeingRedirected || _webHelper.IsPostBeingDone)
                return new EmptyResult();

            return RedirectToRoute(NopRouteNames.Standard.ORDER_DETAILS, new { orderId = orderId });
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);
            _loggerMsft.LogError(ex, "Error reposting payment for OrderId {OrderId}", orderId);
            throw;
        }
    }

    //My account / Order details page / Shipment details page
    public virtual async Task<IActionResult> ShipmentDetails(int shipmentId)
    {
        var shipment = await _shipmentService.GetShipmentByIdAsync(shipmentId);
        if (shipment == null)
            return Challenge();

        var order = await _orderService.GetOrderByIdAsync(shipment.OrderId);
        var customer = await _workContext.GetCurrentCustomerAsync();

        if (order == null || order.Deleted || customer.Id != order.CustomerId)
            return Challenge();

        var model = await _orderModelFactory.PrepareShipmentDetailsModelAsync(shipment);
        return View(model);
    }

    #endregion
}