using System.Text.Json.Serialization;

namespace LocalWeb.Mcp.Search;

/// <summary>A single SearXNG search result.</summary>
public sealed class SearchResult
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("engine")]
    public string Engine { get; set; } = string.Empty;

    [JsonPropertyName("publishedDate")]
    public string? PublishedDate { get; set; }
}

/// <summary>The subset of the SearXNG JSON response we care about.</summary>
public sealed class SearchResponse
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("number_of_results")]
    public long NumberOfResults { get; set; }

    [JsonPropertyName("results")]
    public List<SearchResult> Results { get; set; } = [];

    [JsonPropertyName("suggestions")]
    public List<string> Suggestions { get; set; } = [];
}
