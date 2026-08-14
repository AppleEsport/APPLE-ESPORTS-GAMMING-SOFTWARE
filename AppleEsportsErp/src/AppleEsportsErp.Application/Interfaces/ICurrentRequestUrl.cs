namespace AppleEsportsErp.Application.Interfaces;

/// <summary>
/// The address the caller reached this server on, when there is a caller.
///
/// Exists so <see cref="IAppUrlProvider"/> can build a working email link without Infrastructure
/// having to know what an HTTP request is. Infrastructure has no ASP.NET Core dependency and is
/// better off without one for a single property, so the web layer answers this and Infrastructure
/// just asks.
/// </summary>
public interface ICurrentRequestUrl
{
    /// <summary>
    /// Scheme and host as the caller sees them - "https://erp.example.com" - or null when there
    /// is no request in progress, which is the case for anything a background job sends.
    /// </summary>
    string? BaseUrl { get; }
}
