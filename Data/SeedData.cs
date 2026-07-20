using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync();

        var adminUser = await SeedRolesAndAdminAsync(services);
        var defaultMerchant = await SeedDefaultMerchantAsync(context, adminUser);
        await SeedCatalogAsync(context, defaultMerchant.Id);
        await SeedBulkExpansionAsync(context, defaultMerchant.Id);
        await SeedSubcategoriesAsync(context);
        await SeedSubcategoryPlaceholderProductsAsync(context, defaultMerchant.Id);
        await SeedPromoEventsAsync(context);
        await SeedPromoCouponsAsync(context);
    }

    // Real-world subcategory taxonomy supplied by the store owner, matched onto the four
    // existing categories that have an equivalent (Outdoor Equipment has none yet).
    private static readonly Dictionary<string, string[]> SubcategoryTaxonomy = new()
    {
        ["tactical-apparel"] = new[]
        {
            "Tactical Pants", "Tactical Shorts", "Short Sleeve T-Shirt", "Long Sleeve T-Shirt",
            "Combat Shirt", "Short Sleeve Crew Neck T-Shirt", "Long Sleeve Crew Neck T-Shirt",
            "Thermal Base Layers", "Base Layer Underwear", "Tactical Jackets", "Tactical Vests / Plate Carriers"
        },
        ["gear-and-accessories"] = new[]
        {
            "Accessories", "Tactical Gloves", "Holsters", "Tactical Socks", "Tactical Caps", "Magazines",
            "Face Masks / Balaclavas", "Weapon Cases", "Entrenching Tools", "Tactical Helmets", "Tactical Eyewear",
            "Tactical Baton", "Tactical Watches", "Tactical Belts", "Tactical Flashlights", "Tactical Knives",
            "Multi-Tools / Leatherman Tools", "Optics / Binoculars", "Morale Patches", "National Service Gear",
            "First Aid Kits", "Towels & Towelettes", "Camouflage Tape", "Tool Bags", "Weapon Cleaning Kits",
            "Tactical Compass", "Military Folding Shovel", "Military Canteen"
        },
        // Note: source list had "Desert Tactical Boots – Beige" and "Black Tactical Boots" each
        // listed twice; de-duplicated here since identical subcategory names under one category
        // would just show up as confusing duplicate nav links.
        ["footwear"] = new[]
        {
            "Footwear", "Desert Tactical Boots – Beige", "Low Cut Desert Tactical Boots – Beige",
            "Black Tactical Boots", "Low Cut Black Tactical Boots", "Safety Shoes", "Sports Shoes",
            "Formal / Duty Shoes", "Hiking Shoes"
        },
        ["bags-and-packs"] = new[]
        {
            "Bags & Packs", "Backpacks", "Trolley Bags", "Duffel Bags", "Shoulder Bags", "Waist Packs"
        }
    };

    private static async Task SeedSubcategoriesAsync(ApplicationDbContext context)
    {
        if (await context.Subcategories.AnyAsync())
        {
            return;
        }

        var categories = await context.Categories.ToDictionaryAsync(c => c.Slug);
        var usedSlugs = new HashSet<string>();
        var subcategories = new List<Subcategory>();

        foreach (var (categorySlug, names) in SubcategoryTaxonomy)
        {
            if (!categories.TryGetValue(categorySlug, out var category))
            {
                continue;
            }

            foreach (var name in names)
            {
                var slug = Slugify(name);
                var dedupeSuffix = 2;
                while (!usedSlugs.Add(slug))
                {
                    slug = $"{Slugify(name)}-{dedupeSuffix}";
                    dedupeSuffix++;
                }

                subcategories.Add(new Subcategory { Name = name, Slug = slug, Category = category });
            }
        }

        context.Subcategories.AddRange(subcategories);
        await context.SaveChangesAsync();
    }

    private static async Task SeedPromoCouponsAsync(ApplicationDbContext context)
    {
        if (await context.Coupons.AnyAsync(c => c.Source == CouponSource.AdminPromo))
        {
            return;
        }

        var today = DateTime.UtcNow.Date;
        context.Coupons.AddRange(
            new Coupon
            {
                Code = "SUMMER20",
                Description = "Summer sale — 20% off your order",
                DiscountPercent = 20,
                Source = CouponSource.AdminPromo,
                StartsAt = today.AddDays(-7),
                ExpiresAt = today.AddDays(30),
                IsActive = true
            },
            new Coupon
            {
                Code = "WELCOME15",
                Description = "15% off for every shopper",
                DiscountPercent = 15,
                Source = CouponSource.AdminPromo,
                StartsAt = today.AddDays(-1),
                ExpiresAt = today.AddDays(60),
                IsActive = true
            },
            new Coupon
            {
                Code = "FLASH30",
                Description = "Limited flash deal — 30% off (first 100 orders)",
                DiscountPercent = 30,
                Source = CouponSource.AdminPromo,
                StartsAt = today,
                ExpiresAt = today.AddDays(5),
                MaxRedemptions = 100,
                IsActive = true
            }
        );

        await context.SaveChangesAsync();
    }

    private static async Task SeedPromoEventsAsync(ApplicationDbContext context)
    {
        if (await context.PromoEvents.AnyAsync())
        {
            return;
        }

        var today = DateTime.UtcNow.Date;
        context.PromoEvents.AddRange(
            new PromoEvent
            {
                Name = "UAE Summer Surprises",
                Description = "Beat the heat with deep discounts across the whole store.",
                IconEmoji = "☀️",
                DiscountPercent = 20,
                StartDate = today.AddDays(-14),
                EndDate = today.AddDays(30)
            },
            new PromoEvent
            {
                Name = "UAE National Day Sale",
                Description = "Celebrate the Union with our biggest discount of the year.",
                IconEmoji = "\U0001F389",
                DiscountPercent = 30,
                StartDate = new DateTime(today.Year, 11, 29),
                EndDate = new DateTime(today.Year, 12, 3)
            },
            new PromoEvent
            {
                Name = "Dubai Shopping Festival",
                Description = "Weeks of citywide deals — stock up on field-ready gear.",
                IconEmoji = "\U0001F6CD️",
                DiscountPercent = 25,
                StartDate = new DateTime(today.Year, 12, 10),
                EndDate = new DateTime(today.Year + 1, 1, 31)
            },
            new PromoEvent
            {
                Name = "New Year Sale",
                Description = "Ring in the new year with fresh gear at a fresh price.",
                IconEmoji = "\U0001F386",
                DiscountPercent = 20,
                StartDate = new DateTime(today.Year, 12, 30),
                EndDate = new DateTime(today.Year + 1, 1, 2)
            },
            new PromoEvent
            {
                Name = "Ramadan Specials",
                Description = "Special pricing on essentials throughout the holy month.",
                IconEmoji = "\U0001F319",
                DiscountPercent = 15,
                StartDate = new DateTime(today.Year + 1, 2, 17),
                EndDate = new DateTime(today.Year + 1, 3, 19)
            },
            new PromoEvent
            {
                Name = "Eid Al Fitr Sale",
                Description = "Eid Mubarak! Celebrate with a storewide discount.",
                IconEmoji = "\U0001F31F",
                DiscountPercent = 20,
                StartDate = new DateTime(today.Year + 1, 3, 20),
                EndDate = new DateTime(today.Year + 1, 3, 24)
            }
        );

        await context.SaveChangesAsync();
    }

    private static async Task<ApplicationUser> SeedRolesAndAdminAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = services.GetRequiredService<IConfiguration>();
        var environment = services.GetRequiredService<IHostEnvironment>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SeedData");

        foreach (var role in new[] { "Admin", "Customer", "Merchant", "Wholesale", "Support" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        var adminEmail = configuration["SeedAdmin:Email"] ?? "admin@worldlinkmaster.com";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser != null)
        {
            return adminUser;
        }

        adminUser = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            FirstName = "Site",
            LastName = "Admin"
        };

        // The admin password is NEVER hardcoded for production. Precedence:
        //   1. SeedAdmin:Password (env var SeedAdmin__Password) — the supported way to set it.
        //   2. Development only: a well-known local password for convenience.
        //   3. Otherwise: a random, unknown password so no default credential exists; the
        //      operator must set a real one via the password-reset flow (needs SMTP).
        var configuredPassword = configuration["SeedAdmin:Password"];
        string password;
        if (!string.IsNullOrWhiteSpace(configuredPassword))
        {
            password = configuredPassword;
            logger.LogInformation("Seeding admin account {Email} from SeedAdmin:Password.", adminEmail);
        }
        else if (environment.IsDevelopment())
        {
            password = "Admin@12345";
            logger.LogWarning("SeedAdmin:Password not set — using the built-in DEVELOPMENT admin password. Set SeedAdmin__Password before deploying.");
        }
        else
        {
            password = GenerateRandomPassword();
            logger.LogWarning(
                "SeedAdmin:Password is not configured in environment '{Environment}'. Admin account {Email} was created with a random, unknown password. " +
                "Set SeedAdmin__Password and restart, or use the password-reset flow to establish a real password. No default credential has been left in place.",
                environment.EnvironmentName, adminEmail);
        }

        var result = await userManager.CreateAsync(adminUser, password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
        }
        else
        {
            logger.LogError("Failed to create seeded admin {Email}: {Errors}", adminEmail,
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        return adminUser;
    }

    // Cryptographically random password that satisfies the production password policy
    // (upper, lower, digit, and a non-alphanumeric character). Never logged.
    private static string GenerateRandomPassword()
    {
        var bytes = new byte[24];
        RandomNumberGenerator.Fill(bytes);
        return "Aa1!" + Convert.ToBase64String(bytes);
    }

    private static async Task<Merchant> SeedDefaultMerchantAsync(ApplicationDbContext context, ApplicationUser adminUser)
    {
        var merchant = await context.Merchants.FirstOrDefaultAsync(m => m.Slug == "world-link-master");
        if (merchant == null)
        {
            merchant = new Merchant
            {
                UserId = adminUser.Id,
                BusinessName = "World Link Master",
                Slug = "world-link-master",
                Description = "World Link Master's own first-party catalog.",
                StripeOnboardingComplete = false
            };

            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();
        }

        return merchant;
    }

    private static async Task SeedCatalogAsync(ApplicationDbContext context, int defaultMerchantId)
    {
        if (await context.Categories.AnyAsync())
        {
            return;
        }

        var categories = new List<Category>
        {
            new()
            {
                Name = "Tactical Apparel",
                Slug = "tactical-apparel",
                Description = "Durable field-tested clothing built for movement and long days outdoors.",
                ImageUrl = PexelsImage(1408986, 800, 500)
            },
            new()
            {
                Name = "Bags & Packs",
                Slug = "bags-and-packs",
                Description = "Load-bearing packs, sling bags, and pouches for every mission.",
                ImageUrl = PexelsImage(11900635, 800, 500)
            },
            new()
            {
                Name = "Footwear",
                Slug = "footwear",
                Description = "Rugged boots engineered for grip, support, and all-day comfort.",
                ImageUrl = PexelsImage(13882914, 800, 500)
            },
            new()
            {
                Name = "Gear & Accessories",
                Slug = "gear-and-accessories",
                Description = "Belts, gloves, holsters, and everyday-carry essentials.",
                ImageUrl = PexelsImage(3817741, 800, 500)
            },
            new()
            {
                Name = "Outdoor Equipment",
                Slug = "outdoor-equipment",
                Description = "Shelter, hydration, and camp gear for wherever the trail leads.",
                ImageUrl = PexelsImage(17192955, 800, 500)
            }
        };

        context.Categories.AddRange(categories);
        await context.SaveChangesAsync();

        var products = new List<Product>
        {
            // Tactical Apparel
            Product("Sentinel Combat Shirt", "sentinel-combat-shirt", categories[0], "Breathable ripstop combat shirt with reinforced elbows.", Aed(54.99m), "WLM-APP-001", 40, true, 1408986, 4.8m, 312),
            Product("Ranger Field Pants", "ranger-field-pants", categories[0], "Stretch-panel field pants with reinforced knees and 8 pockets.", Aed(69.99m), "WLM-APP-002", 35, true, 16983209, 4.6m, 204),
            Product("Vanguard Softshell Jacket", "vanguard-softshell-jacket", categories[0], "Wind-resistant softshell jacket with adjustable hood.", Aed(89.99m), "WLM-APP-003", 25, false, 6368576, 4.8m, 176),
            Product("Trailblazer Base Layer", "trailblazer-base-layer", categories[0], "Moisture-wicking thermal base layer for cold weather ops.", Aed(34.99m), "WLM-APP-004", 50, false, 9522942, 4.5m, 98),
            Product("Overwatch Cargo Shorts", "overwatch-cargo-shorts", categories[0], "Lightweight ripstop cargo shorts built for hot climates.", Aed(44.99m), "WLM-APP-005", 30, false, 11716436, 4.4m, 67),

            // Bags & Packs
            Product("Expedition 45L Rucksack", "expedition-45l-rucksack", categories[1], "Modular 45L rucksack with MOLLE webbing and hydration port.", Aed(149.99m), "WLM-BAG-001", 20, true, 11900635, 4.9m, 415),
            Product("Recon Sling Pack", "recon-sling-pack", categories[1], "Compact sling pack for fast access to daily essentials.", Aed(59.99m), "WLM-BAG-002", 40, true, 18318640, 4.7m, 289),
            Product("Outrider Waist Pack", "outrider-waist-pack", categories[1], "Low-profile waist pack with concealed carry compartment.", Aed(39.99m), "WLM-BAG-003", 45, false, 11726037, 4.5m, 132),
            Product("Basecamp Duffel 90L", "basecamp-duffel-90l", categories[1], "Heavy-duty 90L duffel for gear transport and deployment.", Aed(119.99m), "WLM-BAG-004", 18, false, 27462287, 4.6m, 88),

            // Footwear
            Product("Summit Tactical Boot", "summit-tactical-boot", categories[2], "8-inch waterproof tactical boot with reinforced toe.", Aed(129.99m), "WLM-FTW-001", 30, true, 13882914, 4.8m, 501),
            Product("Pathfinder Trail Shoe", "pathfinder-trail-shoe", categories[2], "Lightweight trail shoe with aggressive grip outsole.", Aed(94.99m), "WLM-FTW-002", 35, true, 9654860, 4.6m, 243),
            Product("Ironclad Combat Boot", "ironclad-combat-boot", categories[2], "Full-grain leather combat boot with side zip entry.", Aed(139.99m), "WLM-FTW-003", 22, false, 4589107, 4.7m, 159),
            Product("Glacier Insulated Boot", "glacier-insulated-boot", categories[2], "Insulated cold-weather boot rated to -40F.", Aed(159.99m), "WLM-FTW-004", 15, false, 6555893, 4.9m, 94),

            // Gear & Accessories
            Product("Apex Riggers Belt", "apex-riggers-belt", categories[3], "Heavy-duty nylon riggers belt with quick-release buckle.", Aed(29.99m), "WLM-GEA-001", 60, false, 7679471, 4.4m, 76),
            Product("Sentry Tactical Gloves", "sentry-tactical-gloves", categories[3], "Knuckle-protected gloves with touchscreen fingertips.", Aed(24.99m), "WLM-GEA-002", 55, true, 13796801, 4.7m, 358),
            Product("Nightwatch Headlamp", "nightwatch-headlamp", categories[3], "300-lumen rechargeable headlamp with red-light mode.", Aed(32.99m), "WLM-GEA-003", 40, false, 34377535, 4.5m, 121),
            Product("Precision Compass Kit", "precision-compass-kit", categories[3], "Lensatic compass with signal mirror and lanyard.", Aed(19.99m), "WLM-GEA-004", 70, false, 9906080, 4.3m, 54),

            // Outdoor Equipment
            Product("Basecamp 2-Person Tent", "basecamp-2-person-tent", categories[4], "Weatherproof 3-season tent with quick-pitch frame.", Aed(179.99m), "WLM-OUT-001", 12, true, 4268094, 4.8m, 227),
            Product("Trailhead Hydration Carrier", "trailhead-hydration-carrier", categories[4], "3L hydration bladder carrier with insulated hose.", Aed(42.99m), "WLM-OUT-002", 38, false, 20446198, 4.4m, 63),
            Product("Alpine Sleep System", "alpine-sleep-system", categories[4], "Compression sleeping bag rated for freezing temps.", Aed(109.99m), "WLM-OUT-003", 20, false, 7009497, 4.6m, 142),
            Product("Frontier Camp Stove", "frontier-camp-stove", categories[4], "Compact folding camp stove with wind-resistant burner.", Aed(49.99m), "WLM-OUT-004", 28, false, 29295215, 4.5m, 85)
        };

        foreach (var product in products)
        {
            product.MerchantId = defaultMerchantId;

            // Enroll the featured lineup in the Trade Program at a 30% wholesale discount.
            if (product.IsFeatured)
            {
                product.WholesalePrice = Math.Round(product.Price * 0.70m, 2);
            }
        }

        ApplyVariants(products);

        context.Products.AddRange(products);
        await context.SaveChangesAsync();
    }

    // Large catalog expansion: jackets, hats, shoes, belts, and gear components,
    // generated from prefix/type combinations so the storefront has a full multi-page
    // catalog rather than just the curated 21-item launch lineup.
    private static async Task SeedBulkExpansionAsync(ApplicationDbContext context, int defaultMerchantId)
    {
        const string bulkSkuMarker = "WLM-BULK-";
        if (await context.Products.AnyAsync(p => p.Sku.StartsWith(bulkSkuMarker)))
        {
            return;
        }

        var categories = await context.Categories.ToDictionaryAsync(c => c.Slug);
        var usedSlugs = new HashSet<string>(await context.Products.Select(p => p.Slug).ToListAsync());
        var products = new List<Product>();

        products.AddRange(GenerateBulkProducts(JacketTypes, categories["tactical-apparel"], "WLM-BULK-JKT", JacketImages, JacketColorPool, ClothingSizes, 300m, 500m, 32, defaultMerchantId, usedSlugs));
        products.AddRange(GenerateBulkProducts(HatTypes, categories["tactical-apparel"], "WLM-BULK-HAT", HatImages, HatColorPool, null, 60m, 140m, 32, defaultMerchantId, usedSlugs));
        products.AddRange(GenerateBulkProducts(ShoeTypes, categories["footwear"], "WLM-BULK-SHO", ShoeImages, ShoeColorPool, ShoeSizes, 300m, 550m, 32, defaultMerchantId, usedSlugs));
        products.AddRange(GenerateBulkProducts(BeltTypes, categories["gear-and-accessories"], "WLM-BULK-BLT", BeltImages, BeltColorPool, GloveSizes, 90m, 180m, 32, defaultMerchantId, usedSlugs));
        products.AddRange(GenerateBulkProducts(ComponentTypes, categories["gear-and-accessories"], "WLM-BULK-CMP", ComponentImages, ComponentColorPool, null, 40m, 120m, 32, defaultMerchantId, usedSlugs));

        context.Products.AddRange(products);
        await context.SaveChangesAsync();
    }

    private static readonly string[] BulkPrefixes =
    {
        "Sentinel", "Vanguard", "Recon", "Ranger", "Ridge", "Ironclad", "Summit", "Apex",
        "Ghost", "Talon", "Falcon", "Warden", "Outrider", "Trailhawk", "Rampart", "Bastion",
        "Frontier", "Blackwatch", "Ironside", "Nomad", "Sabre", "Wraith", "Hunter", "Sentry",
        "Marauder", "Centurion", "Vigil", "Redline", "Ember", "Onyx", "Cipher", "Titan"
    };

    // Note: type wording is deliberately kept distinct from the launch lineup's exact
    // product names (e.g. no "Softshell Jacket", "Combat Boot", or "Riggers Belt" here)
    // to avoid colliding with the unique Slug constraint on already-seeded products.
    private static readonly string[] JacketTypes = { "Combat Jacket", "Field Jacket", "Bomber Jacket", "Parka", "Windbreaker", "Rain Shell", "Shell Jacket", "Insulated Jacket" };
    private static readonly string[] HatTypes = { "Tactical Cap", "Boonie Hat", "Beanie", "Patrol Cap", "Sun Hat", "Watch Cap" };
    private static readonly string[] ShoeTypes = { "Trail Shoe", "Patrol Boot", "Hiking Shoe", "Tactical Sneaker", "Desert Boot", "Assault Boot" };
    private static readonly string[] BeltTypes = { "Tactical Belt", "Gun Belt", "Duty Belt", "Web Belt", "EDC Belt" };
    private static readonly string[] ComponentTypes = { "MOLLE Pouch", "Mag Pouch", "Admin Pouch", "Utility Pouch", "Chest Rig", "Gear Mount", "Sling Strap" };

    private static readonly int[] JacketImages = { 37678157, 32132582, 6368576, 18832219, 22064415, 19928303, 32039137, 1408986, 669291, 7468101, 30217011, 15814566, 33648165, 34409819, 25525596, 6786309, 7416037, 9522942 };
    private static readonly int[] HatImages = { 8449785, 8443673, 8443671, 17216548, 16919435, 16047421, 17216544, 17179123, 30415388 };
    private static readonly int[] ShoeImages = { 13020558, 1047966, 32189248, 4275517, 17115801, 18236151, 4589107, 13882914, 9654860, 28991265, 7026406, 9654861, 4314202, 11280664, 6555893, 31954808, 29490907, 10781162, 35120029, 12983267, 11061924, 8729056, 5579004, 5876412, 31650335 };
    private static readonly int[] BeltImages = { 31959216, 31959214, 31959215, 31323080, 31959217, 31367058, 35222039, 1023937, 7679471, 6371786, 35322152, 33879839, 34443362, 37483619, 38053200, 34443363, 38053161, 38053187, 6654765, 35322161, 7679660 };
    private static readonly int[] ComponentImages = { 6562582, 6562586, 1883947, 9099796, 30871177, 13643158, 14401952, 8388915, 34693951, 34695170, 16253057, 7832893, 6114577, 20446195 };
    private static readonly int[] BagImages =
    {
        11900635, 18318640, 11726037, 27462287, 11900631, 31438937,
        18318689, 9448166, 11726029, 30407654, 16359250, 9448163
    };

    private static List<Product> GenerateBulkProducts(
        string[] types,
        Category category,
        string skuPrefix,
        int[] imagePool,
        (string Name, string Hex)[] colorPool,
        string[]? sizes,
        decimal minPriceAed,
        decimal maxPriceAed,
        int count,
        int merchantId,
        HashSet<string> usedSlugs)
    {
        var result = new List<Product>();
        var priceRange = maxPriceAed - minPriceAed;

        for (int index = 0; index < count; index++)
        {
            var type = types[index % types.Length];
            var prefix = BulkPrefixes[(index / types.Length) % BulkPrefixes.Length];
            var name = $"{prefix} {type}";
            var image = imagePool[index % imagePool.Length];
            var price = Math.Round(minPriceAed + priceRange * ((index * 37) % 100) / 100m, 2);
            var rating = Math.Round(4.2m + (index * 13 % 8) * 0.1m, 1);
            var reviewCount = 15 + (index * 29 % 380);

            // Defend against a generated name accidentally matching an existing product
            // (e.g. a curated launch item) and violating the unique Slug constraint.
            var slug = Slugify(name);
            var dedupeSuffix = 2;
            while (!usedSlugs.Add(slug))
            {
                slug = $"{Slugify(name)}-{dedupeSuffix}";
                dedupeSuffix++;
            }

            var product = new Product
            {
                Name = name,
                Slug = slug,
                Category = category,
                MerchantId = merchantId,
                ShortDescription = $"{name} built to World Link Master's field-tested standard.",
                Description = $"{name} engineered for durability and performance. Reinforced construction, tested materials, and field-ready design make this a reliable choice for operators and outdoor professionals alike.",
                Price = price,
                Sku = $"{skuPrefix}-{index + 1:000}",
                StockQuantity = 10 + (index % 40),
                IsFeatured = false,
                Rating = rating,
                ReviewCount = reviewCount,
                ImageUrl = PexelsImage(image, 800, 800)
            };

            var numColors = 2 + (index % 2);
            for (int c = 0; c < numColors && c < colorPool.Length; c++)
            {
                var color = colorPool[(index + c) % colorPool.Length];
                product.Colors.Add(new ProductColor
                {
                    Name = color.Name,
                    HexCode = color.Hex,
                    ImageUrl = product.ImageUrl,
                    SortOrder = c
                });
            }

            if (sizes != null)
            {
                for (int s = 0; s < sizes.Length; s++)
                {
                    product.Sizes.Add(new ProductSize { Label = sizes[s], SortOrder = s });
                }
            }

            var altImage = imagePool[(index + 3) % imagePool.Length];
            if (altImage != image)
            {
                product.Images.Add(new ProductImage { ImageUrl = PexelsImage(altImage, 800, 800), Label = "Alternate", SortOrder = 1 });
            }

            result.Add(product);
        }

        return result;
    }

    private static string Slugify(string name)
    {
        var slug = name.ToLowerInvariant().Trim();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        return slug.Trim('-');
    }

    private static string PexelsImage(int photoId, int width, int height)
    {
        return $"https://images.pexels.com/photos/{photoId}/pexels-photo-{photoId}.jpeg?auto=compress&cs=tinysrgb&w={width}&h={height}&dpr=1";
    }

    // World Link Master prices in AED. Source figures below are written as their
    // USD-equivalent for readability and converted at the AED peg rate (~3.67).
    private const decimal UsdToAedRate = 3.67m;

    private static decimal Aed(decimal usdAmount) => Math.Round(usdAmount * UsdToAedRate, 2);

    private static readonly (string Name, string Hex) Black = ("Black", "#1c1c1c");
    private static readonly (string Name, string Hex) CoyoteTan = ("Coyote Tan", "#b08d57");
    private static readonly (string Name, string Hex) RangerGreen = ("Ranger Green", "#4b5320");
    private static readonly (string Name, string Hex) Charcoal = ("Charcoal", "#36454f");
    private static readonly (string Name, string Hex) Brown = ("Brown", "#5b3a29");
    private static readonly (string Name, string Hex) Silver = ("Silver", "#c0c0c0");
    private static readonly (string Name, string Hex) Navy = ("Navy", "#1b263b");
    private static readonly (string Name, string Hex) Khaki = ("Khaki", "#c3b091");
    private static readonly (string Name, string Hex) SlateGray = ("Slate Gray", "#6c757d");
    private static readonly (string Name, string Hex) OliveDrab = ("Olive Drab", "#5c5f2e");
    private static readonly (string Name, string Hex) ArcticWhite = ("Arctic White", "#eef1ee");

    private static readonly (string Name, string Hex)[] JacketColorPool = { Black, CoyoteTan, RangerGreen, Charcoal, OliveDrab };
    private static readonly (string Name, string Hex)[] HatColorPool = { Black, CoyoteTan, RangerGreen, OliveDrab, Khaki };
    private static readonly (string Name, string Hex)[] ShoeColorPool = { Black, Brown, CoyoteTan, Charcoal };
    private static readonly (string Name, string Hex)[] BeltColorPool = { Black, CoyoteTan, Brown };
    private static readonly (string Name, string Hex)[] ComponentColorPool = { Black, CoyoteTan, RangerGreen, OliveDrab };
    private static readonly (string Name, string Hex)[] BagColorPool = { Black, CoyoteTan, RangerGreen, Charcoal };

    // Added to every product's color list so each item offers a fuller range of options.
    private static readonly (string Name, string Hex)[] BonusColors = { Navy, Khaki, SlateGray, OliveDrab, ArcticWhite };

    private static readonly string[] ClothingSizes = { "S", "M", "L", "XL", "XXL" };
    private static readonly string[] ShoeSizes = { "38", "39", "40", "41", "42", "43", "44", "45" };
    private static readonly string[] GloveSizes = { "S", "M", "L", "XL" };

    private static readonly Dictionary<string, (decimal Min, decimal Max, int[] Images, (string Name, string Hex)[] Colors, string[]? Sizes)> SubcategoryProductProfile = new()
    {
        ["tactical-apparel"] = (110m, 330m, JacketImages.Concat(HatImages).ToArray(), JacketColorPool, ClothingSizes),
        ["footwear"] = (300m, 570m, ShoeImages, ShoeColorPool, ShoeSizes),
        ["bags-and-packs"] = (150m, 450m, BagImages, BagColorPool, null),
        ["gear-and-accessories"] = (45m, 190m, ComponentImages.Concat(BeltImages).ToArray(), ComponentColorPool, null),
    };

    // Gives every subcategory (Camouflage Tape, Trolley Bags, etc.) at least a few real,
    // purchasable products so browsing by subcategory never shows an empty page.
    private static async Task SeedSubcategoryPlaceholderProductsAsync(ApplicationDbContext context, int defaultMerchantId)
    {
        const string skuMarker = "WLM-SUB-";
        if (await context.Products.AnyAsync(p => p.Sku.StartsWith(skuMarker)))
        {
            return;
        }

        var subcategories = await context.Subcategories.Include(s => s.Category).ToListAsync();
        var usedSlugs = new HashSet<string>(await context.Products.Select(p => p.Slug).ToListAsync());
        var products = new List<Product>();
        var skuCounter = 1;

        foreach (var subcategory in subcategories)
        {
            var categorySlug = subcategory.Category?.Slug;
            if (categorySlug == null || !SubcategoryProductProfile.TryGetValue(categorySlug, out var profile))
            {
                continue; // e.g. Outdoor Equipment has no subcategories yet
            }

            if (await context.Products.AnyAsync(p => p.SubcategoryId == subcategory.Id))
            {
                continue;
            }

            for (int i = 0; i < 3; i++)
            {
                var prefix = BulkPrefixes[(subcategory.Id * 3 + i) % BulkPrefixes.Length];
                var name = $"{prefix} {subcategory.Name}";
                var slug = Slugify(name);
                var dedupeSuffix = 2;
                while (!usedSlugs.Add(slug))
                {
                    slug = $"{Slugify(name)}-{dedupeSuffix}";
                    dedupeSuffix++;
                }

                var image = profile.Images[(subcategory.Id * 3 + i) % profile.Images.Length];
                var price = Math.Round(profile.Min + (profile.Max - profile.Min) * ((subcategory.Id * 37 + i * 19) % 100) / 100m, 2);

                var product = new Product
                {
                    Name = name,
                    Slug = slug,
                    Category = subcategory.Category,
                    Subcategory = subcategory,
                    MerchantId = defaultMerchantId,
                    ShortDescription = $"{name} — field-tested {subcategory.Name.ToLowerInvariant()} from World Link Master.",
                    Description = $"{name} built to World Link Master's field-tested standard. Reinforced construction, tested materials, and reliable performance for operators and outdoor professionals.",
                    Price = price,
                    Sku = $"{skuMarker}{skuCounter:0000}",
                    StockQuantity = 10 + ((subcategory.Id + i) % 40),
                    IsFeatured = false,
                    Rating = Math.Round(4.2m + ((subcategory.Id + i) % 8) * 0.1m, 1),
                    ReviewCount = 10 + ((subcategory.Id * 7 + i * 11) % 300),
                    ImageUrl = PexelsImage(image, 800, 800)
                };
                skuCounter++;

                var numColors = Math.Min(3, profile.Colors.Length);
                for (int c = 0; c < numColors; c++)
                {
                    var color = profile.Colors[(i + c) % profile.Colors.Length];
                    product.Colors.Add(new ProductColor { Name = color.Name, HexCode = color.Hex, ImageUrl = product.ImageUrl, SortOrder = c });
                }

                if (profile.Sizes != null)
                {
                    for (int s = 0; s < profile.Sizes.Length; s++)
                    {
                        product.Sizes.Add(new ProductSize { Label = profile.Sizes[s], SortOrder = s });
                    }
                }

                products.Add(product);
            }
        }

        context.Products.AddRange(products);
        await context.SaveChangesAsync();
    }

    private static readonly Dictionary<string, (int[] ExtraImages, (string Name, string Hex)[] Colors, string[]? Sizes)> Variants = new()
    {
        ["sentinel-combat-shirt"] = (new[] { 7468101, 669291 }, new[] { Black, CoyoteTan, RangerGreen }, ClothingSizes),
        ["ranger-field-pants"] = (new[] { 16983204, 16983321 }, new[] { Black, CoyoteTan, RangerGreen }, ClothingSizes),
        ["vanguard-softshell-jacket"] = (new[] { 19928303, 32039137 }, new[] { Black, Charcoal, CoyoteTan }, ClothingSizes),
        ["trailblazer-base-layer"] = (new[] { 6506484, 32256806 }, new[] { Black, Charcoal }, ClothingSizes),
        ["overwatch-cargo-shorts"] = (new[] { 35043249, 20196037 }, new[] { Black, CoyoteTan, RangerGreen }, ClothingSizes),

        ["expedition-45l-rucksack"] = (new[] { 11900631, 31438937 }, new[] { Black, CoyoteTan, RangerGreen }, null),
        ["recon-sling-pack"] = (new[] { 18318689, 9448166 }, new[] { Black, CoyoteTan }, null),
        ["outrider-waist-pack"] = (new[] { 11726029, 30407654 }, new[] { Black, RangerGreen }, null),
        ["basecamp-duffel-90l"] = (new[] { 16359250, 9448163 }, new[] { Black, CoyoteTan }, null),

        ["summit-tactical-boot"] = (new[] { 9654861, 11280664 }, new[] { Black, CoyoteTan }, ShoeSizes),
        ["pathfinder-trail-shoe"] = (new[] { 32189248, 31954808 }, new[] { Black, Brown }, ShoeSizes),
        ["ironclad-combat-boot"] = (new[] { 4314202, 10781162 }, new[] { Black, Brown }, ShoeSizes),
        ["glacier-insulated-boot"] = (new[] { 7026406, 17115801 }, new[] { Black, Charcoal }, ShoeSizes),

        ["apex-riggers-belt"] = (new[] { 7679660, 6654765 }, new[] { Black, CoyoteTan, Brown }, null),
        ["sentry-tactical-gloves"] = (new[] { 3817741, 5343768 }, new[] { Black, CoyoteTan }, GloveSizes),
        ["nightwatch-headlamp"] = (new[] { 15977285, 6831219 }, new[] { Black, RangerGreen }, null),
        ["precision-compass-kit"] = (new[] { 37270880, 17184744 }, new[] { Black, CoyoteTan }, null),

        ["basecamp-2-person-tent"] = (new[] { 17192955, 15925118 }, new[] { RangerGreen, CoyoteTan }, null),
        ["trailhead-hydration-carrier"] = (new[] { 11667749, 31438937 }, new[] { Black, CoyoteTan }, null),
        ["alpine-sleep-system"] = (new[] { 10772129, 7010184 }, new[] { Black, RangerGreen }, null),
        ["frontier-camp-stove"] = (new[] { 8911191, 6324492 }, new[] { Black, Silver }, null)
    };

    private static void ApplyVariants(List<Product> products)
    {
        foreach (var product in products)
        {
            if (!Variants.TryGetValue(product.Slug, out var variant))
            {
                continue;
            }

            for (int i = 0; i < variant.ExtraImages.Length; i++)
            {
                product.Images.Add(new ProductImage
                {
                    ImageUrl = PexelsImage(variant.ExtraImages[i], 800, 800),
                    Label = i == 0 ? "Detail" : "Alternate",
                    SortOrder = i + 1
                });
            }

            var allColors = variant.Colors.Concat(BonusColors).ToArray();
            for (int i = 0; i < allColors.Length; i++)
            {
                // First color shows the product's main photo; each additional color
                // reuses one of the gallery photos so picking a color swaps the image.
                // Colors beyond the available gallery photos fall back to the main photo.
                var colorImageUrl = i == 0 || i - 1 >= variant.ExtraImages.Length
                    ? product.ImageUrl
                    : PexelsImage(variant.ExtraImages[i - 1], 800, 800);

                product.Colors.Add(new ProductColor
                {
                    Name = allColors[i].Name,
                    HexCode = allColors[i].Hex,
                    ImageUrl = colorImageUrl,
                    SortOrder = i
                });
            }

            if (variant.Sizes != null)
            {
                for (int i = 0; i < variant.Sizes.Length; i++)
                {
                    product.Sizes.Add(new ProductSize
                    {
                        Label = variant.Sizes[i],
                        SortOrder = i
                    });
                }
            }
        }
    }

    private static Product Product(string name, string slug, Category category, string shortDescription, decimal price, string sku, int stock, bool featured, int pexelsPhotoId, decimal rating, int reviewCount)
    {
        return new Product
        {
            Name = name,
            Slug = slug,
            Category = category,
            ShortDescription = shortDescription,
            Description = shortDescription + " Built to the same standard World Link Master applies across every category: tested materials, reinforced stress points, and field-ready durability.",
            Price = price,
            Sku = sku,
            StockQuantity = stock,
            IsFeatured = featured,
            Rating = rating,
            ReviewCount = reviewCount,
            ImageUrl = PexelsImage(pexelsPhotoId, 800, 800)
        };
    }
}
