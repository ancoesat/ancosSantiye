namespace AncosSantiye.Adapters;

/// <summary>
/// Guards the endpoints that accept writes. Claude Code posts JSON without an Origin header.
/// A web page in the browser always sends Origin, and cannot send application/json across sites
/// without a CORS preflight we never answer. So: no Origin, JSON only.
/// </summary>
public static class LocalOnly
{
    public static IResult? Reject(HttpRequest request)
    {
        if (request.Headers.Origin.Count > 0)
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!request.HasJsonContentType())
        {
            return TypedResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        return null;
    }
}
