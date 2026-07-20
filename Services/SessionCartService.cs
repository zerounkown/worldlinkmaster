using System.Text.Json;
using Microsoft.AspNetCore.Http;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Services;

public class SessionCartService : ICartService
{
    private const string CartSessionKey = "Cart";
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SessionCartService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ISession Session => _httpContextAccessor.HttpContext!.Session;

    public List<CartItem> GetCart()
    {
        var json = Session.GetString(CartSessionKey);
        if (string.IsNullOrEmpty(json))
        {
            return new List<CartItem>();
        }

        return JsonSerializer.Deserialize<List<CartItem>>(json) ?? new List<CartItem>();
    }

    private void SaveCart(List<CartItem> cart)
    {
        Session.SetString(CartSessionKey, JsonSerializer.Serialize(cart));
    }

    private static bool Matches(CartItem item, int productId, string? color, string? size)
    {
        return item.ProductId == productId && item.Color == color && item.Size == size;
    }

    public void AddToCart(Product product, int quantity, string? color, string? size, decimal unitPrice)
    {
        var cart = GetCart();
        var existing = cart.FirstOrDefault(i => Matches(i, product.Id, color, size));
        if (existing != null)
        {
            existing.Quantity += quantity;
        }
        else
        {
            cart.Add(new CartItem
            {
                ProductId = product.Id,
                Name = product.Name,
                UnitPrice = unitPrice,
                ImageUrl = product.ImageUrl,
                Quantity = quantity,
                Color = color,
                Size = size,
                MerchantId = product.MerchantId
            });
        }

        SaveCart(cart);
    }

    public void UpdateQuantity(int productId, string? color, string? size, int quantity)
    {
        var cart = GetCart();
        var item = cart.FirstOrDefault(i => Matches(i, productId, color, size));
        if (item == null)
        {
            return;
        }

        if (quantity <= 0)
        {
            cart.Remove(item);
        }
        else
        {
            item.Quantity = quantity;
        }

        SaveCart(cart);
    }

    public void RemoveFromCart(int productId, string? color, string? size)
    {
        var cart = GetCart();
        cart.RemoveAll(i => Matches(i, productId, color, size));
        SaveCart(cart);
    }

    public void ClearCart()
    {
        Session.Remove(CartSessionKey);
    }

    public int GetItemCount()
    {
        return GetCart().Sum(i => i.Quantity);
    }
}
