namespace LocalWeb.Mcp.Security;

/// <summary>
/// Thrown when a URL fails SSRF validation or could not be resolved. The message
/// is safe to return to the MCP client.
/// </summary>
public sealed class UrlGuardException : LocalWebException
{
    /// <summary>Creates the exception with a user-facing message.</summary>
    /// <param name="message">Safe to return to the MCP client.</param>
    public UrlGuardException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner cause.</summary>
    /// <param name="message">Safe to return to the MCP client.</param>
    /// <param name="innerException">The underlying cause.</param>
    public UrlGuardException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
