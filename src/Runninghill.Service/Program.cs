using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Runninghill.Application;
using Runninghill.Contracts;
using Runninghill.Database;
using Runninghill.Service;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddSingleton(services => ServiceConfiguration.Load(
    services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IHostEnvironment>()));
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
var exportTelemetry = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Runninghill.Service"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation().AddSource("Npgsql");
        if (exportTelemetry) tracing.AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation().AddMeter("Microsoft.AspNetCore.Server.Kestrel", "Npgsql");
        if (exportTelemetry) metrics.AddOtlpExporter();
    });
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default));
builder.Services.AddSingleton<NpgsqlDataSource>(services => new NpgsqlSlimDataSourceBuilder(
    services.GetRequiredService<ServiceConfiguration>().ConnectionString).Build());
builder.Services.AddSingleton<IDatabaseReadiness, PostgresReadiness>();
builder.Services.AddTransient<IRunninghillApplication, RunninghillApplication>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<ServiceConfiguration>((options, configuration) =>
{
    options.Audience = configuration.Audience;
    options.MapInboundClaims = false;
    if (!string.IsNullOrWhiteSpace(configuration.Authority))
        options.Authority = configuration.Authority;
    else
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = "runninghill-development",
            ValidateAudience = true, ValidAudience = configuration.Audience,
            ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration.DevelopmentSigningKey!)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };
});
builder.Services.AddAuthorizationBuilder().AddPolicy("status.read", policy =>
    policy.RequireAuthenticatedUser().RequireAssertion(context => context.User.FindAll("scope")
        .Any(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("status.read"))));
builder.Services.AddGrpc(options => options.EnableDetailedErrors = false);
builder.Services.AddProblemDetails();
builder.Services.AddRequestTimeouts();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddConcurrencyLimiter("api", limits => { limits.PermitLimit = 64; limits.QueueLimit = 0; });
});
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"], timeout: TimeSpan.FromSeconds(6));

var app = builder.Build();
// Resolve validated settings before opening listeners; never silently use a fallback.
_ = app.Services.GetRequiredService<ServiceConfiguration>();
_ = app.Services.GetRequiredService<NpgsqlDataSource>();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseRequestTimeouts();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapGet("/api/status", async (IRunninghillApplication application, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(new StatusResponse(await application.GetStatusAsync(cancellationToken)));
    }
    catch (ApplicationUnavailableException)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "The application is temporarily unavailable.");
    }
}).RequireAuthorization("status.read").RequireRateLimiting("api").WithRequestTimeout(TimeSpan.FromSeconds(10));
app.MapGrpcService<ApplicationGrpcService>().RequireAuthorization("status.read").RequireRateLimiting("api");
app.Run();

public partial class Program;
