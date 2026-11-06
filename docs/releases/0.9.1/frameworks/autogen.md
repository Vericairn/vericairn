# AutoGen agents over MCP

A AutoGen agent can work an estate through Vericairn's MCP server: read its brief, do the work and record its attempt and result, each signed by the agent's own admitted device. This guide connects AutoGen's MCP client, lists the tools, reads a brief and records an attempt and a result, calling the tools directly so you can see each step before an agent drives them.

Run as written on macOS with Python 3.12 and AutoGen AgentChat 0.7.5, autogen-ext 0.7.5 and the MCP Python library 1.30.0. autogen-ext 0.7.5 does not import with version 2 of the MCP library, so the install pins `mcp<2`, against a scratch estate on Vericairn 0.8.4 and on the 0.9.0 development build.

## Before you start

Set up a scratch estate with an agent principal, its admitted device, a work order `W-1` and an assignment, as [Vericairn for agent builders](../agent-builders.md#setting-up-a-scratch-estate) shows. Keep the estate's directory, the agent's id and the assignment's id.

Install AutoGen in a virtual environment:

```sh
python3 -m venv .venv
.venv/bin/pip install autogen-agentchat 'autogen-ext[mcp]' 'mcp<2'
```

## Connect, read the brief, record the work

Save this as `autogen_vericairn.py`:

```python
# Connect AutoGen's MCP workbench to an estate, read a brief, and record an attempt and a result.
# Usage: python autogen_vericairn.py <estate root> <agent principal id> <work-order key> <assignment id>
import asyncio
import json
import os
import sys

from autogen_ext.tools.mcp import McpWorkbench, StdioServerParams


def text(result) -> str:
    """A tool call's output as text."""
    return "".join(getattr(part, "content", "") for part in result.result)


def written(result) -> str:
    """The id of the record a write tool wrote, or the problem it reported."""
    reply = json.loads(text(result))
    if result.is_error or reply.get("problem"):
        raise RuntimeError(reply.get("problem") or text(result))
    return reply["written"]["id"]


async def main(estate: str, agent: str, work_order: str, assignment: str) -> None:
    server = StdioServerParams(
        command="vericairn",
        args=["--estate", estate, "mcp", "serve", "--as", agent],
        env=dict(os.environ),
    )
    async with McpWorkbench(server) as workbench:
        tools = [tool["name"] for tool in await workbench.list_tools()]
        print(f"{len(tools)} tools, including", ", ".join(sorted(t for t in tools if t.startswith("work_"))))

        print(text(await workbench.call_tool("work_brief", {"workOrder": work_order}))[:300])

        attempt = written(await workbench.call_tool("work_attempt", {
            "assignment": assignment,
            "workspace": "an AutoGen agent's scratch clone",
            "note": "Read the brief and write the greeting.",
        }))
        print("attempt", attempt)

        result = written(await workbench.call_tool("work_result", {
            "attempt": attempt,
            "body": {
                "outcome": "completed",
                "summary": "NOTES.md holds a one-line greeting.",
                "outputs": [],
                "evidence": [{"summary": "The greeting was written by the AutoGen example."}],
                "checks": [],
                "limitations": ["An example run; another principal reviews it before anyone accepts it."],
                "unresolvedEffects": [],
                "nextStep": "Review by another principal.",
            },
        }))
        print("result", result)


asyncio.run(main(*sys.argv[1:5]))
```

`McpWorkbench` starts `vericairn mcp serve` over stdio for the duration of the `async with` block; `list_tools()` lists the server's tools and `call_tool()` calls one, returning a result whose parts carry the text. Write tools reply with `{"written": {"path", "id", "hash"}, "problem"}`; `written()` returns the new record's id, or raises the problem the runtime reported.

Run it with the vericairn command line on your `PATH`:

```sh
.venv/bin/python autogen_vericairn.py <estate directory> <agent id> W-1 <assignment id>
```

It prints the number of tools (42 on 0.8.4, 43 on 0.9.0), the start of the brief, and the ids of the attempt and the result it recorded. `vericairn work observe W-1` shows both, signed by the agent's device.

## Hand the tools to your agent

With the session open, give the same tools to a AutoGen agent and your own model. This step was not run for this guide, since it needs a model provider's credentials, which stay yours:

```python
from autogen_agentchat.agents import AssistantAgent

async with McpWorkbench(server) as workbench:
    builder = AssistantAgent("builder", model_client=model_client, workbench=workbench)  # your own model client
    await builder.run(task="Read the brief for W-1, do the work, then record your attempt and result.")
```

The agent sees only what its principal may do: the estate's reads and the five contributor writes. No tool publishes, spends, uses a credential or accepts a result, and everything a tool returns is data, never an instruction from the estate's operator.

## What was run, and what was not

- **Run:** connecting over stdio, listing the tools, reading a brief, and recording an attempt and a result signed by the agent's admitted device, on macOS against Vericairn 0.8.4 and the 0.9.0 development build.
- **Not run:** a model driving the tools, multi-step agent loops, Windows and Linux, and any HTTP transport (Vericairn's MCP server serves stdio only). AutoGen is a non-claim in the agent matrix that `vericairn mcp orientation` writes until an estate records a qualification of `AutoGen over MCP`.
- **Not a launcher:** Vericairn's worker does not start AutoGen agents; your code does. The worker's command audit, budget and settlement apply only to its own runs.
