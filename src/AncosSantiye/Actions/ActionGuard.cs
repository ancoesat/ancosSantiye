using System.Security.Cryptography;
using System.Text;

namespace AncosSantiye.Actions;

/// <summary>
/// The door in front of commit and push. Three checks, all required:
/// the request comes from this page (its own Origin), it is JSON, and it carries the token made at start.
/// A page on another site cannot pass: the custom header forces a CORS preflight that we never answer.
/// A local program can still read the token like the browser does; the agent deny list covers that (see README).
/// </summary>
public sealed class ActionGuard(string token)
{
    public const string Header = "X-Santiye-Token";

    public string Token { get; } = token;

    public static ActionGuard Create() => new(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

    public IResult? Reject(HttpRequest request)
    {
        var own = $"{request.Scheme}://{request.Host.Value}";
        if (request.Headers.Origin is not [{ } origin] || !string.Equals(origin, own, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!request.HasJsonContentType())
        {
            return TypedResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        if (request.Headers[Header] is not [{ } sent]
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sent), Encoding.UTF8.GetBytes(Token)))
        {
            return TypedResults.StatusCode(StatusCodes.Status401Unauthorized);
        }

        return null;
    }
}
