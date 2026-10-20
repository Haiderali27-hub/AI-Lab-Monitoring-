using System.Text;
using System.Threading.RateLimiting;
using Backend_API.Data;
using Backend_API.Helpers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// E2E mode (Playwright): a throwaway in-memory database, seeded on startup, so browser/API
// test runs can never touch the real (Neon) database. Never allowed in Production.
var e2eInMemory = builder.Configuration.GetValue("E2E:InMemory", false);
if (e2eInMemory && builder.Environment.IsProduction())
    throw new InvalidOperationException("E2E:InMemory must not be enabled in Production.");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (e2eInMemory)
    {
        options.UseInMemoryDatabase("SmartExamE2E");
    }
    else if (builder.Configuration["UseInMemoryDatabase"] != "true")
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    }
});

// ── Auth ─────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<JwtHelper>();
builder.Services.AddScoped<Backend_API.Services.AuditService>();
// Module 1 — rotating refresh tokens (hashed at rest, reuse detection)
builder.Services.AddScoped<Backend_API.Services.RefreshTokenService>();
builder.Services.AddScoped<Backend_API.Services.SubmissionService>();
// Module 6 — times out sessions still in progress past their deadline + grace (config: Submission:TimeoutSweepSeconds)
builder.Services.AddHostedService<Backend_API.Services.SessionTimeoutService>();
// Module 9 — seat enforcement (config: SeatMatch = MachineName | Ip | Either)
builder.Services.AddScoped<Backend_API.Services.SeatGuard>();
// Module 7/10 — who may supervise which exam (owner teacher, assigned proctor, admin)
builder.Services.AddScoped<Backend_API.Services.ExamAccessService>();
// …and which live connections joined which exam group, so withdrawn access is enforced on open pages too
builder.Services.AddSingleton<Backend_API.Services.ExamGroupTracker>();

// Module 4 — Analytics
builder.Services.AddScoped<Backend_API.Services.Analytics.AnalyticsService>();

// Module 6 — Notifications
builder.Services.AddScoped<Backend_API.Services.Notifications.EmailService>();
builder.Services.AddScoped<Backend_API.Services.Notifications.NotificationService>();

// Serve static PDF report files
builder.Services.AddDirectoryBrowser();

builder.Services.AddSignalR();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!))
        };
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
                return Task.CompletedTask;
            },
            // Enforce server-side session state so logout / force-logout / deactivation
            // actually invalidate a still-unexpired token (the JWT alone can't be recalled).
            OnTokenValidated = async context =>
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var jti = context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
                var sub = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                if (jti is not null)
                {
                    var session = await db.UserSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Jti == jti);
                    if (session is not null && session.IsRevoked)
                    {
                        context.Fail("Session revoked.");
                        return;
                    }
                }

                if (Guid.TryParse(sub, out var userId))
                {
                    var isActive = await db.Users.AsNoTracking().Where(u => u.UserId == userId).Select(u => u.IsActive).FirstOrDefaultAsync();
                    if (!isActive) context.Fail("Account inactive.");
                }
            }
        };
    });

builder.Services.AddAuthorization();

// ── Rate limiting (brute-force protection on login endpoints) ────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:LoginPermitLimit", 10),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    // Refresh is anonymous too; a client legitimately refreshes about once per access-token lifetime.
    options.AddPolicy("refresh", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:RefreshPermitLimit", 30),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ── CORS (allow React dev server) ─────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ── Controllers + Swagger ─────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SmartExam API", Version = "v1" });

    // Add JWT input to Swagger UI
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste your JWT token here (without 'Bearer ' prefix — Swagger adds it automatically)"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ── Seed Database ─────────────────────────────────────────────────────────────
// Demo accounts have well-known passwords, so seeding defaults to Development only.
// Set "Seed:DemoData": true to seed elsewhere (e.g. a one-off demo machine).
var seedDemoData = app.Configuration.GetValue("Seed:DemoData", app.Environment.IsDevelopment());
// (UseInMemoryDatabase=true is the xUnit factory, which seeds its own data; E2E mode always seeds.)
if (e2eInMemory || (seedDemoData && app.Configuration["UseInMemoryDatabase"] != "true"))
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await DbSeeder.SeedAsync(db);
    }
}

// Module 7: first SuperAdmin for a database that has none (configured via environment, see SuperAdminBootstrap).
var bootstrapEmail = app.Configuration["Bootstrap:SuperAdminEmail"];
if (!string.IsNullOrWhiteSpace(bootstrapEmail))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var outcome = await SuperAdminBootstrap.EnsureAsync(db, bootstrapEmail, app.Configuration["Bootstrap:SuperAdminPassword"]);
    app.Logger.LogInformation("SuperAdmin bootstrap: {Outcome}", outcome);
}

// ── Middleware Pipeline ───────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHub<Backend_API.Hubs.MonitoringHub>("/hubs/monitoring");

app.Run();

public partial class Program { }
