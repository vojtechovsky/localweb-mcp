# LocalWeb MCP

A small, local MCP server that replaces a paid web-search service. It exposes
four tools — `web_search`, `web_fetch`, `web_render`, `web_extract_links` —
backed by a self-hosted
[SearXNG](https://docs.searxng.org/) instance, plain HTTP, a headless browser
(Playwright), a TTL cache and SSRF protection.

It is meant to be the cheap local fallback next to a hosted provider such as
Exa, which stays configured as its own MCP server in the client.

## Architecture

```text
MCP client
  ├── Exa MCP (hosted, external)          # preferred, if available
  └── LocalWeb MCP (this project, stdio)
        ├── web_search        → cache → SearXNG JSON API
        ├── web_fetch         → cache → UrlGuard → HTTP → (fallback) browser
        ├── web_render        → cache → UrlGuard → browser (forced)
        └── web_extract_links → cache → UrlGuard → HTTP → (fallback) browser → links
```

Because browser switches and caching must be deterministic, they live in this C#
server rather than in prompt instructions.

## Requirements

- .NET SDK 10 (project targets `net10.0`)
- Docker + Docker Compose (for SearXNG)
- Linux/Windows/macOS; Chromium is installed by Playwright

## 1. Run SearXNG

The infra files live in [`infra/searxng`](infra/searxng). They follow the
official container template.

```bash
cd infra/searxng
cp .env.example .env

# Put the same secret in .env (SEARXNG_SECRET) and core-config/settings.yml
# (server.secret_key). The environment variable wins.
openssl rand -hex 32

docker compose up -d

# Acceptance: the JSON API must return a "results" array.
curl "http://127.0.0.1:8080/search?q=test&format=json"
```

A `403` means `json` is missing from `search.formats` in
`core-config/settings.yml` (or the config was not reloaded). The compose file
binds the port to `127.0.0.1` only.

## 2. Build and install the browser

```bash
dotnet build LocalWeb.Mcp/LocalWeb.Mcp.csproj

# Install Chromium. On Linux without PowerShell, use the maintenance flag:
dotnet run --project LocalWeb.Mcp -- --install-browser

# If OS packages are missing (needs root), also run:
sudo dotnet run --project LocalWeb.Mcp -- --install-browser-deps
```

> With PowerShell available, the upstream command also works:
> `pwsh LocalWeb.Mcp/bin/Debug/net10.0/playwright.ps1 install chromium`.

## 3. Configure your MCP client

```json
{
  "mcpServers": {
    "exa": {
      "type": "streamable-http",
      "url": "https://mcp.exa.ai/mcp"
    },
    "localweb": {
      "command": "dotnet",
      "args": ["run", "--project", "/absolute/path/to/LocalWeb.Mcp"]
    }
  }
}
```

Then add the policy from [`AGENTS.md`](AGENTS.md): prefer Exa, fall back to
`localweb` on rate limits or outages.

## Tools

| Tool | Description |
| --- | --- |
| `web_search(query, maxResults=0, language="auto", page=1, bypassCache=false)` | Search via SearXNG. Returns titles, URLs and snippets. `maxResults=0` uses the configured default. |
| `web_fetch(url, bypassCache=false)` | Fetch a page as Markdown. HTTP first, browser fallback for JavaScript pages. |
| `web_render(url, bypassCache=false)` | Render a page in a headless browser and return Markdown. |
| `web_extract_links(url, maxLinks=50, bypassCache=false)` | Return the http(s) links found on a page as Markdown. |

Expected failures are reported as MCP errors (`isError = true`) with a
human-readable message, so a client can tell a failure apart from page content.

## Configuration

Bound from the `LocalWeb` section of `appsettings.json`, overridable via
environment variables (`LocalWeb__SearxngUrl=...`) and command-line options.

| Setting | Default | Notes |
| --- | --- | --- |
| `SearxngUrl` | `http://127.0.0.1:8080` | SearXNG base URL. |
| `CachePath` | `cache.db` | SQLite cache file. |
| `SearchTtlMinutes` | `180` | Search result TTL. |
| `FetchTtlMinutes` | `720` | Page TTL. |
| `ErrorTtlMinutes` | `2` | TTL for cached errors. |
| `MaxResponseBytes` | `5000000` | HTTP body cap. |
| `MaxOutputChars` | `20000` | Returned content cap. |
| `HttpTimeoutSeconds` | `20` | |
| `BrowserTimeoutSeconds` | `30` | |
| `MaxRedirects` | `5` | Each redirect target is re-validated. |
| `MaxConcurrentBrowserPages` | `2` | |
| `AllowedSchemes` | `["http","https"]` | A configured list **replaces** the default (e.g. `["https"]` narrows egress). |
| `AllowedPorts` | `[80,443]` | A configured list **replaces** the default. |
| `DefaultMaxResults` | `8` | Default for `web_search` when `maxResults` is `0`. |
| `MaxMaxResults` | `20` | Upper bound `web_search` clamps to. |
| `MinExtractCharsForBrowser` | `400` | Extracted text below this triggers the browser fallback. |
| `UserAgent` | `LocalWebMcp/1.0` | |
| `AllowLoopbackForTests` | `false` | **Testing only.** Disables SSRF blocking for loopback; logs a warning at startup when enabled. |

Command-line options: `--searxng-url`, `--cache-path`, `--log-level`,
`--http-timeout`, `--browser-timeout`, `--install-browser`,
`--install-browser-deps`. Unknown arguments are ignored, and help/errors are
written to stderr.

Example:

```bash
dotnet run --project LocalWeb.Mcp -- --searxng-url http://127.0.0.1:8080 --log-level Debug
```

## Security

`UrlGuard` validates every outbound request:

- only allowed schemes and ports;
- no URLs containing credentials;
- DNS is resolved and **every** address is checked;
- loopback, link-local (including `169.254.169.254`), private ranges
  (`10/8`, `172.16/12`, `192.168/16`), `100.64/10`, multicast, unspecified and
  their IPv6 equivalents (`::1`, `fc00::/7`, `fe80::/10`, IPv4-mapped IPv6, and
  the NAT64/6to4/Teredo/IPv4-compatible forms) are rejected;
- resolved addresses are never echoed back to the client.

For the HTTP path specifically:

- redirects are followed manually and re-validated at every hop;
- the socket is opened against the exact validated IP (`ConnectCallback`), which
  closes the DNS-rebinding window, and HTTP proxies are disabled so the pinning
  cannot be silently bypassed;
- the whole request (connect, headers **and body**) is bounded by
  `HttpTimeoutSeconds`, and the body is capped at `MaxResponseBytes`.

The browser path (`web_render` / the `web_fetch` fallback) is a check, not a hard
pin, and has residual risk you should be aware of when rendering untrusted URLs:

- requests are intercepted at the context level, service workers are blocked and
  WebSockets are disabled; images/media/fonts are dropped for speed;
- the final page URL is re-validated after navigation (redirect targets are not
  seen by the router);
- **limitation:** Chromium resolves the host itself when it connects, so unlike
  the HTTP path the browser cannot be pinned to the validated IP; a DNS-rebinding
  answer could still reach an internal address. Chromium also runs with
  `--no-sandbox` (needed in most containers), so treat browser rendering of
  hostile pages as best-effort isolation.

All web-derived text (titles, snippets, link text and URLs) is escaped before it
is embedded in the Markdown returned to the model.

## Tests

```bash
dotnet test                     # unit tests only
LOCALWEB_INTEGRATION=1 dotnet test   # + integration tests (needs Chromium, internet)
```

- **Unit tests** cover `UrlGuard`, the cache, the content extractor, the link
  extractor, the SearXNG client and the output formatter.
- **Local integration tests** (`IntegrationTests`) start a small Kestrel server
  and exercise the HTTP path, the browser fallback, cache reuse, the loopback
  block and the browser page limit. They need an installed Chromium.
- **Live web tests** (`LiveWebTests`) fetch real, well-known pages and assert on
  known information:
  1. a static page is fetched over HTTP (IANA "Example Domains");
  2. links on that static page include the RFC 2606 reference;
  3. a JavaScript-rendered page (Playwright's TodoMVC) falls back to the browser
     and yields the rendered content;
  4. `web_render` forces the browser on a dynamic page.

  Install the browser once with `dotnet run --project LocalWeb.Mcp -- --install-browser`.

Run only the live tests with:

```bash
LOCALWEB_INTEGRATION=1 dotnet test --filter "FullyQualifiedName~LiveWebTests"
```

These tests reach out to third-party sites, so they can be affected by network
availability or upstream page changes. SearXNG is only needed to exercise
`web_search`.

## Docker

```bash
docker build -t localweb-mcp .
docker run --rm -i --network host localweb-mcp
```

The image installs Chromium and its OS dependencies and runs as a non-privileged
user. Use `--network host` (or point `LocalWeb__SearxngUrl` at a reachable
SearXNG) so the container can reach the local instance.

## Known limitations

- SearXNG has no index of its own; result quality depends on the upstream
  engines you enable and on the network.
- Only `text/html`, `application/xhtml+xml` and `text/plain` are supported.
  PDF is not handled yet.
- The cache is process-local SQLite (WAL). Use a networked store if you run
  multiple instances.
- The browser resource filter makes one DNS check per subresource, which trades
  a little speed for SSRF safety.
