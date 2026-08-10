namespace WorldLinkMaster.Web.Services;

public interface ICompareService
{
    Task<HashSet<int>> GetCompareProductIdsAsync();
    Task<bool> IsInCompareAsync(int productId);
    Task<int> GetCountAsync();
}
