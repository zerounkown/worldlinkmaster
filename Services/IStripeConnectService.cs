namespace WorldLinkMaster.Web.Services;

public interface IStripeConnectService
{
    Task<string> CreateExpressAccountAsync(string email);
    Task<string> CreateOnboardingLinkAsync(string stripeAccountId, string returnUrl, string refreshUrl);
    Task<bool> IsOnboardingCompleteAsync(string stripeAccountId);
    Task<string> CreateTransferAsync(string destinationAccountId, decimal amountAed, int orderId);
}
