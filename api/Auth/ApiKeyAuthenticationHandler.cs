using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text;
using api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace api.Auth;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";

    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AppDbContext dbContext,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _dbContext = dbContext;
        _configuration = configuration;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("x-api-key", out var values) || values.Count == 0)
        {
            return AuthenticateResult.NoResult();
        }

        var providedKey = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedKey))
        {
            return AuthenticateResult.Fail("x-api-key header is empty.");
        }

        var masterKey = _configuration["Security:MasterKey"];
        if (!string.IsNullOrWhiteSpace(masterKey) && string.Equals(providedKey, masterKey, StringComparison.Ordinal))
        {
            var masterClaims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "master"),
                new Claim(ClaimTypes.Name, "MasterKey"),
                new Claim("master_key", "true")
            };

            var masterIdentity = new ClaimsIdentity(masterClaims, SchemeName);
            var masterPrincipal = new ClaimsPrincipal(masterIdentity);
            var masterTicket = new AuthenticationTicket(masterPrincipal, SchemeName);
            return AuthenticateResult.Success(masterTicket);
        }

        var providedKeyHash = ComputeSha256(providedKey);

        var apiKey = await _dbContext.ApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsActive && x.KeyHash == providedKeyHash);

        if (apiKey is null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, apiKey.Id.ToString()),
            new Claim(ClaimTypes.Name, apiKey.Name),
            new Claim("master_key", "false")
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }

    public static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
