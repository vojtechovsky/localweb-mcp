using System.Text.Json.Serialization;

namespace LocalWeb.Mcp.Search;

/// <summary>A single SearXNG search result.</summary>
public sealed class SearchResult
{
    /// <summary>Result title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Result URL.</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>Snippet / short description.</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>Name of the engine that produced the result.</summary>
    [JsonPropertyName("engine")]
    public string Engine { get; set; } = string.Empty;

    /// <summary>Publication date, when the engine supplies one.</summary>
    [JsonPropertyName("publishedDate")]
    public string? PublishedDate { get; set; }
}

/// <summary>The subset of the SearXNG JSON response we care about.</summary>
public sealed class SearchResponse
{
    /// <summary>The query as echoed back by SearXNG.</summary>
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    /// <summary>Approximate total number of results.</summary>
    [JsonPropertyName("number_of_results")]
    public long NumberOfResults { get; set; }

    /// <summary>The returned results.</summary>
    [JsonPropertyName("results")]
    public List<SearchResult> Results { get; set; } = [];

    /// <summary>Query suggestions.</summary>
    [JsonPropertyName("suggestions")]
    public List<string> Suggestions { get; set; } = [];
}
