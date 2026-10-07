using System.Text;
using Npgsql;

namespace Runninghill.Service;

public sealed record ServiceConfiguration(string ConnectionString, string Audience, string? Authority, string? DevelopmentSigningKey)
{
    public static ServiceConfiguration Load(IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("Runninghill");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Runninghill is required; no temporary database fallback is allowed.");
        var connectionSettings = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(connectionSettings.Database))
            throw new InvalidOperationException("The PostgreSQL database name must be explicitly configured.");
        var audience = configuration["Authentication:Audience"];
        if (string.IsNullOrWhiteSpace(audience))
            throw new InvalidOperationException("Authentication:Audience is required.");
        var authority = configuration["Authentication:Authority"];
        var signingKey = configuration["Authentication:DevelopmentSigningKey"];
        if (string.IsNullOrWhiteSpace(authority) &&
            (!environment.IsDevelopment() || string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32))
            throw new InvalidOperationException("Configure an HTTPS Authentication:Authority, or a development signing key of at least 32 bytes in Development.");
        if (!string.IsNullOrWhiteSpace(authority) && (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) || authorityUri.Scheme != "https"))
            throw new InvalidOperationException("Authentication:Authority must be an HTTPS URL.");

        return new ServiceConfiguration(connectionString, audience, authority, signingKey);
    }
}
