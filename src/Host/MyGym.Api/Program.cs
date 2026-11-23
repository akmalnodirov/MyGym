using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MyGym.Api;
using MyGym.Api.Auth;
using MyGym.Api.Endpoints;
using MyGym.Api.OpenApi;
using MyGym.Modules.ExerciseCatalog.Infrastructure;
using MyGym.Modules.Identity.Application;
using MyGym.Modules.Identity.Infrastructure;
using MyGym.Modules.Training.Application.Programs;
using MyGym.Modules.Training.Application.Sessions;
using MyGym.Modules.Training.Infrastructure;
using MyGym.Shared.Infrastructure.Auth;
using MyGym.Shared.Infrastructure.Persistence;
using MyGym.SharedKernel;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

// Options — bound and eagerly validated. A misconfigured deployment (e.g. a signing key under
// 32 bytes) refuses to boot rather than booting healthy and failing the first sign-in.
builder.Services.AddValidatedAppSettings(builder.Configuration);

// C4: .NET 10's built-in minimal-API validation. Request records across the host carry
// [Required]/[Range]/[EnumDataType] attributes (see each Endpoints/*.cs and *Dtos.cs file); without
// this call those attributes are inert and a null collection or out-of-range enum reaches the
// handler as a NullReferenceException (500) or a silently-accepted default instead of a 400.
builder.Services.AddValidation();

// The Mini App generates its TypeScript request/response types from this document, so a server-side
// rename becomes a client compile error instead of a runtime surprise. Document generation is
// registered unconditionally (it is inert without MapOpenApi) but only served in Development --
// the API surface is not public documentation.
builder.Services.AddOpenApi(options => options.AddSchemaTransformer<NumericSchemaTransformer>());

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
builder.Services.AddScoped<ICommandHandler<DeleteAccountCommand>, DeleteAccountCommandHandler>();

// Task 12: program authoring and the day preview.
builder.Services.AddScoped<ICommandHandler<CreateProgramCommand, ProgramResponse>, CreateProgramCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ReplaceProgramCommand, ProgramResponse>, ReplaceProgramCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ActivateProgramCommand>, ActivateProgramCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ArchiveProgramCommand>, ArchiveProgramCommandHandler>();
builder.Services.AddScoped<ICommandHandler<SetProgramVisibilityCommand>, SetProgramVisibilityCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ForkProgramCommand, ProgramResponse>, ForkProgramCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetProgramQuery, ProgramResponse>, GetProgramQueryHandler>();
builder.Services.AddScoped<IQueryHandler<PreviewProgramDayQuery, DayPreviewResponse>, PreviewProgramDayQueryHandler>();

// Task 13: session endpoints and the critical path. ListSessionsQuery is registered from
// TrainingInfrastructureExtensions.AddTrainingInfrastructure because its handler is internal to
// MyGym.Modules.Training.Infrastructure.
builder.Services.AddScoped<ICommandHandler<StartSessionCommand, SessionResponse>, StartSessionCommandHandler>();
builder.Services.AddScoped<ICommandHandler<LogSetCommand, LoggedSetResponse>, LogSetCommandHandler>();
builder.Services.AddScoped<ICommandHandler<AddAdHocExerciseCommand, AddedExerciseResponse>,
    AddAdHocExerciseCommandHandler>();
builder.Services.AddScoped<ICommandHandler<EditSetCommand>, EditSetCommandHandler>();
builder.Services.AddScoped<ICommandHandler<DeleteSetCommand>, DeleteSetCommandHandler>();
builder.Services.AddScoped<ICommandHandler<CompleteSessionCommand>, CompleteSessionCommandHandler>();
builder.Services.AddScoped<ICommandHandler<AbandonSessionCommand>, AbandonSessionCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetSessionQuery, SessionResponse>, GetSessionQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetCurrentSessionQuery, SessionResponse?>, GetCurrentSessionQueryHandler>();

// Task 14: account deletion and the Telegram bot webhook.
// MiniAppOptions is already bound and eagerly validated by AddValidatedAppSettings above; the
// Configure<MiniAppOptions> call that used to be here was a second, redundant binding of the same
// section (minor fix-before-merge item 5).
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
    new TelegramBotClient(sp.GetRequiredService<IOptions<TelegramAuthOptions>>().Value.BotToken));

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

// I3: spec §11 deploys behind Caddy, so without this, every request's RemoteIpAddress is Caddy's
// own address and the limiter below's IP fallback collapses onto one bucket for every anonymous
// client. Caddy is deployed as a sibling process/container on the same small VPS or its private
// network (never reached across the public internet per spec §11), so trusting the private address
// ranges — rather than one proxy IP that breaks the moment the VPS is rebuilt — is the right-sized
// trust boundary. Added on top of the framework's loopback default rather than replacing it, so
// local/dev (where there is no proxy at all) keeps working unchanged.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("192.168.0.0"), 16));
});

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

// I3: a second, separate RateLimiterOptions — not registered through DI, so it runs as its own
// RateLimitingMiddleware instance placed before UseAuthentication below. The GlobalLimiter
// registered via AddRateLimiter above only ever evaluates after UseAuthorization runs (it sits
// after it in the pipeline), so an unauthenticated flood against a protected endpoint used to never
// reach any limiter at all: AuthorizationMiddleware returns 401 and short-circuits before
// UseRateLimiter() is even reached. This cheap, IP-partitioned limiter sits ahead of
// UseAuthentication specifically to close that gap; the per-user one from AddRateLimiter still runs
// afterward for authenticated traffic. Both are needed — neither alone covers both failure modes.
var anonymousRateLimiterOptions = new RateLimiterOptions { RejectionStatusCode = StatusCodes.Status429TooManyRequests };
anonymousRateLimiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    RateLimitPartition.GetTokenBucketLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 120,
            TokensPerPeriod = 120,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

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
    var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    var training = scope.ServiceProvider.GetRequiredService<TrainingDbContext>();

    // Promoted minor: migrations and seeding ran at every startup immediately after three ungated
    // MigrateAsync() calls, with no coordination between instances. Two instances starting
    // together — a rolling deploy, a restart loop, a second replica — both compute CatalogSeeder's
    // "missing" set, both insert, and one dies on ix_exercises_slug: a boot crash loop. A Postgres
    // advisory lock serialises "one instance migrates and seeds at a time" across however many
    // processes reach this block concurrently. It is session-scoped on this connection and releases
    // automatically when the connection closes at the end of this using scope, so there is no
    // explicit unlock to forget.
    const long StartupAdvisoryLockKey = 2026_11_21;
    await identity.Database.OpenConnectionAsync();
    await identity.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({StartupAdvisoryLockKey})");

    await identity.Database.MigrateAsync();
    await catalog.Database.MigrateAsync();
    await training.Database.MigrateAsync();
    await CatalogSeeder.SeedAsync(catalog, CancellationToken.None);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// I3: must run before anything reads RemoteIpAddress — both rate limiters below, and the
// forwarded-headers middleware itself needs to run before authentication/authorization too, since
// neither depends on the client address but ordering it earliest keeps every later middleware
// consistent about which address is "the client".
app.UseForwardedHeaders();

// I3: a cheap, IP-partitioned limiter ahead of authentication, so an unauthenticated flood against
// a protected endpoint is bounded even though AuthorizationMiddleware below will reject it with a
// 401 before the per-user limiter ever runs. See the two RateLimiterOptions built above for why
// this has to be a second, separately-positioned middleware instance rather than one GlobalLimiter.
app.UseRateLimiter(anonymousRateLimiterOptions);

// Authentication/authorization must run before the per-user rate limiter: its partition callback
// reads context.User.FindFirst("sub") (see the MapInboundClaims comment above), and that claim
// only exists once UseAuthentication() has populated the principal. Reversed, every authenticated
// request would see the unauthenticated default principal and silently fall back to per-IP limiting.
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// I12: must run ahead of routing/endpoint dispatch so the secret check happens before the
// webhook's Update parameter is model-bound. See UseTelegramWebhookSecretGuard's own doc comment
// for why an endpoint filter could not do this.
app.UseTelegramWebhookSecretGuard();

// Served only in Development, at /openapi/v1.json. The Mini App's `npm run api:types` reads it
// from a locally running host; CI reads it from `dotnet run` against the Development environment.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous().ExcludeFromDescription();

// Auth and profile endpoints (Task 11). Tasks 12-14 append their own MapXEndpoints() calls here.
app.MapAuthEndpoints();
app.MapProfileEndpoints();

// Program authoring and the day preview; exercise search (Task 12).
app.MapProgramEndpoints();
app.MapExerciseEndpoints();

// Session endpoints and the critical path (Task 13).
app.MapSessionEndpoints();

// Telegram bot webhook (Task 14).
app.MapTelegramWebhook();

app.Run();

public partial class Program;
