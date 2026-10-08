using LocalWeb.Mcp.Cache;
using LocalWeb.Mcp.Fetch;
using LocalWeb.Mcp.Search;
using LocalWeb.Mcp.Security;
using Microsoft.Extensions.DependencyInjection;

namespace LocalWeb.Mcp.Composition;

/// <summary>
/// Composition root for the local web layer. Kept in one place so the host and
/// the tests register an identical graph and cannot drift apart.
/// </summary>
public static class LocalWebServiceCollectionExtensions
{
    /// <summary>Registers the local web services (all singletons).</summary>
    public static IServiceCollection AddLocalWebServices(this IServiceCollection services)
    {
        services.AddSingleton<UrlGuard>();
        services.AddSingleton<SqliteCache>();
        services.AddSingleton<SearxngClient>();
        services.AddSingleton<HttpFetcher>();
        services.AddSingleton<PlaywrightFetcher>();
        services.AddSingleton<ContentExtractor>();
        services.AddSingleton<LinkExtractor>();
        services.AddSingleton<PageFetcher>();
        return services;
    }
}
