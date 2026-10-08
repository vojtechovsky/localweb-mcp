# AGENTS.md

Guidance for AI coding agents working in this repository.

## Web access policy

- **Prefer Exa** for web search and page fetching when it is available.
- If Exa fails because of a rate limit, quota, or an outage, fall back to the
  `localweb` MCP server and use, in order:
  1. `web_search` — find candidate URLs.
  2. `web_fetch` — retrieve a page (HTTP first, browser fallback).
  3. `web_render` — force a headless-browser render for JavaScript-heavy pages.
  4. `web_extract_links` — list the links on a page.
- Do not invent or guess content. If both Exa and `localweb` fail, report the
  error instead of fabricating an answer.

## Repository conventions

- Target framework: `net10.0`.
- `stdout` is reserved for the MCP protocol. All logs must go to `stderr`.
- Never commit secrets. Configuration comes from `appsettings.json`, environment
  variables, and command-line options only.
- Keep the tool surface minimal: `web_search`, `web_fetch`, `web_render`,
  `web_extract_links`.
- Do not weaken `UrlGuard` (SSRF protection). `AllowLoopbackForTests` must stay
  `false` outside tests.
