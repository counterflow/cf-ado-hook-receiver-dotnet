using System.Security.Cryptography;
using System.Text;
using AdoHookReceiver.Options;
using Microsoft.Extensions.Options;

namespace AdoHookReceiver.Hooks;

/// <summary>
/// Checks the <c>Authorization: Basic</c> header ADO sends against the configured credentials.
/// Runs before the body is read.
/// </summary>
public sealed class BasicAuthFilter : IEndpointFilter
{
    private const string Challenge = "Basic realm=\"ado-hooks\"";

    private readonly byte[] _usernameHash;
    private readonly byte[] _passwordHash;
    private readonly ILogger<BasicAuthFilter> _logger;

    public BasicAuthFilter(IOptions<AdoHookOptions> options, ILogger<BasicAuthFilter> logger)
    {
        _usernameHash = Hash(options.Value.Username);
        _passwordHash = Hash(options.Value.Password);
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (IsAuthorized(http.Request.Headers.Authorization.ToString()))
        {
            return await next(context);
        }

        HookLog.Unauthorized(_logger, http.Connection.RemoteIpAddress?.ToString(), http.Request.Path);
        http.Response.Headers.WWWAuthenticate = Challenge;
        return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
    }

    private bool IsAuthorized(string header)
    {
        const string scheme = "Basic ";
        if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var encoded = header.AsSpan(scheme.Length).Trim();
        var decoded = new byte[encoded.Length];
        if (!Convert.TryFromBase64Chars(encoded, decoded, out var length))
        {
            return false;
        }

        var credentials = Encoding.UTF8.GetString(decoded, 0, length);
        var colon = credentials.IndexOf(':');
        if (colon < 0)
        {
            return false;
        }

        // Compare fixed-length hashes so neither the result nor its timing reveals the length,
        // and check both values so a wrong username takes as long as a wrong password.
        var usernameMatches = CryptographicOperations.FixedTimeEquals(Hash(credentials[..colon]), _usernameHash);
        var passwordMatches = CryptographicOperations.FixedTimeEquals(Hash(credentials[(colon + 1)..]), _passwordHash);
        return usernameMatches & passwordMatches;
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
