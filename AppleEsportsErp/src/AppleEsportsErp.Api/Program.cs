using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using AppleEsportsErp.Api.Filters;
using AppleEsportsErp.Api.Hubs;
using AppleEsportsErp.Api.Middleware;
using AppleEsportsErp.Application.Constants;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Infrastructure.Data;
using AppleEsportsErp.Infrastructure.Identity;
using AppleEsportsErp.Infrastructure.Services;
using Serilog;

// ═══════════════════════════════════════════════
//  AppleEsports ERP — .NET 8 Enterprise Backend
//  SOP Master Source of Truth compliance
// ═══════════════════════════════════════════════

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Pinned to where the executable lives, not to whatever directory it happened to be
    // launched from. A Windows service starts in System32, so the default would send it
    // looking for appsettings.Production.json there — and it would die on start-up
    // complaining that nothing is configured, which is exactly how a branch install
    // fails in a way nobody can diagnose.
    ContentRootPath = AppContext.BaseDirectory,
});

// At a branch this runs as a Windows service so the shop is ready the moment the operator
// PC boots, with no console window for anyone to close by accident. Harmless elsewhere:
// outside a service host it is a no-op, so the same build still runs in Docker and from
// the command line unchanged.
builder.Host.UseWindowsService(options => options.ServiceName = "AppleEsportsApi");

// Enable DI validation on build
builder.Host.UseDefaultServiceProvider((context, options) => {
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

// ── Serilog ──
//
// The path is absolute, and deliberately so. "logs/appleesports-.log" is relative to the
// process working directory, which for a Windows service is C:\Windows\System32 — so a
// branch quietly wrote its log there, where nobody would ever look for it and where it has
// no business being. Setting ContentRootPath does not help; Serilog resolves against the
// working directory, not the content root.
//
// In a container this is a path inside it, which is where the logs were expected anyway.
var logDirectory = OperatingSystem.IsWindows()
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                   "Apple Esports", "logs")
    : Path.Combine(AppContext.BaseDirectory, "logs");

Directory.CreateDirectory(logDirectory);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    // Every request logged six lines and every query its full SQL, which on the sessions
    // screen — polled continuously — came to roughly 3 MB a minute, or 4 GB a day on a till
    // that is meant to run unattended for months. The framework's own chatter is dropped;
    // anything the application logs itself still comes through at Information.
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Infrastructure", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [req:{RequestId}] [op:{OperatorId}] [br:{BranchId}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(Path.Combine(logDirectory, "appleesports-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        // A hard ceiling as well as a daily roll. The daily roll alone did not stop a single
        // day's file reaching 612 MB, and a till that fills its own disk stops the shop.
        fileSizeLimitBytes: 50L * 1024 * 1024,
        rollOnFileSizeLimit: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [req:{RequestId}] [op:{OperatorId}] [br:{BranchId}] [sh:{ShiftId}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
builder.Host.UseSerilog();

Log.Information("Logging to {LogDirectory}", logDirectory);

// ── Configuration sections ──
var jwtConfig = builder.Configuration.GetSection("Jwt");
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:5173", "http://127.0.0.1:5173" };

// ═══════════════════════════════════════════════
//  SERVICES — maps from server/src/index.js
// ═══════════════════════════════════════════════

// ── 1. EF Core / PostgreSQL (SOP §23) ──
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// ── 2. JWT Authentication (SOP §21 + Q1: full claims embedded) ──
//
// Checked explicitly rather than dereferenced with "!". In Docker these arrive as
// environment variables from compose, but a branch install has neither — and the
// unchecked version died on start-up with "ArgumentNullException: Parameter 's'",
// which tells whoever is standing at the branch precisely nothing. The service would
// register, refuse to run, and look like a broken installer.
foreach (var required in new[] { "Secret", "RefreshSecret" })
{
    if (string.IsNullOrWhiteSpace(jwtConfig[required]))
    {
        var message =
            $"Jwt:{required} is not configured, so the API cannot start.\n" +
            "A branch install writes this into appsettings.Production.json during setup; " +
            "a Docker deployment passes it in as an environment variable. " +
            "If you are seeing this on a branch PC, re-run the installer.";

        Log.Fatal(message);
        Console.Error.WriteLine(message);
        return 1;
    }
}

var jwtKey = Encoding.UTF8.GetBytes(jwtConfig["Secret"]!);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    options.UseSecurityTokenValidators = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(jwtKey),
        ValidateIssuer = true,
        ValidIssuer = jwtConfig["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtConfig["Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        RoleClaimType = ClaimTypes.Role,
        NameClaimType = ClaimTypes.Name,
    };

    // Q2 Decision: SignalR JWT via query string for WebSocket connections
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }

            // Tokens live in an HttpOnly cookie so page scripts cannot read them, which
            // also means the browser sends no *Bearer* header. Fall back to the cookie.
            //
            // The test is specifically for a Bearer scheme, not merely for the presence of
            // an Authorization header. The dashboard sits behind an nginx Basic Auth gate,
            // and once the browser has authenticated to that realm it attaches
            // "Authorization: Basic ..." to every request on the origin — including these
            // API calls. Treating any Authorization header as "a token was supplied" means
            // the cookie is never read and the gate's own credential gets handed to the JWT
            // parser, which rejects it: login succeeds, every call afterwards is a 401.
            if (string.IsNullOrEmpty(context.Token))
            {
                var authHeader = context.Request.Headers.Authorization.ToString();
                var hasBearer = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

                if (!hasBearer)
                {
                    var cookieToken = context.Request.Cookies["accessToken"];
                    if (!string.IsNullOrEmpty(cookieToken))
                        context.Token = cookieToken;
                }
            }

            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var revocationService = context.HttpContext.RequestServices.GetRequiredService<ITokenRevocationService>();
            var jti = context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
            var userIdString = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            if (jti != null && Guid.TryParse(userIdString, out var userId))
            {
                var validFrom = context.SecurityToken.ValidFrom;
                var issueTime = validFrom == DateTime.MinValue ? DateTimeOffset.UtcNow : new DateTimeOffset(validFrom, TimeSpan.Zero);
                if (await revocationService.IsTokenRevokedAsync(jti, userId, issueTime))
                {
                    context.Fail("Token has been revoked.");
                }
            }
        }
    };
});

// ── 3. Authorization Policies (SOP §5 + §19) ──
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdminOnly", policy =>
        policy.RequireClaim(ClaimTypes.Role, Roles.SuperAdmin));

    options.AddPolicy("AdminOrSuperAdmin", policy =>
        policy.RequireClaim(ClaimTypes.Role, Roles.SuperAdmin, Roles.Admin));

    options.AddPolicy("OperatorOrAdmin", policy =>
        policy.RequireClaim(ClaimTypes.Role, Roles.SuperAdmin, Roles.Admin, Roles.Operator, "Agent"));

    // Dashboard-specific policies
    foreach (var dashboard in new[] { Dashboards.BillingCounter, Dashboards.Sessions, Dashboards.Reservations,
        Dashboards.FoodOrders, Dashboards.CashRegister, Dashboards.CashDesk, Dashboards.Members,
        Dashboards.MenuEditor, Dashboards.MainDashboard, Dashboards.PcStatus, Dashboards.Eod, Dashboards.Settings,
        Dashboards.WalletSettings, Dashboards.MemberValueEdit })
    {
        options.AddPolicy($"Dashboard:{dashboard}", policy =>
            policy.Requirements.Add(new DashboardRequirement(dashboard)));
    }
});
builder.Services.AddSingleton<IAuthorizationHandler, DashboardAuthorizationHandler>();

// ── 4. CORS (maps from Helmet + CORS in Node.js) ──
builder.Services.AddCors(options =>
{
    options.AddPolicy("AppleEsportsCors", policy =>
    {
        policy
            .WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ── 5. Rate Limiting (maps from rateLimit.js) ──
var rateLimitConfig = builder.Configuration.GetSection("RateLimiting");
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitConfig.GetValue("PermitLimit", 100),
                Window = TimeSpan.FromSeconds(rateLimitConfig.GetValue("WindowSeconds", 60)),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5,
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            success = false,
            error = "Rate limit exceeded. Please try again later.",
            code = "RATE_LIMIT",
        }, cancellationToken: cancellationToken);
    };
});

// ── 6. SignalR (Q2: auto-negotiation, WebSocket primary) ──
var signalRBuilder = builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.MaximumReceiveMessageSize = 128 * 1024; // 128 KB
})
.AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var redisConnectionString = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrEmpty(redisConnectionString))
{
    signalRBuilder.AddStackExchangeRedis(redisConnectionString, options =>
    {
        options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("AppleEsportsSignalR");
    });
}

// ── 7. FluentValidation ──
builder.Services.AddValidatorsFromAssemblyContaining<AppleEsportsErp.Application.Validators.Auth.AdminLoginValidator>();

// ── 8. Application Services ──
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>().GetSection("Jwt");
    return new JwtTokenService(
        config["Secret"]!, config["RefreshSecret"]!,
        config["AccessExpiry"] ?? "15m", config["RefreshExpiry"] ?? "7d",
        config["Issuer"] ?? "AppleEsportsErp", config["Audience"] ?? "AppleEsportsErpClient"
    );
});
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IEmailService, EmailService>();
// Resolves this deployment's own public URL for links inside outgoing emails.
builder.Services.AddSingleton<IAppUrlProvider, AppUrlProvider>();
builder.Services.AddScoped<IPcStatusService, PcStatusService>();
builder.Services.AddScoped<ITokenRevocationService, TokenRevocationService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<IHubNotificationService, AppleEsportsErp.Api.Services.HubNotificationService>();

// Sprint 2 Services
builder.Services.AddScoped<IReservationService, ReservationService>();
builder.Services.AddScoped<IMemberService, MemberService>();
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddScoped<IBillingService, BillingService>();
builder.Services.AddScoped<ICreditService, CreditService>();
builder.Services.AddScoped<IFoodOrderService, FoodOrderService>();
builder.Services.AddScoped<ICashRegisterService, CashRegisterService>();
builder.Services.AddScoped<ICashDeskService, CashDeskService>();
builder.Services.AddScoped<IEodService, EodService>();
builder.Services.AddScoped<IPcManagementService, PcManagementService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IReportsService, ReportsService>();
builder.Services.AddScoped<ISystemDesksService, AppleEsportsErp.Infrastructure.Services.SystemDesksService>();
builder.Services.AddScoped<IUnitOfWork, AppleEsportsErp.Infrastructure.Repositories.UnitOfWork>();
builder.Services.AddScoped<ISessionActivityService, SessionActivityService>();
builder.Services.AddScoped<IMaintenanceLogService, MaintenanceLogService>();
builder.Services.AddHostedService<AppleEsportsErp.Api.Services.ReservationBackgroundService>();
builder.Services.AddHostedService<AppleEsportsErp.Api.Services.OpenSessionMonitorService>();
builder.Services.AddHostedService<AppleEsportsErp.Api.Services.FixedDurationSessionMonitorService>();
builder.Services.AddHostedService<AppleEsportsErp.Api.Services.DeferredBillingMonitorService>();
builder.Services.AddHostedService<AppleEsportsErp.Api.Services.SessionActivityCleanupService>();
// Marks live sessions as still running, so a power cut can be told apart from play time.
builder.Services.AddHostedService<AppleEsportsErp.Api.Services.SessionHeartbeatService>();
builder.Services.AddHostedService<AppleEsportsErp.Api.Services.SyncCourierService>();

// Lets a fresh branch take Head Office's identifiers instead of inventing its own, which is
// what makes anything it later reports recognisable at Head Office.
builder.Services.AddHttpClient();
builder.Services.AddScoped<AppleEsportsErp.Api.Services.BranchAdoptionService>();

builder.Services.AddScoped<IOfflineSyncService, OfflineSyncService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IPricingProfileService, PricingProfileService>();
builder.Services.AddScoped<IVersionService, VersionService>();
builder.Services.AddScoped<IOutboxService, OutboxService>();
builder.Services.AddScoped<IEmailQueueService, EmailQueueService>();


// ── 9. Controllers + Swagger ──
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "AppleEsports ERP API", Version = "v2.0" });
    c.AddSecurityDefinition("Bearer", new()
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter JWT token",
    });
    c.AddSecurityRequirement(new()
    {
        {
            new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

// ═══════════════════════════════════════════════
//  MIDDLEWARE PIPELINE
//  SOP order: Helmet → CORS → Auth → RBAC → Branch → Audit → Rate Limit
//  .NET mapping: ExceptionHandler → CORS → Auth → Authz → RateLimiter → Controllers
// ═══════════════════════════════════════════════

var app = builder.Build();

// ── Auto-migrate in Development (SOP §23: Database Architecture) ──
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        if (await db.Database.CanConnectAsync())
        {
            Log.Information("PostgreSQL connection verified ✓");
            await db.Database.MigrateAsync();
            Log.Information("Database migrations applied ✓");
            AppleEsportsErp.Api.DbUpdater.UpdateSchema(app);
            Log.Information("Database schema patches applied ✓");
            AppleEsportsErp.Api.DataSeeder.SeedBranchesAsync(db).GetAwaiter().GetResult();
            Log.Information("Database seeded with default branches and PCs ✓");
        }
        else
        {
            Log.Warning("PostgreSQL not available — skipping migration. Start Docker PostgreSQL to enable.");
        }
    }
    catch (Exception ex)
    {
        Log.Warning("Database migration skipped: {Message} | Inner: {Inner}", ex.Message, ex.InnerException?.Message);
    }
}

// ── Credit back time lost while we were down (power cut, restart, update) ──
// Must happen here, before app.Run() starts the hosted services: the fixed-duration
// monitor auto-stops sessions whose EndTime has passed, and after an outage that
// would close sessions which are only "expired" because the branch had no power.
try
{
    var recoveryLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SessionDowntimeRecovery");
    await AppleEsportsErp.Api.Services.SessionDowntimeRecovery.RunAsync(app.Services, recoveryLogger);
}
catch (Exception ex)
{
    // Never block start-up over this — worst case is that no time is credited back.
    Log.Warning("Session downtime recovery skipped: {Message}", ex.Message);
}

// ── 1. Global Exception Handler (maps from errorHandler.js) ──
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();

// ── Development tools ──
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "AppleEsports ERP API v2.0"));
}

// ── 2. CORS (maps from Helmet + CORS) ──
app.UseCors("AppleEsportsCors");

// ── 3. Authentication (maps from auth.js verifyToken) ──
app.UseAuthentication();

// ── Auto Migrate Database ──
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// ── 4. Authorization (maps from roles.js authorize/requireDashboardAccess) ──
app.UseAuthorization();

// ── 5. Rate Limiting (maps from rateLimit.js) ──
app.UseRateLimiter();

// ── 5b. Serve the dashboard itself, when it has been published alongside the API ──
// A branch install has no nginx: one Windows service answers both the API and the screens,
// which removes a whole moving part from every operator PC. In Docker the client container
// still serves the UI and wwwroot simply does not exist, so this stays inert there.
var dashboardRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(dashboardRoot))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    Log.Information("Serving the dashboard from {Root}", dashboardRoot);
}

// ── 6. Map Controllers (maps from routes/index.js registerRoutes) ──
app.MapControllers();

// ── 7. Map SignalR Hubs (maps from sockets/index.js registerSocketHandlers) ──
app.MapHub<SessionHub>("/hubs/sessions");
app.MapHub<BillingHub>("/hubs/billing");
app.MapHub<ReservationHub>("/hubs/reservations");
app.MapHub<PcStatusHub>("/hubs/pc-status");
app.MapHub<FoodOrderHub>("/hubs/food-orders");
app.MapHub<CashHub>("/hubs/cash");
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<DashboardHub>("/hubs/dashboard");
app.MapHub<PcOverlayHub>("/hubs/pc-overlay");

// The dashboard is a single-page app: refreshing on /app/sessions must return index.html
// rather than a 404, because that route only exists in the browser. Registered after the
// controllers and hubs so it can never shadow a real endpoint.
if (Directory.Exists(dashboardRoot))
{
    app.MapFallbackToFile("index.html");
}

// ── Startup banner ──
app.Lifetime.ApplicationStarted.Register(() =>
{
    Log.Information("╔═══════════════════════════════════════════════╗");
    Log.Information("║   AppleEsports ERP — .NET 8 Enterprise Backend  ║");
    Log.Information("║   Environment: {Env}", app.Environment.EnvironmentName);
    Log.Information("║   Hubs: 7 SignalR endpoints mapped            ║");
    Log.Information("║   Controllers: 15 API controllers mapped      ║");
    Log.Information("╚═══════════════════════════════════════════════╝");
});

app.Run();

// Top-level statements need an explicit success code because the configuration check
// above returns 1 on failure.
return 0;
