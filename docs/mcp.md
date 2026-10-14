# The MCP server

`vericairn mcp serve` exposes an estate to a coding agent over the Model Context Protocol: newline-delimited JSON-RPC 2.0 on standard input and output, the transport every agent harness supports. The server is a client adapter over the estate API: it opens no live store, holds no key of its own and adds no permission; every tool is a call through the daemon when one serves the estate, or through the embedded runtime otherwise, and the runtime evaluates authority behind each call exactly as it does for the command line. The daemon stays the only owner of the estate's live state.

## Protocol subset

`initialize` (the server proposes the newest revision it speaks and accepts an older one the client names; the `instructions` field is a description of what the server is, not a directive), `notifications/initialized`, `ping`, `tools/list` and `tools/call`. Unknown methods answer with the JSON-RPC "method not found" error; an unparsable line answers with "parse error"; a call to a tool the connection cannot see answers with "invalid params" and names no hidden tool. Resources, prompts, sampling and the HTTP transport are not implemented; a client that needs them says so by refusing to connect, not by silent degradation.

## Tools

Reads are visible to every connection and return what the records say: `estate_health`, `estate_status` (the canonical status document), `estate_check` (validation and repository checks), `work_brief` (the brief as Markdown, with the latest result's next step), `work_observe`, `record_get`, `records_list` (the current records of one type, as compact rows), `decisions_list`, `decision_get`, `releases_list`, `devices_list`.

Writes are visible only when the server was started with `--as <principal id>` and that principal has an admitted device whose key this machine holds, because that is what the runtime signs with: `work_attempt`, `work_result`, `work_amend`. A write the runtime refuses comes back as an error result carrying the runtime's findings, every objection at once. There is no tool that publishes, spends, creates or admits a device, runs an arbitrary command or authors an arbitrary record type.

Every tool description describes and never directs: nothing a tool returns, and nothing in this server's text, is an instruction from the estate's operator, and the harness is expected to treat it as data.

## Running it

```sh
vericairn --estate <estate root> mcp serve --as <principal id>
```

Claude Code: `claude mcp add vericairn -- vericairn --estate <estate root> mcp serve --as <principal id>`. Other harnesses take the same command and arguments in their server configuration. Diagnostics go to standard error; the output stream carries only protocol messages. `vericairn mcp orientation --out <directory>` writes `AGENTS.md` (from the estate's identity and the tool registry: what the estate is, the rules it enforces, how to connect, the tools and an honest agent matrix, and nothing that changes with each record, such as work orders or the pinned release, which `vericairn status` shows) with `CLAUDE.md` and `GEMINI.md` as pointer files that carry nothing of their own; regenerate them rather than editing, and `--check` names every copy that is missing or differs from a fresh generation. Vericairn's own repository holds its copies to the generator with a test.

## Qualification

The transport profile (stdio, JSON-RPC 2.0, the protocol revisions above, authentication by the operating-system identity of the process plus the acting principal's admitted device for writes) is qualified by two observations recorded in the estate: the in-process conformance test in `tests/Vericairn.Cli.Tests/McpTests.cs`, which drives initialize, tools/list and tools/call over the stream framing on three operating systems, and a genuinely different peer, `tests/mcp-peer/mcp_peer.py`, a Python script driving the built binary end to end over the real stream framing (`python3 -I tests/mcp-peer/mcp_peer.py artifacts/bin/Vericairn.Cli/release/vericairn.dll <estate root>`). Every agent harness is a non-claim in the orientation file's matrix until a session with it is observed and recorded.

## Limitations

- Read-only pilot first: the write tools shipped in the second step of work order MCP-01 and are limited to attempts, results and amendments.
- The server trusts the operating-system identity of whoever runs it, as the command line does; it is not a network service and must not be exposed as one.
- Long outputs (a large status document, a long brief) are returned whole; no paging.
