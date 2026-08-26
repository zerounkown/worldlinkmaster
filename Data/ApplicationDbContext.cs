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
    public DbSet<Subcategory> Subcategories => Set<Subcategory>();
    public DbSet<Feature> Features => Set<Feature>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Color> Colors => Set<Color>();
    public DbSet<Size> Sizes => Set<Size>();
    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<MerchantPayout> MerchantPayouts => Set<MerchantPayout>();
    public DbSet<WholesaleAccount> WholesaleAccounts => Set<WholesaleAccount>();
    public DbSet<PromoEvent> PromoEvents => Set<PromoEvent>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponRedemption> CouponRedemptions => Set<CouponRedemption>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ProcessedStripeEvent> ProcessedStripeEvents => Set<ProcessedStripeEvent>();
    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<QuoteRequest> QuoteRequests => Set<QuoteRequest>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<SizeGroup> SizeGroups => Set<SizeGroup>();
    public DbSet<AttributeDefinition> AttributeDefinitions => Set<AttributeDefinition>();
    public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();
    public DbSet<ProductColor> ProductColors => Set<ProductColor>();
    public DbSet<ProductMedia> ProductMedia => Set<ProductMedia>();
    public DbSet<HomeBanner> HomeBanners => Set<HomeBanner>();
    public DbSet<CompareItem> CompareItems => Set<CompareItem>();
    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();
    public DbSet<Store> Stores => Set<Store>();

    // Postgres' default "timestamp" column type has no timezone and rejects DateTime
    // values with Kind=Utc (which DateTime.UtcNow — used throughout this codebase — always
    // has). Mapping every DateTime to "timestamptz" avoids that mismatch at the source.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<DateTime>().HaveColumnType("timestamptz");
        configurationBuilder.Properties<DateTime?>().HaveColumnType("timestamptz");
    }

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
            .HasIndex(p => p.VendorSku);

        // Search now matches on Sku (exact-match relevance tier, plus partial ILIKE), which had
        // no index at all before — ProductVariant.Sku already had one, this closes the gap on
        // the Product side. Not unique: the SKU audit found zero duplicates in production today,
        // but nothing currently enforces that at the DB level, so a plain index rather than a
        // uniqueness constraint to avoid silently changing that guarantee.
        builder.Entity<Product>()
            .HasIndex(p => p.Sku);

        builder.Entity<Product>()
            .HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Subcategory>()
            .HasIndex(s => s.Slug)
            .IsUnique();

        builder.Entity<Subcategory>()
            .HasOne(s => s.Category)
            .WithMany(c => c.Subcategories)
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Product>()
            .HasOne(p => p.Subcategory)
            .WithMany(s => s.Products)
            .HasForeignKey(p => p.SubcategoryId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

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

        builder.Entity<ProductVariant>()
            .HasIndex(v => v.Sku)
            .IsUnique();

        builder.Entity<ProductVariant>()
            .HasOne(v => v.Product)
            .WithMany(p => p.Variants)
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProductVariant>()
            .HasOne(v => v.Color)
            .WithMany()
            .HasForeignKey(v => v.ColorId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProductVariant>()
            .HasOne(v => v.Size)
            .WithMany()
            .HasForeignKey(v => v.SizeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Prevents the same product from having two variant rows for the same color+size combo.
        builder.Entity<ProductVariant>()
            .HasIndex(v => new { v.ProductId, v.ColorId, v.SizeId })
            .IsUnique();

        builder.Entity<Product>()
            .HasMany(p => p.Features)
            .WithMany(f => f.Products)
            .UsingEntity(j => j.ToTable("ProductFeatures"));

        builder.Entity<Favorite>()
            .HasIndex(f => new { f.UserId, f.ProductId })
            .IsUnique();

        builder.Entity<Favorite>()
            .HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Favorite>()
            .HasOne(f => f.Product)
            .WithMany()
            .HasForeignKey(f => f.ProductId)
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
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Coupon>()
            .HasOne(c => c.Order)
            .WithMany()
            .HasForeignKey(c => c.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CouponRedemption>()
            .HasOne(r => r.Coupon)
            .WithMany(c => c.Redemptions)
            .HasForeignKey(r => r.CouponId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CouponRedemption>()
            .HasIndex(r => new { r.CouponId, r.UserId })
            .IsUnique();

        builder.Entity<RefreshToken>()
            .HasIndex(r => r.Token)
            .IsUnique();

        builder.Entity<RefreshToken>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProcessedStripeEvent>()
            .HasIndex(e => e.EventId)
            .IsUnique();

        builder.Entity<ChatConversation>()
            .HasOne(c => c.Customer)
            .WithMany()
            .HasForeignKey(c => c.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ChatConversation>()
            .HasOne(c => c.AssignedAgent)
            .WithMany()
            .HasForeignKey(c => c.AssignedAgentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ChatMessage>()
            .HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ChatMessage>()
            .HasOne(m => m.SenderUser)
            .WithMany()
            .HasForeignKey(m => m.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<QuoteRequest>()
            .HasIndex(q => q.ConfirmationToken)
            .IsUnique();

        builder.Entity<QuoteRequest>()
            .HasOne(q => q.User)
            .WithMany()
            .HasForeignKey(q => q.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // --- Master Data / Product Import schema (WLM_01_Developer_Specification.xlsx) ---

        builder.Entity<Brand>().HasIndex(b => b.Code).IsUnique();
        builder.Entity<Category>().HasIndex(c => c.Code).IsUnique();
        builder.Entity<Subcategory>().HasIndex(s => s.Code).IsUnique();
        builder.Entity<Color>().HasIndex(c => c.Code).IsUnique();
        builder.Entity<Size>().HasIndex(s => s.Code).IsUnique();
        builder.Entity<SizeGroup>().HasIndex(g => g.Code).IsUnique();
        builder.Entity<AttributeDefinition>().HasIndex(a => a.Code).IsUnique();
        builder.Entity<ProductColor>().HasIndex(pc => pc.Code).IsUnique();
        builder.Entity<ProductVariant>().HasIndex(v => v.Barcode).IsUnique();

        builder.Entity<Size>()
            .HasOne(s => s.SizeGroup)
            .WithMany(g => g.Sizes)
            .HasForeignKey(s => s.SizeGroupId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Product>()
            .HasOne(p => p.SizeGroup)
            .WithMany()
            .HasForeignKey(p => p.SizeGroupId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProductColor>()
            .HasOne(pc => pc.Product)
            .WithMany(p => p.ProductColors)
            .HasForeignKey(pc => pc.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProductColor>()
            .HasOne(pc => pc.Color)
            .WithMany()
            .HasForeignKey(pc => pc.ColorId)
            .OnDelete(DeleteBehavior.Restrict);

        // A product can't offer the same shared Color twice as two different ProductColors.
        builder.Entity<ProductColor>()
            .HasIndex(pc => new { pc.ProductId, pc.ColorId })
            .IsUnique();

        builder.Entity<ProductVariant>()
            .HasOne(v => v.ProductColor)
            .WithMany(pc => pc.Variants)
            .HasForeignKey(v => v.ProductColorId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProductMedia>()
            .HasOne(m => m.Product)
            .WithMany(p => p.Media)
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProductMedia>()
            .HasOne(m => m.ProductColor)
            .WithMany(pc => pc.Media)
            .HasForeignKey(m => m.ProductColorId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProductAttributeValue>()
            .HasOne(v => v.Product)
            .WithMany(p => p.AttributeValues)
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProductAttributeValue>()
            .HasOne(v => v.AttributeDefinition)
            .WithMany()
            .HasForeignKey(v => v.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProductReview>()
            .HasIndex(r => new { r.ProductId, r.UserId })
            .IsUnique();

        builder.Entity<ProductReview>()
            .HasOne(r => r.Product)
            .WithMany()
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProductReview>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
