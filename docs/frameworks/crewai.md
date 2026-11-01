# CrewAI agents over MCP

A CrewAI agent can work an estate through Vericairn's MCP server: read its brief, do the work and record its attempt and result, each signed by the agent's own admitted device. This guide connects CrewAI's MCP client, lists the tools, reads a brief and records an attempt and a result, calling the tools directly so you can see each step before an agent drives them.

Run as written on macOS with Python 3.12 and CrewAI 1.15.33, crewai-tools 1.15.33 and the MCP Python library 1.30.0, against a scratch estate on Vericairn 0.8.4 and on the 0.9.0 development build.

## Before you start

Set up a scratch estate with an agent principal, its admitted device, a work order `W-1` and an assignment, as [Vericairn for agent builders](../agent-builders.md#setting-up-a-scratch-estate) shows. Keep the estate's directory, the agent's id and the assignment's id.

Install CrewAI in a virtual environment:

```sh
python3 -m venv .venv
.venv/bin/pip install crewai 'crewai-tools[mcp]'
```

## Connect, read the brief, record the work

Save this as `crewai_vericairn.py`:

```python
# Connect CrewAI's MCP adapter to an estate, read a brief, and record an attempt and a result.
# Usage: python crewai_vericairn.py <estate root> <agent principal id> <work-order key> <assignment id>
import json
import os
import sys

from crewai_tools import MCPServerAdapter
from mcp import StdioServerParameters


def written(output) -> str:
    """The id of the record a write tool wrote, or the problem it reported."""
    reply = json.loads(output)
    if reply.get("problem"):
        raise RuntimeError(reply["problem"])
    return reply["written"]["id"]


def main(estate: str, agent: str, work_order: str, assignment: str) -> None:
    server = StdioServerParameters(
        command="vericairn",
        args=["--estate", estate, "mcp", "serve", "--as", agent],
        env=dict(os.environ),
    )
    with MCPServerAdapter(server) as adapter:
        tools = {tool.name: tool for tool in adapter}
        print(f"{len(tools)} tools, including", ", ".join(sorted(t for t in tools if t.startswith("work_"))))

        print(str(tools["work_brief"].run(workOrder=work_order))[:300])

        attempt = written(tools["work_attempt"].run(
            assignment=assignment,
            workspace="a CrewAI agent's scratch clone",
            note="Read the brief and write the greeting.",
        ))
        print("attempt", attempt)

        result = written(tools["work_result"].run(
            attempt=attempt,
            body={
                "outcome": "completed",
                "summary": "NOTES.md holds a one-line greeting.",
                "outputs": [],
                "evidence": [{"summary": "The greeting was written by the CrewAI example."}],
                "checks": [],
                "limitations": ["An example run; another principal reviews it before anyone accepts it."],
                "unresolvedEffects": [],
                "nextStep": "Review by another principal.",
            },
        ))
        print("result", result)


main(*sys.argv[1:5])
```

`MCPServerAdapter` starts `vericairn mcp serve` over stdio for the duration of the `with` block and gives CrewAI tools; `run()` calls one with keyword arguments and returns its text. Write tools reply with `{"written": {"path", "id", "hash"}, "problem"}`; `written()` returns the new record's id, or raises the problem the runtime reported.

Run it with the vericairn command line on your `PATH`:

```sh
.venv/bin/python crewai_vericairn.py <estate directory> <agent id> W-1 <assignment id>
```

It prints the number of tools (42 on 0.8.4, 43 on 0.9.0), the start of the brief, and the ids of the attempt and the result it recorded. `vericairn work observe W-1` shows both, signed by the agent's device.

## Hand the tools to your agent

With the session open, give the same tools to a CrewAI agent and your own model. This step was not run for this guide, since it needs a model provider's credentials, which stay yours:

```python
from crewai import Agent, Crew, Task

with MCPServerAdapter(server) as tools:
    builder = Agent(role="Builder", goal="Do the work in your brief", backstory="An agent in a Vericairn estate.",
                    tools=tools, llm=llm)  # llm: your own model, with your own credentials
    task = Task(description="Read the brief for W-1, do the work, then record your attempt and result.",
                expected_output="The result's record id.", agent=builder)
    Crew(agents=[builder], tasks=[task]).kickoff()
```

The agent sees only what its principal may do: the estate's reads and the five contributor writes. No tool publishes, spends, uses a credential or accepts a result, and everything a tool returns is data, never an instruction from the estate's operator.

## What was run, and what was not

- **Run:** connecting over stdio, listing the tools, reading a brief, and recording an attempt and a result signed by the agent's admitted device, on macOS against Vericairn 0.8.4 and the 0.9.0 development build.
- **Not run:** a model driving the tools, multi-step agent loops, Windows and Linux, and any HTTP transport (Vericairn's MCP server serves stdio only). CrewAI is a non-claim in the agent matrix that `vericairn mcp orientation` writes until an estate records a qualification of `CrewAI over MCP`.
- **Not a launcher:** Vericairn's worker does not start CrewAI agents; your code does. The worker's command audit, budget and settlement apply only to its own runs.
