using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using Stripe;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.HealthChecks;
using WorldLinkMaster.Web.Hubs;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Resources;
using WorldLinkMaster.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Database — connection string comes from configuration/environment
// (ConnectionStrings__DefaultConnection). Connection resiliency is enabled so
// transient SQL failures (common with cloud SQL) are retried automatically.
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is not configured. Set the ConnectionStrings__DefaultConnection environment variable.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
    {
        sql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null);
        sql.CommandTimeout(60);
    }));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// ---------------------------------------------------------------------------
// ASP.NET Identity — production security posture.
// ---------------------------------------------------------------------------
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        // Require confirmed email in production (real SMTP delivers the link). In Development
        // there is usually no SMTP, so requiring it would leave freshly-registered accounts
        // unable to log in — relax it locally only.
        options.SignIn.RequireConfirmedAccount = !builder.Environment.IsDevelopment();

        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredUniqueChars = 1;

        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;

        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Secure the Identity auth cookie. HTTPS-only outside Development (dev may run over plain HTTP).
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});

// No ResourcesPath configured deliberately: SharedResource.cs already lives in a folder (and
// namespace) called "Resources", so its compiled resource name is "WorldLinkMaster.Web.Resources.
// SharedResource" — setting ResourcesPath="Resources" here would double that prefix and the
// localizer would never find a matching satellite resource.
builder.Services.AddLocalization();
var mvcBuilder = builder.Services.AddControllersWithViews()
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization(options =>
        options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));

// Dev convenience: .cshtml edits take effect on the next request — just refresh the
// browser — instead of needing a full rebuild/restart. Never enabled outside Development.
if (builder.Environment.IsDevelopment())
{
    mvcBuilder.AddRazorRuntimeCompilation();
}

builder.Services.AddSignalR();

// ---------------------------------------------------------------------------
// Arabic/English localization. Arabic is the default for every visitor; a
// language switcher (see LocalizationController) sets a cookie to override it.
// Only the cookie provider is registered — deliberately no browser
// Accept-Language sniffing, so a fresh visitor always sees Arabic regardless
// of their browser's locale, exactly as requested.
//
// The "ar" culture's NumberFormat/DateTimeFormat are forced to invariant
// conventions here. Without this, CultureInfo.CurrentCulture (used by MVC's
// model binder to parse posted form values) switches to Arabic numeral/
// separator conventions whenever a visitor is in Arabic mode — but HTML
// <input type="number"/date"> always submits values in the invariant "."
// format regardless of page language, so every numeric/date form field
// (prices, quantities, discount percents, etc.) would silently fail to bind.
// Only the resource-string lookup (CurrentUICulture) needs to be Arabic.
// ---------------------------------------------------------------------------
var arabicCulture = new CultureInfo("ar")
{
    NumberFormat = CultureInfo.InvariantCulture.NumberFormat,
    DateTimeFormat = CultureInfo.InvariantCulture.DateTimeFormat
};
var supportedCultures = new[] { arabicCulture, new CultureInfo("en") };
var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(arabicCulture, arabicCulture),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
};
localizationOptions.RequestCultureProviders = new IRequestCultureProvider[]
{
    new CookieRequestCultureProvider()
};

// ---------------------------------------------------------------------------
// Distributed cache, session, and Data Protection.
// Redis is used when ConnectionStrings__Redis is set (required for multi-instance
// hosting); otherwise the app falls back to in-memory suitable for a single node.
// ---------------------------------------------------------------------------
var redisConnection = builder.Configuration.GetConnectionString("Redis");
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("WorldLinkMaster");

if (!string.IsNullOrWhiteSpace(redisConnection))
{
    var multiplexer = ConnectionMultiplexer.Connect(redisConnection);
    builder.Services.AddSingleton<IConnectionMultiplexer>(multiplexer);
    builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
    // Share Data Protection keys across instances so auth cookies / antiforgery / session survive scale-out and restarts.
    dataProtection.PersistKeysToStackExchangeRedis(multiplexer, "WorldLinkMaster-DataProtection-Keys");
}
else
{
    builder.Services.AddDistributedMemoryCache();

    // Optional: persist keys to a mounted volume so they survive container restarts on a single node.
    var keyDirectory = builder.Configuration["DataProtection:KeyDirectory"];
    if (!string.IsNullOrWhiteSpace(keyDirectory))
    {
        dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
    }
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

// ---------------------------------------------------------------------------
// Application services.
// ---------------------------------------------------------------------------
builder.Services.AddScoped<ICartService, SessionCartService>();
builder.Services.AddScoped<IStripeConnectService, StripeConnectService>();
builder.Services.AddScoped<IPromoService, PromoService>();
builder.Services.AddScoped<ICouponService, WorldLinkMaster.Web.Services.CouponService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IOrderFulfillmentService, OrderFulfillmentService>();
builder.Services.AddHttpClient<IRecaptchaService, RecaptchaService>();

// ---------------------------------------------------------------------------
// JWT Bearer auth for the REST API surface (api/auth/*, etc.). Added alongside
// Identity's cookie auth (which stays default for the website). API controllers
// opt in via [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)].
// ---------------------------------------------------------------------------
var jwtKey = builder.Configuration["Jwt:Key"];
var jwtKeyConfigured = !string.IsNullOrWhiteSpace(jwtKey);
if (!jwtKeyConfigured)
{
    // Not configured — fall back to an ephemeral key so startup succeeds. Tokens will not
    // survive a restart or work across instances until Jwt__Key is set (warned at startup).
    jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}

builder.Services.AddAuthentication()
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey!)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// ---------------------------------------------------------------------------
// Reverse-proxy awareness: honor X-Forwarded-For/Proto so HTTPS redirection,
// secure cookies, and Request.Scheme work when TLS is terminated at the proxy.
// ---------------------------------------------------------------------------
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ---------------------------------------------------------------------------
// Health checks: /health (liveness) and /ready (readiness: DB + Stripe + SMTP).
// ---------------------------------------------------------------------------
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" })
    .AddCheck<StripeConfigurationHealthCheck>("stripe", tags: new[] { "ready" })
    .AddCheck<SmtpConfigurationHealthCheck>("smtp", tags: new[] { "ready" });

var app = builder.Build();

if (!jwtKeyConfigured)
{
    app.Logger.LogWarning(
        "Jwt:Key is not configured; using an ephemeral signing key. Set the Jwt__Key environment variable so issued tokens survive restarts and are valid across instances.");
}

// ---------------------------------------------------------------------------
// HTTP request pipeline.
// ---------------------------------------------------------------------------
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // HSTS: instruct browsers to use HTTPS only. Safe behind a TLS-terminating proxy
    // because UseForwardedHeaders surfaces the original HTTPS scheme.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRequestLocalization(localizationOptions);

// Ambient currency preference (AED/USD) — read once per request from the "currency" cookie
// set by CurrencyController, so ToDisplayCurrency() can format prices without every call
// site needing to pass it through explicitly. Defaults to AED for visitors with no cookie.
app.Use(async (context, next) =>
{
    CurrencyContext.Current = context.Request.Cookies["currency"] == "USD" ? "USD" : "AED";
    await next();
});

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Products}/{action=Index}/{id?}");

// The bare site root ("/") always lands on the Welcome page — a fixed target with no route
// parameters, so it can never collide with links generated elsewhere (see "default" below).
app.MapControllerRoute(
    name: "root",
    pattern: "",
    defaults: new { controller = "Home", action = "Welcome" });

// Every other URL keeps its original shape. Deliberately no default controller here — action
// still defaults to "Index" (so "/Cart", "/Products", "/Home" etc. all keep working), but with
// no default controller, NO single controller/action pair can ever collapse all the way down to
// the empty path. That's reserved exclusively for "root" above. (An earlier version of this
// route defaulted controller to "Products", which fixed the Home/Index collision but simply
// moved the same bug onto Products/Index instead — every category link, which targets
// Products/Index, collapsed to "/" and got hijacked by the Welcome page.)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller}/{action=Index}/{id?}");
app.MapRazorPages();

// Liveness: process is up (no dependency checks).
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
});

// Readiness: dependencies are usable (returns 503 if the database is unreachable).
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteJson
});

using (var scope = app.Services.CreateScope())
{
    await SeedData.InitializeAsync(scope.ServiceProvider);
}

app.Run();
