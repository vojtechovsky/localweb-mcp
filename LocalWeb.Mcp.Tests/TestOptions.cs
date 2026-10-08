using LocalWeb.Mcp.Options;
using Microsoft.Extensions.Options;

namespace LocalWeb.Mcp.Tests;

internal static class TestOptions
{
    public static IOptions<LocalWebOptions> Create(Action<LocalWebOptions>? configure = null)
    {
        var options = new LocalWebOptions
        {
            // Keep tests hermetic: never write a cache file into the repo.
            CachePath = Path.Combine(Path.GetTempPath(), "localweb-mcp-tests", Guid.NewGuid() + ".db"),
        };

        configure?.Invoke(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }
}
