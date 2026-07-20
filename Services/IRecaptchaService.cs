namespace WorldLinkMaster.Web.Services;

public interface IRecaptchaService
{
    Task<bool> VerifyAsync(string? token);
}
