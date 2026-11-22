using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MyGym.Api;
using MyGym.Api.Auth;
using MyGym.Api.Endpoints;
using MyGym.Modules.ExerciseCatalog.Infrastructure;
using MyGym.Modules.Identity.Application;
using MyGym.Modules.Identity.Infrastructure;
using MyGym.Modules.Training.Infrastructure;
using MyGym.Shared.Infrastructure.Auth;
using MyGym.Shared.Infrastructure.Persistence;
using MyGym.SharedKernel;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

// Options — bound and eagerly validated. A misconfigured deployment (e.g. a signing key under
// 32 bytes) refuses to boot rather than booting healthy and failing the first sign-in.
builder.Services.AddValidatedAppSettings(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Modules
builder.Services.AddIdentityInfrastructure(connectionString);
builder.Services.AddCatalogInfrastructure(connectionString);
builder.Services.AddTrainingInfrastructure(connectionString);

// Shared services
builder.Services.AddSingleton<ITelegramInitDataValidator, TelegramInitDataValidator>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// Handlers — registered explicitly; there is no mediator scanning assemblies at startup.
builder.Services.AddScoped<ICommandHandler<AuthenticateWithTelegramCommand, AuthTokensResponse>,
    AuthenticateWithTelegramCommandHandler>();
builder.Services.AddScoped<ICommandHandler<RefreshTokensCommand, AuthTokensResponse>,
    RefreshTokensCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetProfileQuery, ProfileResponse>, GetProfileQueryHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateProfileCommand, ProfileResponse>, UpdateProfileCommandHandler>();

// Parsed once, here, as a strongly-typed JwtOptions — not via raw IConfiguration indexers — so a
// missing key fails loudly with a message naming the configuration section instead of an NRE from
// a null-forgiving `!`. This runs at registration time, before AddValidatedAppSettings' eager
// ValidateOnStart gets a chance to run, so it is this parse (not that validation) that is the
// actual backstop for a missing Jwt section at this point in startup.
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException(
        $"Configuration section '{JwtOptions.SectionName}' is required to configure JWT bearer authentication.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // AddJwtBearer maps inbound claims by default, which rewrites the "sub" claim to
        // ClaimTypes.NameIdentifier before the principal is ever handed to application code.
        // Left at its default, the rate limiter's `context.User.FindFirst("sub")` below would
        // always return null for an authenticated request and silently fall back to per-IP
        // limiting — wrong, and invisible without reading the token on the wire. Disabling the
        // mapping keeps "sub" as "sub", which both the rate limiter and ICurrentUser (which also
        // falls back to checking "sub") agree on. This only matters at all because
        // UseRateLimiter() below runs after UseAuthentication()/UseAuthorization() in the request
        // pipeline — reversed, context.User would still be the unauthenticated default principal
        // when the partition callback runs, and no claim-mapping choice could fix that.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetTokenBucketLimiter(
            context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 120,
                TokensPerPeriod = 120,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddOtlpExporter())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddOtlpExporter());

// Request body cap. Nothing this API accepts is legitimately larger than a program with its days,
// so 256 KB is generous; the logger has no upload endpoints at all.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 256 * 1024);

var app = builder.Build();

if (app.Configuration.GetValue("RunMigrationsOnStartup", defaultValue: true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<TrainingDbContext>().Database.MigrateAsync();
    await CatalogSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<CatalogDbContext>(), CancellationToken.None);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// Authentication/authorization must run before the rate limiter: the limiter's partition callback
// reads context.User.FindFirst("sub") (see the MapInboundClaims comment above), and that claim
// only exists once UseAuthentication() has populated the principal. Reversed, every authenticated
// request would see the unauthenticated default principal and silently fall back to per-IP limiting.
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// Auth and profile endpoints (Task 11). Tasks 12-14 append their own MapXEndpoints() calls here.
app.MapAuthEndpoints();
app.MapProfileEndpoints();

app.Run();

public partial class Program;
