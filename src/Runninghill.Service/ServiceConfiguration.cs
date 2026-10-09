using System.Text;
using Runninghill.Database;

namespace Runninghill.Service;

// Validate once at startup. Refusing bad settings is safer than starting a service that
// looks healthy but cannot store data or check who is allowed to use it.
/// <summary>
/// Holds validated database and authentication settings loaded once during service startup.
/// </summary>
public sealed record ServiceConfiguration(string ConnectionString, string Audience, string? Authority, string? DevelopmentSigningKey, DatabaseSettings Database)
{
    /// <summary>
    /// Validates the database, audience and authentication issuer before startup, allowing a shared
    /// signing key only in Development.
    /// </summary>
    public static ServiceConfiguration Load(IConfiguration configuration, IHostEnvironment environment)
    {
        var database = DatabaseSettings.Parse(configuration["Database:Provider"], configuration.GetConnectionString("Runninghill"));
        var audience = configuration["Authentication:Audience"];
        if (string.IsNullOrWhiteSpace(audience))
            throw new InvalidOperationException("Authentication:Audience is required.");
        // Authority is the trusted login provider. Audience identifies the app the token is for.
        var authority = configuration["Authentication:Authority"];
        var signingKey = configuration["Authentication:DevelopmentSigningKey"];
        if (string.IsNullOrWhiteSpace(authority) &&
            (!environment.IsDevelopment() || string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32))
            throw new InvalidOperationException("Configure an HTTPS Authentication:Authority, or a development signing key of at least 32 bytes in Development.");
        if (!string.IsNullOrWhiteSpace(authority) && (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) || authorityUri.Scheme != "https"))
            throw new InvalidOperationException("Authentication:Authority must be an HTTPS URL.");

        return new ServiceConfiguration(database.ConnectionString, audience, authority, signingKey, database);
    }
}
