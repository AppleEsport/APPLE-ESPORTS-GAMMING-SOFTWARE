using AppleEsportsErp.Application.Interfaces;

namespace AppleEsportsErp.Api.Services;

/// <summary>
/// Reads the address the caller actually used, from the request in flight.
///
/// Sits behind UseForwardedHeaders, so on a deployment fronted by nginx this reports the public
/// address the browser typed rather than the container's own - which is the entire point. Get
/// that wrong and the email link is as dead as the localhost it replaced, just less obviously.
/// </summary>
public class CurrentRequestUrl : ICurrentRequestUrl
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentRequestUrl(IHttpContextAccessor accessor) => _accessor = accessor;

    public string? BaseUrl
    {
        get
        {
            var request = _accessor.HttpContext?.Request;
            if (request is null || !request.Host.HasValue) return null;

            return $"{request.Scheme}://{request.Host.Value}";
        }
    }
}
