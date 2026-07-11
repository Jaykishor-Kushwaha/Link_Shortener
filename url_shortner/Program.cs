using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using StackExchange.Redis;
using UrlShortener.Api.Configuration;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Middleware;
using UrlShortener.Api.Models;
using UrlShortener.Api.Services;

// Load .env file if it exists
var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (!File.Exists(envPath))
{
    envPath = Path.Combine(Directory.GetParent(Directory.GetCurrentDirectory())?.FullName ?? "", ".env");
}

if (File.Exists(envPath))
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
        var parts = line.Split('=', 2);
        if (parts.Length == 2)
        {
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
        }
    }
}

// ── Serilog Bootstrap ──
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .Enrich.FromLogContext()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // ── Configuration ──
    var settings = AppSettings.FromEnvironment();
    builder.Services.AddSingleton(settings);

    // ── MongoDB ──
    builder.Services.AddSingleton<MongoDbContext>();

    // ── Redis ──
    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    {
        var appSettings = sp.GetRequiredService<AppSettings>();
        return ConnectionMultiplexer.Connect(appSettings.RedisConnectionString);
    });
    builder.Services.AddSingleton<RedisService>();

    // ── Click Event Channel (bounded to prevent memory exhaustion) ──
    var clickChannel = Channel.CreateBounded<ClickEvent>(new BoundedChannelOptions(10_000)
    {
        FullMode = BoundedChannelFullMode.DropOldest
    });
    builder.Services.AddSingleton(clickChannel);

    // ── Services ──
    builder.Services.AddScoped<AuthService>();
    builder.Services.AddScoped<UrlService>();
    builder.Services.AddScoped<AnalyticsService>();
    builder.Services.AddHostedService<ClickEventBackgroundService>();

    // ── JWT Authentication ──
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer();

    builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<AppSettings>((options, appSettings) =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "url-shortener",
                ValidateAudience = true,
                ValidAudience = "url-shortener",
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(appSettings.JwtSecret)),
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        });

    builder.Services.AddAuthorization();

    // ── Controllers ──
    builder.Services.AddControllers(options =>
    {
        // Limit request body size to 1 MB to prevent abuse
        options.Filters.Add(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(1_048_576));
    });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "URL Shortener API", Version = "v1" });
        options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Description = "Paste your JWT access token below to authenticate (Swagger will automatically prepend 'Bearer ')."
        });
        options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    // ── CORS for Angular frontend ──
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins("http://localhost:4200", "http://localhost:80", "http://localhost")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    var app = builder.Build();

    // ── Create MongoDB Indexes with retry to handle container startup lag ──
    var mongoContext = app.Services.GetRequiredService<MongoDbContext>();
    var indexCreated = false;
    for (int i = 1; i <= 5; i++)
    {
        try
        {
            await mongoContext.CreateIndexesAsync();
            indexCreated = true;
            Log.Information("MongoDB indexes created successfully.");
            break;
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to connect to MongoDB and create indexes. Retry {Attempt}/5... Error: {Message}", i, ex.Message);
            if (i < 5) await Task.Delay(3000);
        }
    }

    if (!indexCreated)
    {
        Log.Fatal("Could not connect to MongoDB after 5 attempts. Exiting.");
        throw new Exception("MongoDB connection failed.");
    }

    // ── Middleware Pipeline ──
    // Order matters: Exception → CORS → RateLimit → Auth → Controllers
    app.UseMiddleware<GlobalExceptionMiddleware>();

    app.UseSwagger();
    app.UseSwaggerUI();

    app.UseCors();
    app.UseMiddleware<RateLimitMiddleware>();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    var port = settings.Port;
    app.Urls.Clear();
    app.Urls.Add($"http://0.0.0.0:{port}");

    Log.Information("URL Shortener API starting on port {Port}", port);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    try
    {
        System.IO.File.WriteAllText("c:/url_shortner/program_error.txt", ex.ToString());
    }
    catch {}
}
finally
{
    Log.CloseAndFlush();
}

// Partial class to enable WebApplicationFactory<Program> in tests
public partial class Program { }
