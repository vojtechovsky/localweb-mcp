# Findings — localweb-mcp (2026-10-08)

> Adapted `pipeline-code-review` run. This is the machine-verified output; the
> round state lives in `docs/review-sessions/2026-10-08-localweb-mcp-rounds.md`.

- **Protocol:** pipeline-code-review (adapted for localweb-mcp).
- **Round log:** `docs/review-sessions/2026-10-08-localweb-mcp-rounds.md`.
- **Rounds run:** `2`.
- **Baseline commit:** `c139baca3890be807b9e9d53c4006cf6853f6de5`.
- **Build / tests at close:** `dotnet build LocalWeb.Mcp.slnx` → pass (0 warnings,
  XML docs enforced via `GenerateDocumentationFile`);
  `dotnet test` → 77 passed / 7 skipped; `LOCALWEB_INTEGRATION=1 dotnet test` → 84 passed.
- **Outcome:** all H1–H3 fixed or explicitly documented as residual; H4 addressed
  except the residual browser DNS-rebinding note.

## Consolidation

Five role agents (R1 correctness, R2 security/SSRF, R4 async/resilience,
R5 contracts/tests/MCP, R8 architecture/clean-code/annotations) reviewed the
whole solution independently and verified claims against the code with runtime
probes. Findings were deduplicated by location + topic; severities agreed by
consensus. The three highest-severity clusters (browser SSRF, error signalling,
resilience) were independently reported by 2–3 agents each.

## Fixed

### F1 — Browser SSRF: redirects, service workers and WebSockets bypassed the guard (H1)

- **Location:** `LocalWeb.Mcp/Fetch/PlaywrightFetcher.cs`.
- **Issue:** `page.RouteAsync` does not see redirect hops, service-worker requests
  or WebSockets, and popups were not covered. A page could reach
  `169.254.169.254`/loopback and the rendered DOM was returned.
- **Fix:** intercept at the **context** level, `ServiceWorkers = Block`, block all
  WebSockets, and re-validate `page.Url` after navigation.
- **Verification:** `LOCALWEB_INTEGRATION=1 dotnet test` (browser fallback + forced
  render still pass). **Residual:** the redirect request still fires before the
  final-URL check; see Open O1/O2.

### F2/F3 — Infrastructure exceptions escaped the tools (H2)

- **Location:** `PlaywrightFetcher`, `PageFetcher`, `Actions/*`, `HttpFetcher`.
- **Issue:** only `LocalWebException` was caught; `PlaywrightException` (Chromium
  not installed), `JsonException` (corrupt cache), `IOException` and
  `SocketException` surfaced as opaque/generic errors and were not cached.
- **Fix:** wrap `PlaywrightException` in `PlaywrightFetcher`; treat corrupt cache
  rows as misses; wrap body-read failures; add a last-resort `catch` in each
  action that logs and returns a safe message.
- **Verification:** build + unit tests; `ActionErrorTests`.

### F4 — Tool failures were reported as successful results (H2)

- **Location:** `Actions/*.cs`, `Actions/OutputFormatter.cs`.
- **Issue:** expected failures were returned as a normal `"Error: …"` string, so
  `isError` stayed `false` and clients could not detect failure.
- **Fix:** throw `McpException` for expected and unexpected failures; removed the
  `FormatError` helper.
- **Verification:** `ActionErrorTests` assert `McpException`.

### F5 — HTTP body read was unbounded in time and not size-limited for SearXNG (H2/H3)

- **Location:** `Fetch/HttpFetcher.cs`, `Search/SearxngClient.cs`.
- **Issue:** `HttpClient.Timeout` stops applying once headers are read with
  `ResponseHeadersRead`, so a stalled body hung forever; SearXNG's body was read
  without the `MaxResponseBytes` cap.
- **Fix:** a linked `CancellationTokenSource.CancelAfter(HttpTimeoutSeconds)`
  around the whole request (headers **and** body), and a bounded read for SearXNG.
- **Verification:** build + probes; unit tests.

### F6 — HTTP proxy silently defeated DNS pinning (H2)

- **Location:** `Fetch/HttpFetcher.cs`.
- **Issue:** `SocketsHttpHandler.UseProxy` defaults to `true`; with a proxy the
  `ConnectCallback` validated the proxy, not the target, reopening DNS rebinding.
- **Fix:** `UseProxy = false`.

### F7 — Browser render ignored cancellation and could leak page slots (H2)

- **Location:** `Fetch/PlaywrightFetcher.cs`.
- **Issue:** the token was used only for the semaphore; `context.DisposeAsync()`
  throwing skipped `_pageLimit.Release()`; a disconnected browser was replaced
  without disposal; `DisposeAsync` raced in-flight renders.
- **Fix:** close the context on cancellation; release the slot in a nested
  `finally`; dispose a disconnected browser before relaunch; drain in-flight
  renders in `DisposeAsync` and stop disposing the semaphores under waiters.

### F8 — `SqliteCache.DisposeAsync` gate dance (H2)

- **Location:** `Cache/SqliteCache.cs`.
- **Issue:** it acquired then immediately released the gate and disposed the
  semaphore, stranding or faulting waiters; `_disposed` was never checked.
- **Fix:** guard each operation with `ObjectDisposedException.ThrowIf`; on dispose,
  hold the gate to drain in-flight work and leave the semaphore undisposed.

### F9 — IPv6 transition/translation addresses bypassed SSRF blocking (H2)

- **Location:** `Security/UrlGuard.cs`.
- **Issue:** NAT64 (`64:ff9b::/96`), 6to4 (`2002::/16`), Teredo (`2001::/32`) and
  IPv4-compatible (`::/96`) forms embed an IPv4 address and were allowed.
- **Fix:** reject those prefixes; added theory cases to `UrlGuardTests`.

### F10 — Egress could not be narrowed by configuration (H2)

- **Location:** `Program.cs`, `Options/LocalWebOptions.cs`.
- **Issue:** the binder appends to non-empty defaults, so `AllowedPorts:[443]`
  still allowed `80`.
- **Fix:** clear the collection defaults before binding, so a configured list
  replaces them; added option validation (`ValidateOnStart`).

### F11 — Untrusted web text was embedded in Markdown unescaped (H2)

- **Location:** `Actions/OutputFormatter.cs`.
- **Issue:** titles, snippets, engines and link destinations could inject Markdown
  structure/HTML into the model's context.
- **Fix:** escape inline metacharacters, collapse newlines, and wrap link
  destinations in angle brackets (encoding `<`/`>`); added a test.

### F12 — Assorted correctness fixes (H2/H3)

- `SearchResponse.Results`/`Suggestions` set to JSON `null` → NRE: coalesce after
  parse (`Search/SearxngClient.cs`).
- Corrupt cache JSON → tool fault: treat as a cache miss (`PageFetcher`,
  `WebSearchAction`).
- `DefaultMaxResults` was dead: `web_search` now uses it when `maxResults <= 0`.
- Legacy charsets and BOM: register `CodePagesEncodingProvider`, strip a leading
  `U+FEFF`, decode without copying (`HttpFetcher`).
- `LinkExtractor` deduped case-insensitively across the whole URL: dedupe on a
  normalized URL but case-sensitively (`Fetch/LinkExtractor.cs`).
- `NeedsBrowser` scanned scripts/noscript/comments: scan visible markup only.
- Response truncation at `MaxResponseBytes` is now surfaced as a marker/warning.
- Browser fallback failure no longer discards a usable HTTP result; plain text no
  longer triggers the browser.
- Removed the dead `AddSingleton<Web*Action>()` registrations and the false
  comment (tools are constructed per call by `WithToolsFromAssembly`).

### H4 fixed

- `CachePath` XML doc corrected (resolves against `AppContext.BaseDirectory`).
- `appsettings.json` `Logging` section is honored (`AddConfiguration`).
- `IntegrationTests` docs no longer claim a SearXNG dependency.
- `WebExtractLinksAction.ExtractLinksAsync` renamed to `WebExtractLinksAsync`.

## Round 2 (2026-10-08)

Second pass targeted the items left open after round 1.

- **O1 (H1) browser redirect** — now **mitigated**: before the browser runs,
  `HttpFetcher.ResolveRedirectsAsync` follows redirects with validated, IP-pinned
  HEAD requests and the browser is pointed at the already-validated final URL.
  Residual (documented): a redirect hop still fires server-side before the final
  URL check; full elimination needs an egress proxy.
- **O3 (H3) rendered HTML size cap** — `page.ContentAsync()` output is capped at
  `MaxResponseBytes` with a warning.
- **O4 (H3) test-only loopback flag** — configuration/env can no longer enable it;
  only the explicit `--allow-loopback` CLI flag does (`PostConfigure`).
- **O5 (H4) concurrency test** — replaced the tautological assertion with one that
  measures the server-side peak concurrency of slow requests and asserts the page
  limit; the test now fails if the semaphore is removed.
- **O6 (H4) loopback test gating** — moved to a plain `[Fact]` that runs without a
  browser or internet.
- **O7 (H4) XML documentation** — `GenerateDocumentationFile` enabled; every public
  member documented; build is warning-free.
- **O8 (H4) composition root** — a single `AddLocalWebServices()` extension is used
  by the host and both test fixtures, removing the duplicated DI graph.
- **O9 (H4) search cache key** — the key no longer includes `maxResults`; the full
  result set is cached and sliced per call.

## Open

| id | severity | location | topic |
|----|----------|----------|-------|
| O2 | H2 (residual) | `PlaywrightFetcher.cs` | The browser cannot pin the validated IP (Chromium re-resolves DNS); a DNS-rebinding answer could still reach an internal address. Documented in the README; full mitigation needs an egress proxy. |

Everything else from round 1 is fixed; see the registry in the round log.

## Notable positives (unchanged)

- HTTP SSRF design (manual re-validated redirects, IP-pinned `ConnectCallback`,
  full 127/8 + private + CGNAT + transition-address coverage, numeric-IP
  normalization) is strong and well tested.
- stdout carries only the protocol; all logs go to stderr.
- DI lifetimes and host disposal are correct; no service locator / `new` for
  services; no `async void` / sync-over-async.
- Parameterized SQL, SHA-256 cache keys, MCP annotations and schemas are correct.
