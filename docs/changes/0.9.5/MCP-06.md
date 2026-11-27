# MCP-06: Stateless MCP beside the session-based revision

## docs/operator-guide.md

### In "14. Agents through the MCP server", after the opening paragraph

> From 0.9.5 (MCP-06), stdio and Streamable HTTP serve MCP `2026-07-28` beside `2025-11-25`. The newer revision identifies itself on each request, can probe with `server/discover`, and follows resource changes with `subscriptions/listen`. A signed HTTP listener has a presence lease for the lifetime of its stream. Authorization remains with the runtime for every operation. The signed bridge forwards either revision, including the existing two-step signing of contributor records.

## docs/api-compatibility-0.9.5.md

### In the MCP compatibility notes

> MCP `2026-07-28` results carry `resultType`. Discovery, lists and resource reads carry private, zero-TTL cache metadata. Modern HTTP POSTs carry version, method and any required name header. They must explicitly accept both JSON and SSE or receive `406`. They create no MCP session. GET and DELETE naming `2026-07-28`, or carrying neither a version nor a session ID, receive `405` with `Allow: POST`. The earlier revision retains its initialization, session and result shapes.
