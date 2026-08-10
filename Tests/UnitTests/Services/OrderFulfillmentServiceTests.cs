using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Tests.UnitTests.Services;

public class OrderFulfillmentServiceTests : IDisposable
{
    // FulfillPaidOrderAsync uses ExecuteUpdateAsync, a relational-only bulk-update feature the
    // EF InMemory provider doesn't support at all — a real SQLite in-memory database is needed
    // here (kept open for the test's lifetime, same reasoning as the integration test factory).
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    private ApplicationDbContext CreateContext()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public void Dispose() => _connection.Dispose();

    // OrderItem.MerchantId, OrderItem.ProductId, and Order.UserId are all real foreign keys,
    // enforced by SQLite (unlike the EF InMemory provider, which never enforces FKs at all).
    // Every order in these tests needs: a customer ApplicationUser, a Merchant (which in turn
    // needs its own backing ApplicationUser), a Category, and the Product(s) referenced by the
    // order's line items.
    private static void SeedPrerequisites(ApplicationDbContext context, string customerUserId = "user-1")
    {
        context.Users.Add(new ApplicationUser { Id = customerUserId, UserName = $"{customerUserId}@example.com", Email = $"{customerUserId}@example.com" });

        const string merchantUserId = "merchant-user-1";
        context.Users.Add(new ApplicationUser { Id = merchantUserId, UserName = "merchant1@example.com", Email = "merchant1@example.com" });
        context.Merchants.Add(new Merchant { Id = 1, UserId = merchantUserId, BusinessName = "Test Merchant", Slug = "test-merchant-1" });

        context.Categories.Add(new Category { Id = 1, Name = "Test Category", Slug = "test-category" });
        context.Products.Add(new Product { Id = 1, Name = "Item", Slug = "item-1", Sku = "SKU-1", CategoryId = 1, MerchantId = 1, Price = 100 });
        context.Products.Add(new Product { Id = 2, Name = "Item 2", Slug = "item-2", Sku = "SKU-2", CategoryId = 1, MerchantId = 1, Price = 30 });

        context.SaveChanges();
    }

    private static OrderFulfillmentService CreateService(
        ApplicationDbContext context,
        out Mock<IEmailService> emailMock,
        out Mock<ICouponService> couponMock)
    {
        emailMock = new Mock<IEmailService>();
        couponMock = new Mock<ICouponService>();
        couponMock.Setup(c => c.IssueLoyaltyCouponIfEligibleAsync(It.IsAny<string>())).ReturnsAsync((Coupon?)null);
        var stripeConnectMock = new Mock<IStripeConnectService>();

        return new OrderFulfillmentService(
            context,
            couponMock.Object,
            emailMock.Object,
            stripeConnectMock.Object,
            NullLogger<OrderFulfillmentService>.Instance);
    }

    private static Order NewUnpaidOrder(string userId = "user-1") => new()
    {
        UserId = userId,
        ShippingName = "Test User",
        ShippingAddress = "123 St",
        ShippingCity = "Dubai",
        ShippingState = "Dubai",
        ShippingZip = "00000",
        ShippingPhone = "0500000000",
        Subtotal = 100,
        ShippingCost = 0,
        Total = 100,
        IsPaid = false,
        Items = new List<OrderItem>
        {
            new() { ProductId = 1, ProductName = "Item", UnitPrice = 100, Quantity = 1, LineTotal = 100, MerchantId = 1 }
        }
    };

    [Fact]
    public async Task FulfillPaidOrderAsync_UnpaidOrder_MarksPaidAndReturnsNewlyPaidTrue()
    {
        using var context = CreateContext();
        SeedPrerequisites(context);
        var order = NewUnpaidOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var service = CreateService(context, out var emailMock, out _);

        var result = await service.FulfillPaidOrderAsync(order.Id, "pi_123");

        Assert.True(result.NewlyPaid);
        // ExecuteUpdateAsync (used by FulfillPaidOrderAsync) bypasses the change tracker
        // entirely, so FindAsync would just return the stale, already-tracked `order` instance
        // instead of re-reading the actual row — force a fresh, untracked read instead.
        var reloaded = await context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == order.Id);
        Assert.True(reloaded!.IsPaid);
        Assert.Equal("pi_123", reloaded.StripePaymentIntentId);
    }

    [Fact]
    public async Task FulfillPaidOrderAsync_CalledTwice_SecondCallIsANoOp()
    {
        using var context = CreateContext();
        SeedPrerequisites(context);
        var order = NewUnpaidOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var service = CreateService(context, out var emailMock, out _);

        var first = await service.FulfillPaidOrderAsync(order.Id, "pi_123");
        var second = await service.FulfillPaidOrderAsync(order.Id, "pi_123");

        Assert.True(first.NewlyPaid);
        Assert.False(second.NewlyPaid);
        // Idempotency guarantee: side effects (order confirmation email) must fire exactly once,
        // even though the webhook and the browser success-page redirect can both call this method
        // for the same order in a race.
        emailMock.Verify(e => e.SendOrderConfirmationAsync(It.IsAny<Order>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task FulfillPaidOrderAsync_NonexistentOrder_ReturnsNewlyPaidFalse()
    {
        using var context = CreateContext();
        var service = CreateService(context, out _, out _);

        var result = await service.FulfillPaidOrderAsync(orderId: 999, paymentIntentId: "pi_x");

        Assert.False(result.NewlyPaid);
    }

    [Fact]
    public async Task FulfillPaidOrderAsync_AlreadyPaidOrder_DoesNotReclaim()
    {
        using var context = CreateContext();
        SeedPrerequisites(context);
        var order = NewUnpaidOrder();
        order.IsPaid = true;
        order.StripePaymentIntentId = "pi_original";
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var service = CreateService(context, out var emailMock, out _);

        var result = await service.FulfillPaidOrderAsync(order.Id, "pi_different");

        Assert.False(result.NewlyPaid);
        // ExecuteUpdateAsync (used by FulfillPaidOrderAsync) bypasses the change tracker
        // entirely, so FindAsync would just return the stale, already-tracked `order` instance
        // instead of re-reading the actual row — force a fresh, untracked read instead.
        var reloaded = await context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == order.Id);
        // The original PaymentIntent id must not be overwritten by a second, unrelated attempt.
        Assert.Equal("pi_original", reloaded!.StripePaymentIntentId);
        emailMock.Verify(e => e.SendOrderConfirmationAsync(It.IsAny<Order>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreatePendingOrderAsync_SumsLineTotalsAndAddsShippingCost()
    {
        using var context = CreateContext();
        var service = CreateService(context, out _, out _);
        var shipping = new CheckoutViewModel
        {
            ShippingName = "Test User",
            ShippingAddress = "123 St",
            ShippingCity = "Dubai",
            ShippingState = "Dubai",
            ShippingZip = "00000",
            ShippingPhone = "0500000000"
        };
        var cartItems = new List<CartItem>
        {
            new() { ProductId = 1, Name = "A", UnitPrice = 50, Quantity = 2, MerchantId = 1 },
            new() { ProductId = 2, Name = "B", UnitPrice = 30, Quantity = 1, MerchantId = 1 }
        };

        SeedPrerequisites(context);
        var order = await service.CreatePendingOrderAsync("user-1", shipping, cartItems);

        Assert.Equal(130m, order.Subtotal); // (50*2) + (30*1)
        Assert.Equal(130m, order.Total); // ShippingCost is 0 when Subtotal >= free-shipping threshold
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.False(order.IsPaid);
        Assert.Equal(2, order.Items.Count);
    }
}
