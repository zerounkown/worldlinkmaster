using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Services;

public interface ICartService
{
    List<CartItem> GetCart();
    void AddToCart(Product product, int quantity, string? color, string? size, decimal unitPrice);
    void UpdateQuantity(int productId, string? color, string? size, int quantity);
    void RemoveFromCart(int productId, string? color, string? size);
    void ClearCart();
    int GetItemCount();
}
