namespace LocalWeb.Mcp.Security;

/// <summary>
/// Thrown when a URL fails SSRF validation or could not be resolved. The message
/// is safe to return to the MCP client.
/// </summary>
public sealed class UrlGuardException : LocalWebException
{
    public UrlGuardException(string message) : base(message)
    {
    }

    public UrlGuardException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
