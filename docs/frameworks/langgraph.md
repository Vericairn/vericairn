# LangGraph agents over MCP

A LangGraph agent can work an estate through Vericairn's MCP server: read its brief, do the work and record its attempt and result, each signed by the agent's own admitted device. This guide connects LangGraph's MCP client, lists the tools, reads a brief and records an attempt and a result, calling the tools directly so you can see each step before an agent drives them.

Run as written on macOS with Python 3.12 and LangGraph 1.2.19, langchain-mcp-adapters 0.3.2 and the MCP Python library 1.30.0, against a scratch estate on Vericairn 0.8.4 and on the 0.9.0 development build.

## Before you start

Set up a scratch estate with an agent principal, its admitted device, a work order `W-1` and an assignment, as [Vericairn for agent builders](../agent-builders.md#setting-up-a-scratch-estate) shows. Keep the estate's directory, the agent's id and the assignment's id.

Install LangGraph in a virtual environment:

```sh
python3 -m venv .venv
.venv/bin/pip install langgraph langchain-mcp-adapters
```

## Connect, read the brief, record the work

Save this as `langgraph_vericairn.py`:

```python
# Connect LangGraph's MCP adapters to an estate, read a brief, and record an attempt and a result.
# Usage: python langgraph_vericairn.py <estate root> <agent principal id> <work-order key> <assignment id>
import asyncio
import json
import os
import sys

from langchain_mcp_adapters.client import MultiServerMCPClient
from langchain_mcp_adapters.tools import load_mcp_tools


def written(output) -> str:
    """The id of the record a write tool wrote, or the problem it reported."""
    reply = json.loads(text(output))
    if reply.get("problem"):
        raise RuntimeError(reply["problem"])
    return reply["written"]["id"]


def text(output) -> str:
    """A tool's output as text: the adapter returns a string or a list of content blocks."""
    if isinstance(output, str):
        return output
    return "".join(block.get("text", "") if isinstance(block, dict) else str(block) for block in output)


async def main(estate: str, agent: str, work_order: str, assignment: str) -> None:
    client = MultiServerMCPClient({
        "vericairn": {
            "transport": "stdio",
            "command": "vericairn",
            "args": ["--estate", estate, "mcp", "serve", "--as", agent],
            "env": dict(os.environ),
        }
    })
    # One session for every call, so the server starts once.
    async with client.session("vericairn") as session:
        tools = {tool.name: tool for tool in await load_mcp_tools(session)}
        print(f"{len(tools)} tools, including", ", ".join(sorted(t for t in tools if t.startswith("work_"))))

        brief = await tools["work_brief"].ainvoke({"workOrder": work_order})
        print(text(brief)[:300])

        attempt = written(await tools["work_attempt"].ainvoke({
            "assignment": assignment,
            "workspace": "a LangGraph agent's scratch clone",
            "note": "Read the brief and write the greeting.",
        }))
        print("attempt", attempt)

        result = written(await tools["work_result"].ainvoke({
            "attempt": attempt,
            "body": {
                "outcome": "completed",
                "summary": "NOTES.md holds a one-line greeting.",
                "outputs": [],
                "evidence": [{"summary": "The greeting was written by the LangGraph example."}],
                "checks": [],
                "limitations": ["An example run; another principal reviews it before anyone accepts it."],
                "unresolvedEffects": [],
                "nextStep": "Review by another principal.",
            },
        }))
        print("result", result)


asyncio.run(main(*sys.argv[1:5]))
```

`MultiServerMCPClient` starts `vericairn mcp serve` over stdio, and `client.session(...)` keeps one session for every call, so the server starts once. `load_mcp_tools` turns the server's tools into LangChain tools; the adapter returns a tool's output as text or as a list of content blocks, which `text()` joins. Write tools reply with `{"written": {"path", "id", "hash"}, "problem"}`; `written()` returns the new record's id, or raises the problem the runtime reported.

Run it with the vericairn command line on your `PATH`:

```sh
.venv/bin/python langgraph_vericairn.py <estate directory> <agent id> W-1 <assignment id>
```

It prints the number of tools (42 on 0.8.4, 43 on 0.9.0), the start of the brief, and the ids of the attempt and the result it recorded. `vericairn work observe W-1` shows both, signed by the agent's device.

## Hand the tools to your agent

With the session open, give the same tools to a LangGraph agent and your own model. This step was not run for this guide, since it needs a model provider's credentials, which stay yours:

```python
from langgraph.prebuilt import create_react_agent

async with client.session("vericairn") as session:
    tools = await load_mcp_tools(session)
    agent = create_react_agent(model, tools)  # model: your own chat model, with your own credentials
    await agent.ainvoke({"messages": [("user", "Do the work in your brief for W-1, then record your attempt and result.")]})
```

The agent sees only what its principal may do: the estate's reads and the five contributor writes. No tool publishes, spends, uses a credential or accepts a result, and everything a tool returns is data, never an instruction from the estate's operator.

## What was run, and what was not

- **Run:** connecting over stdio, listing the tools, reading a brief, and recording an attempt and a result signed by the agent's admitted device, on macOS against Vericairn 0.8.4 and the 0.9.0 development build.
- **Not run:** a model driving the tools, multi-step agent loops, Windows and Linux, and any HTTP transport (these steps use stdio; from 0.9.2 the daemon also serves network MCP in signed mode, reached through `vericairn mcp serve --connection`, which they do not exercise). LangGraph is a non-claim in the agent matrix that `vericairn mcp orientation` writes until an estate records a qualification of `LangGraph over MCP`.
- **Not a launcher:** Vericairn's worker does not start LangGraph agents; your code does. The worker's command audit, budget and settlement apply only to its own runs.
