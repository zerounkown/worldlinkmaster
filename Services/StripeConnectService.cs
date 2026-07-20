using Stripe;

namespace WorldLinkMaster.Web.Services;

public class StripeConnectService : IStripeConnectService
{
    public async Task<string> CreateExpressAccountAsync(string email)
    {
        var service = new AccountService();
        var account = await service.CreateAsync(new AccountCreateOptions
        {
            Type = "express",
            Email = email,
            Capabilities = new AccountCapabilitiesOptions
            {
                CardPayments = new AccountCapabilitiesCardPaymentsOptions { Requested = true },
                Transfers = new AccountCapabilitiesTransfersOptions { Requested = true }
            }
        });

        return account.Id;
    }

    public async Task<string> CreateOnboardingLinkAsync(string stripeAccountId, string returnUrl, string refreshUrl)
    {
        var service = new AccountLinkService();
        var link = await service.CreateAsync(new AccountLinkCreateOptions
        {
            Account = stripeAccountId,
            ReturnUrl = returnUrl,
            RefreshUrl = refreshUrl,
            Type = "account_onboarding"
        });

        return link.Url;
    }

    public async Task<bool> IsOnboardingCompleteAsync(string stripeAccountId)
    {
        var service = new AccountService();
        var account = await service.GetAsync(stripeAccountId);
        return account.DetailsSubmitted && account.ChargesEnabled;
    }

    public async Task<string> CreateTransferAsync(string destinationAccountId, decimal amountAed, int orderId)
    {
        var service = new TransferService();
        var transfer = await service.CreateAsync(new TransferCreateOptions
        {
            Amount = (long)Math.Round(amountAed * 100),
            Currency = "aed",
            Destination = destinationAccountId,
            Description = $"World Link Master order #{orderId}",
            Metadata = new Dictionary<string, string> { ["OrderId"] = orderId.ToString() }
        });

        return transfer.Id;
    }
}
