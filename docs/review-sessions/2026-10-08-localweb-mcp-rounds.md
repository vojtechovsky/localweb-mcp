# Code review rounds — localweb-mcp (2026-10-08)

> Adapted run of the `pipeline-code-review` methodology (the source skill is
> Schedara-scoped) applied to the LocalWeb.Mcp solution.

- **Protocol:** pipeline-code-review (adapted for localweb-mcp).
- **Scope:** whole solution `LocalWeb.Mcp.slnx` — initial review.
- **Baseline commit:** `c139baca3890be807b9e9d53c4006cf6853f6de5`.
- **Build baseline:** `dotnet build LocalWeb.Mcp.slnx` → pass (0 warnings, 0 errors).
- **Tests baseline:** `dotnet test` → 63 passed, 8 skipped.
- **Build / tests at close:** build pass (0 warnings, XML docs enforced);
  `dotnet test` → 77 passed / 7 skipped;
  `LOCALWEB_INTEGRATION=1 dotnet test` → 84 passed.
- **Findings document:** `docs/findings/2026-10-08-localweb-mcp.md`.

## Finding registry

| id | location | topic | severity | round | status |
|----|----------|-------|----------|-------|--------|
| F1 | `Fetch/PlaywrightFetcher.cs` | browser SSRF (redirect/SW/WS/popups) | H1 | R1 | fixed (residual O1) |
| F2 | `Fetch/PlaywrightFetcher.cs` | browser DNS rebinding (no IP pin) | H1 | R1 | wontfix (documented, O2) |
| F3 | `PlaywrightFetcher`/`PageFetcher`/`Actions` | infra exceptions escape, not cached | H2 | R1 | fixed |
| F4 | `Actions/*`, `OutputFormatter.cs` | failures returned as success (isError) | H2 | R1 | fixed |
| F5 | `Fetch/HttpFetcher.cs`, `Search/SearxngClient.cs` | unbounded body read / no size cap | H2 | R1 | fixed |
| F6 | `Fetch/HttpFetcher.cs` | proxy defeats DNS pinning | H2 | R1 | fixed |
| F7 | `Fetch/PlaywrightFetcher.cs` | cancellation + page-slot leak + dispose race | H2 | R1 | fixed |
| F8 | `Cache/SqliteCache.cs` | dispose gate dance / awaiters stranded | H2 | R1 | fixed |
| F9 | `Security/UrlGuard.cs` | IPv6 transition addresses allowed | H2 | R1 | fixed |
| F10 | `Program.cs` | egress lists cannot be narrowed | H2 | R1 | fixed |
| F11 | `Actions/OutputFormatter.cs` | untrusted Markdown/HTML injection | H2 | R1 | fixed |
| F12 | `Search/SearxngClient.cs` | null collections → NRE | H2 | R1 | fixed |
| F13 | `PageFetcher`, `WebSearchAction` | corrupt cache JSON faults tool | H3 | R1 | fixed |
| F14 | `Actions/WebSearchAction.cs` | `DefaultMaxResults` dead | H3 | R1 | fixed |
| F15 | `Fetch/HttpFetcher.cs` | legacy charset / BOM | H3 | R1 | fixed |
| F16 | `Fetch/LinkExtractor.cs` | case-insensitive whole-URL dedupe | H3 | R1 | fixed |
| F17 | `Fetch/ContentExtractor.cs` | `NeedsBrowser` scans scripts/noscript | H3 | R1 | fixed |
| F18 | `Fetch/FetchModels.cs` | `Truncated` never surfaced | H3 | R1 | fixed |
| F19 | `Search/SearxngClient.cs` | SearXNG body not size-capped | H3 | R1 | fixed |
| F20 | `Security/UrlGuard.cs` | error leaks resolved internal IP | H3 | R1 | fixed |
| F21 | `Program.cs` | no options validation | H3 | R1 | fixed |
| F22 | `Program.cs` | dead action singletons + wrong comment | H3 | R1 | fixed |
| F23 | `Options/LocalWebOptions.cs` | `CachePath` doc wrong | H4 | R1 | fixed |
| F24 | `appsettings.json`/`Program.cs` | `Logging` section dead | H4 | R1 | fixed |
| F25 | `Tests/IntegrationTests.cs` | docs claim SearXNG dependency | H4 | R1 | fixed |
| F26 | `Actions/WebExtractLinksAction.cs` | method naming inconsistency | H4 | R1 | fixed |
| O1 | `Fetch/PlaywrightFetcher.cs` | browser redirect hop fires first | H1 | R2 | mitigated |
| O2 | `Fetch/PlaywrightFetcher.cs` | browser cannot pin validated IP | H2 | R2 | wontfix (documented) |
| O3 | `Fetch/PlaywrightFetcher.cs` | rendered HTML has no size cap | H3 | R2 | fixed |
| O4 | `Options/LocalWebOptions.cs` | `AllowLoopbackForTests` production-bindable | H3 | R2 | fixed |
| O5 | `Tests/IntegrationTests.cs` | concurrency test tautological | H4 | R2 | fixed |
| O6 | `Tests/IntegrationTests.cs` | loopback test gated needlessly | H4 | R2 | fixed |
| O7 | solution-wide | XML docs incomplete on public members | H4 | R2 | fixed |
| O8 | `Program.cs`, tests | no interfaces / composition root | H4 | R2 | fixed |
| O9 | `Cache`, `WebSearchAction` | search key includes `maxResults`; `RemoveAsync` dead | H4 | R2 | fixed |

## Round log

### Round 1 — 2026-10-08

- Roles: R1 Correctness & logic; R2 Security & SSRF; R4 Async, errors & resilience;
  R5 Contracts, tests & MCP usage; R8 Architecture, clean code & annotations.
- Confirmed: 26 fixed (H1 1, H2 12, H3 9, H4 4); plus 2 H1/H2 residuals documented.
- Discarded: 0 invalid; several H4 style items folded into systemic fixes or left open.
- Fixes applied: see the registry (all `fixed` rows) and `docs/findings/2026-10-08-localweb-mcp.md`.
  Every fix gated by `dotnet build LocalWeb.Mcp.slnx` and `dotnet test`; the browser
  changes additionally verified with `LOCALWEB_INTEGRATION=1 dotnet test`.
- New topics: 35 (26 fixed, 9 open).
- Regressions: 0.

### Round 2 — 2026-10-08

- Roles: same five (R1, R2, R4, R5, R8); targeted at the 9 carry-over items.
- Confirmed: 8 fixed (O1 mitigated, O3–O9 fixed); 1 documented residual (O2).
- Fixes applied: `ResolveRedirectsAsync` + browser pre-resolution (O1); rendered
  HTML cap (O3); `--allow-loopback` only (O4); real concurrency assertion (O5);
  ungated loopback test (O6); `GenerateDocumentationFile` + full XML docs (O7);
  `AddLocalWebServices` composition root (O8); search cache key without
  `maxResults` (O9). Verified by `dotnet build LocalWeb.Mcp.slnx` and
  `dotnet test` (77 pass / 7 skip; `LOCALWEB_INTEGRATION=1` → 84 pass).
- New topics: 0 (all were carry-over).
- Regressions: 0.
- Stop gate: **no new H1/H2/H3 topics** and every remaining item is either fixed or
  a documented residual (O2). The loop is complete for this scope.

## Confirmed domain facts (challengeable — feed `{KNOWN_FACTS}` next round)

- HTTP path pins the validated IP via `ConnectCallback` and disables proxies; the
  browser path cannot pin and is a check, not a hard guarantee.
- Tool failures are surfaced as `McpException` (isError = true).
- `AllowedSchemes`/`AllowedPorts` are replaced by configured values (defaults are
  cleared before binding); options are validated on start.
- Rendered browser HTML is not size-capped (O3); `MaxResponseBytes` applies to the
  HTTP and SearXNG paths.

## Fixed topics (do not re-report — feed `{FIXED_TOPICS}`)

- F1–F26 as listed in the registry.

## Accepted decisions (out of scope — feed `{ACCEPTED_DECISIONS}`)

- Exa remains a separate hosted MCP server in the client; this project is the deliberate local fallback.
- SearXNG is intentionally not started in this environment (files prepared only).
- Deployment is a framework-dependent `dotnet publish` to `~/tools/localweb-mcp`.
- Chromium runs with `--no-sandbox` (required in most containers); the process runs as a non-privileged user.

## Cross-cutting register (open items to address later)

| module / area | finding | evidence | severity |
|---------------|---------|----------|----------|
| `Fetch/PlaywrightFetcher.cs` | redirect hop fires before final-URL check | `RenderAsync` | H1 |
| `Fetch/PlaywrightFetcher.cs` | no IP pin for the browser | `HandleRouteAsync` | H2 |
| `Fetch/PlaywrightFetcher.cs` | rendered HTML size cap | `ContentAsync` | H3 |
| `Options/LocalWebOptions.cs` | test-only loopback flag | `AllowLoopbackForTests` | H3 |
| `Tests/IntegrationTests.cs` | concurrency / gating nits | tests | H4 |
| solution-wide | XML docs; interfaces/composition root | — | H4 |

## Next round (if run)

Re-launch the five roles with `{FIXED_TOPICS}` = F1–F26 and the open register as
`{ATTENTION_AREAS}`, and target the browser-residual cluster (O1–O3) plus the
testability refactor (O8).
