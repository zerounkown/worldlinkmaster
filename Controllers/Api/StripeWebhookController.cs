using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Controllers.Api;

/// <summary>
/// Receives Stripe webhook callbacks. This is the authoritative source of truth for payment
/// confirmation — the browser redirect to /Checkout/Success is only a best-effort fallback.
/// </summary>
[ApiController]
[Route("api/stripe")]
[AllowAnonymous]
public class StripeWebhookController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IOrderFulfillmentService _fulfillment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        ApplicationDbContext context,
        IOrderFulfillmentService fulfillment,
        IConfiguration configuration,
        ILogger<StripeWebhookController> logger)
    {
        _context = context;
        _fulfillment = fulfillment;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var webhookSecret = _configuration["Stripe:WebhookSecret"];
        if (string.IsNullOrEmpty(webhookSecret))
        {
            _logger.LogError("Stripe webhook received but Stripe:WebhookSecret is not configured; cannot verify signature.");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        var json = await new StreamReader(Request.Body).ReadToEndAsync();

        Event stripeEvent;
        try
        {
            // Verifies the Stripe-Signature header against the raw payload — rejects forgeries/replays.
            // throwOnApiVersionMismatch:false so events sent at the account's API version (which can
            // differ from the pinned Stripe.net version) are still accepted; we only read stable fields.
            stripeEvent = EventUtility.ConstructEvent(
                json,
                Request.Headers["Stripe-Signature"],
                webhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Rejected Stripe webhook: invalid signature.");
            return BadRequest();
        }

        // Idempotency guard #1: never process the same delivered event twice.
        if (await _context.ProcessedStripeEvents.AnyAsync(e => e.EventId == stripeEvent.Id))
        {
            _logger.LogInformation("Duplicate Stripe event {EventId} ({EventType}) ignored.", stripeEvent.Id, stripeEvent.Type);
            return Ok();
        }

        _logger.LogInformation("Processing Stripe event {EventId} of type {EventType}.", stripeEvent.Id, stripeEvent.Type);

        try
        {
            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    await HandleCheckoutSessionCompletedAsync(stripeEvent);
                    break;
                case "payment_intent.succeeded":
                    await HandlePaymentIntentSucceededAsync(stripeEvent);
                    break;
                default:
                    _logger.LogInformation("No handler for Stripe event type {EventType}; acknowledging.", stripeEvent.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Return 500 (without recording the event) so Stripe retries delivery later.
            _logger.LogError(ex, "Error handling Stripe event {EventId} ({EventType}).", stripeEvent.Id, stripeEvent.Type);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        _context.ProcessedStripeEvents.Add(new ProcessedStripeEvent
        {
            EventId = stripeEvent.Id,
            EventType = stripeEvent.Type
        });
        await _context.SaveChangesAsync();

        return Ok();
    }

    private async Task HandleCheckoutSessionCompletedAsync(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not Session session)
        {
            return;
        }

        if (!string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Checkout session {SessionId} completed but payment status is '{Status}'; not fulfilling.",
                session.Id, session.PaymentStatus);
            return;
        }

        var orderId = ResolveOrderId(session.ClientReferenceId, session.Metadata, session.Id);
        if (orderId == null)
        {
            _logger.LogWarning("Checkout session {SessionId} completed but no OrderId could be resolved.", session.Id);
            return;
        }

        var result = await _fulfillment.FulfillPaidOrderAsync(orderId.Value, session.PaymentIntentId);
        _logger.LogInformation("Webhook fulfillment for order {OrderId}: newlyPaid={NewlyPaid}.", orderId.Value, result.NewlyPaid);
    }

    private async Task HandlePaymentIntentSucceededAsync(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not PaymentIntent intent)
        {
            return;
        }

        // checkout.session.completed normally arrives first and links the PaymentIntent to the order.
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.StripePaymentIntentId == intent.Id);
        if (order == null)
        {
            _logger.LogInformation("payment_intent.succeeded {PaymentIntentId} is not linked to an order (yet); nothing to do.", intent.Id);
            return;
        }

        await _fulfillment.FulfillPaidOrderAsync(order.Id, intent.Id);
    }

    private int? ResolveOrderId(string? clientReferenceId, IDictionary<string, string>? metadata, string sessionId)
    {
        if (int.TryParse(clientReferenceId, out var fromReference))
        {
            return fromReference;
        }

        if (metadata != null && metadata.TryGetValue("OrderId", out var raw) && int.TryParse(raw, out var fromMetadata))
        {
            return fromMetadata;
        }

        var order = _context.Orders.FirstOrDefault(o => o.StripeSessionId == sessionId);
        return order?.Id;
    }
}
