using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductColor> ProductColors => Set<ProductColor>();
    public DbSet<ProductSize> ProductSizes => Set<ProductSize>();
    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<MerchantPayout> MerchantPayouts => Set<MerchantPayout>();
    public DbSet<WholesaleAccount> WholesaleAccounts => Set<WholesaleAccount>();
    public DbSet<PromoEvent> PromoEvents => Set<PromoEvent>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Category>()
            .HasIndex(c => c.Slug)
            .IsUnique();

        builder.Entity<Product>()
            .HasIndex(p => p.Slug)
            .IsUnique();

        builder.Entity<Product>()
            .HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Order>()
            .HasOne(o => o.User)
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<OrderItem>()
            .HasOne(oi => oi.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<OrderItem>()
            .HasOne(oi => oi.Product)
            .WithMany()
            .HasForeignKey(oi => oi.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProductImage>()
            .HasOne(pi => pi.Product)
            .WithMany(p => p.Images)
            .HasForeignKey(pi => pi.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProductColor>()
            .HasOne(pc => pc.Product)
            .WithMany(p => p.Colors)
            .HasForeignKey(pc => pc.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProductSize>()
            .HasOne(ps => ps.Product)
            .WithMany(p => p.Sizes)
            .HasForeignKey(ps => ps.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Merchant>()
            .HasIndex(m => m.Slug)
            .IsUnique();

        builder.Entity<Merchant>()
            .HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Product>()
            .HasOne(p => p.Merchant)
            .WithMany(m => m.Products)
            .HasForeignKey(p => p.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<OrderItem>()
            .HasOne(oi => oi.Merchant)
            .WithMany()
            .HasForeignKey(oi => oi.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MerchantPayout>()
            .HasOne(mp => mp.Order)
            .WithMany()
            .HasForeignKey(mp => mp.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MerchantPayout>()
            .HasOne(mp => mp.Merchant)
            .WithMany()
            .HasForeignKey(mp => mp.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<WholesaleAccount>()
            .HasOne(w => w.User)
            .WithMany()
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Coupon>()
            .HasIndex(c => c.Code)
            .IsUnique();

        builder.Entity<Coupon>()
            .HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Coupon>()
            .HasOne(c => c.Order)
            .WithMany()
            .HasForeignKey(c => c.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<RefreshToken>()
            .HasIndex(r => r.Token)
            .IsUnique();

        builder.Entity<RefreshToken>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
