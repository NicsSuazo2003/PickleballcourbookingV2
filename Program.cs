using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;          // ✅ NEW
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PickleballBookingSystem.Data;
using PickleballBookingSystem.Interfaces;
using PickleballBookingSystem.Middleware;
using PickleballBookingSystem.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.Sources.Clear();
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ─── Database ─────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ─── JWT ──────────────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key must be configured (use env var, never commit it).");

if (jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 characters.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name,
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

// ─── ForwardedHeaders: trust X-Forwarded-For from Render's edge ───
//     MUST be configured here and used in the pipeline BEFORE any
//     component that reads HttpContext.Connection.RemoteIpAddress
//     (RateLimiter, ClientResolver, SecurityLogger, etc.).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // Render terminates at Cloudflare, then its own internal proxy.
    // Clear the default trust list (127.0.0.1 only) so those proxies are trusted.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ─── Typed HTTP clients ───────────────────────────────────
builder.Services.AddHttpClient("supabase", c => { c.Timeout = TimeSpan.FromSeconds(20); });
builder.Services.AddHttpClient("brevo", c =>
{
    c.BaseAddress = new Uri("https://api.brevo.com/");
    c.Timeout = TimeSpan.FromSeconds(15);
});

// ─── Rate Limiter ─────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    static string ClientIp(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // Global fallback
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var route = ctx.Request.Path.Value ?? "/";
        var prefix =
            route.StartsWith("/api/bookings", StringComparison.Ordinal) ? "bookings" :
            route.StartsWith("/api/open-play", StringComparison.Ordinal) ? "openplay" :
            route.StartsWith("/api/auth", StringComparison.Ordinal) ? "auth" :
            route.StartsWith("/api/courts", StringComparison.Ordinal) ? "courts" :
            route.StartsWith("/api/clients", StringComparison.Ordinal) ? "clients" :
            "other";

        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: $"{prefix}:{ClientIp(ctx)}",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = prefix switch
                {
                    "bookings" => 60,
                    "openplay" => 60,
                    "auth" => 20,
                    "courts" => 120,
                    "clients" => 120,
                    _ => 120,
                },
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            });
    });

    options.AddPolicy("booking", ctx =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: $"booking-create:{ClientIp(ctx)}",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(10),
                SegmentsPerWindow = 10,
                QueueLimit = 0,
            }));

    options.AddPolicy("auth", ctx =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: $"auth:{ClientIp(ctx)}",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                SegmentsPerWindow = 15,
                QueueLimit = 0,
            }));

    options.AddPolicy("track", ctx =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: $"track:{ClientIp(ctx)}",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(10),
                SegmentsPerWindow = 10,
                QueueLimit = 0,
            }));

    options.AddPolicy("upload", ctx =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: $"upload:{ClientIp(ctx)}",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromHours(1),
                SegmentsPerWindow = 12,
                QueueLimit = 0,
            }));

    options.AddPolicy("join", ctx =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: $"join:{ClientIp(ctx)}",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromHours(1),
                SegmentsPerWindow = 12,
                QueueLimit = 0,
            }));

    options.OnRejected = async (ctx, token) =>
    {
        try
        {
            var clientId = ctx.HttpContext.Items["ClientId"] as Guid?;
            if (clientId is Guid cid)
            {
                var secLogger = ctx.HttpContext.RequestServices.GetService<ISecurityLogger>();
                if (secLogger is not null)
                {
                    var ip = ctx.HttpContext.Connection.RemoteIpAddress?.ToString();
                    var ua = ctx.HttpContext.Request.Headers["User-Agent"].FirstOrDefault();

                    await secLogger.LogAsync(new SecurityEvent
                    {
                        ClientId = cid,
                        IpHash = secLogger.HashIp(ip),
                        IpAddress = ip,
                        UserAgent = ua,
                        EventType = "rate_limit.triggered",
                        Severity = "warning",
                        Message = $"Rate limit hit on {ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}",
                        Metadata = new
                        {
                            path = ctx.HttpContext.Request.Path.Value,
                            method = ctx.HttpContext.Request.Method,
                        },
                        Success = false,
                        ErrorCode = "429",
                    });
                }
            }
        }
        catch { }

        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsync(
            """{"message":"Too many requests. Please slow down and try again shortly."}""",
            token);
    };
});

// ─── Services ────────────────────────────────────────────
builder.Services.AddSingleton<EmailService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICourtService, CourtService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IOpenPlayService, OpenPlayService>();
builder.Services.AddScoped<IPricingRuleService, PricingRuleService>();
builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddHostedService<ExpiredBookingWorker>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ISecurityLogger, SecurityLogger>();
builder.Services.AddHostedService<SecurityRetentionWorker>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHealthChecks();
builder.Services.AddSwaggerGen();

// ─── CORS ────────────────────────────────────────────────
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[]
    {
        "http://localhost:5173",
        "http://localhost:3000",
        "https://sideout-playground.vercel.app",
        "https://sideoutplayground.vercel.app",
        "https://pickleball-client2.vercel.app",
        "https://pickle-joe-booking-sys.vercel.app",
        "https://centercourt-booking.vercel.app",
        "https://picklejoe.vercel.app"
    };

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

var app = builder.Build();

// ─── Startup task ─────────────────────────────────────────
_ = Task.Run(async () =>
{
    await Task.Delay(TimeSpan.FromSeconds(5));
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        try { DbSeeder.Initialize(db, app.Configuration, logger); }
        catch (Exception ex) { logger.LogError(ex, "Seeding failed; app will start with empty DB."); }

        var clientIds = await db.Clients.Select(c => c.Id).ToListAsync();
        var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
        foreach (var cid in clientIds)
        {
            try
            {
                await bookingService.AutoCompletePastBookingsAsync(cid);
                await bookingService.CancelExpiredPaymentsAsync(cid);
            }
            catch { }
        }
    }
    catch { }
});

// ─── Pipeline ────────────────────────────────────────────

// ✅ MUST be first — rewrites RemoteIpAddress to the real client IP
//    before anything (ClientResolver, RateLimiter, SecurityLogger) reads it.
app.UseForwardedHeaders();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<ClientResolver>();

app.UseCors("Frontend");
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"status\":\"healthy\"}");
    }
});

app.Run();