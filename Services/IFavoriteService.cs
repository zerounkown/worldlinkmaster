namespace WorldLinkMaster.Web.Services;

public interface IFavoriteService
{
    Task<HashSet<int>> GetFavoriteProductIdsAsync();
    Task<bool> IsFavoriteAsync(int productId);
    Task<int> GetCountAsync();
}
