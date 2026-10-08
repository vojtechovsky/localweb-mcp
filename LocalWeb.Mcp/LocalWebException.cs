namespace LocalWeb.Mcp;

/// <summary>
/// An expected, user-facing failure in the local web layer. The message is safe
/// to return to the MCP client.
/// </summary>
public class LocalWebException : Exception
{
    /// <summary>Creates the exception with a user-facing message.</summary>
    /// <param name="message">Safe to return to the MCP client.</param>
    public LocalWebException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner cause.</summary>
    /// <param name="message">Safe to return to the MCP client.</param>
    /// <param name="innerException">The underlying cause.</param>
    public LocalWebException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
