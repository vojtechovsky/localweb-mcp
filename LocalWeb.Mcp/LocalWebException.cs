namespace LocalWeb.Mcp;

/// <summary>
/// An expected, user-facing failure in the local web layer. The message is safe
/// to return to the MCP client.
/// </summary>
public class LocalWebException : Exception
{
    public LocalWebException(string message) : base(message)
    {
    }

    public LocalWebException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
