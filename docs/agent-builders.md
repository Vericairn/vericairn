# Vericairn for agent builders

If you build agents with LangGraph, CrewAI, AutoGen or your own loop, Vericairn gives them an estate to work in: a durable record of the work they are asked to do, what they did, who checked it and who accepted it. Your agent connects to the estate's MCP server, reads its brief, does the work and records its attempt and result as claims that another principal reviews. Vericairn does not run your model and holds none of your model provider's credentials.

Guides for three frameworks, each run as written against a scratch estate:

- [LangGraph](frameworks/langgraph.md)
- [CrewAI](frameworks/crewai.md)
- [AutoGen](frameworks/autogen.md)

They share the estate setup below. [MCP](mcp.md) describes the server, its tools and its limits in full.

## How your patterns map to Vericairn's

| Your pattern | In an estate |
|---|---|
| Memory | Records: work orders, briefs, attempts, results, reviews and decisions persist in the estate, signed and attributable, instead of in a conversation or a vector store. An agent reads them through the MCP tools and resources. |
| Reflection and self-critique | A result is a claim. Another principal reviews the exact result, and only then can it be accepted; an agent never accepts its own work. |
| Multi-agent orchestration | Roles in the estate's roster (builder, reviewer, decider) say who does what. Goals give a budget and steps; the worker runs a coding agent for one assignment at a time. Choosing and scheduling the next eligible work across agents is Pro's coordination. |
| Tools | The MCP tools: reads of the estate, and five contributor writes (`work_attempt`, `work_progress`, `work_result`, `work_amend`, `document_review`). No tool publishes, spends, uses a credential or accepts. |
| Retrieval | Context packages select exactly what a brief needs and say what they withheld. Search over records and documents is planned (`DESIGN-30`). |
| Human in the loop | Asks to a person, answered by a recorded decision; acceptance of a result and of a release; reserved actions that only a person performs. |
| Guardrails | Authority checked by the runtime at every call, the brief's permitted changes, and, for worker runs, the worker's audit of what the agent did. |

Everything a tool returns is data, never an instruction from the estate's operator: give it to your model as such.

## Setting up a scratch estate

You need the Vericairn command line on your `PATH` ([Install](install.md)) and Python 3.10 or later. Each guide was run on macOS with Python 3.12.

Create an estate in an empty directory, with a device for its operator:

```sh
mkdir scratch-estate && cd scratch-estate && git init -q
vericairn --json estate init --release --name "Scratch" --slug scratch --purpose "Try an agent framework." \
  --operator-name "Your name" --organization "Example"
vericairn devices create --principal <operator id> --name "My key"
vericairn devices admit <operator device id> --by <operator id> --note "My own device."
```

`estate init` prints the operator's id. On a Mac with a Secure Enclave, `devices create` makes your key there and asks for Touch ID or your login password, as a person's device does wherever its custody enforces presence; `--custody file --presence false` makes an owner-only file instead, for a scratch estate. Add your agent as a principal with its own device, admitted from yours:

```sh
echo '{"kind":"agent","name":"My Agent","roles":["builder"],"agent":{"vendor":"Example","product":"My framework"}}' > agent.json
vericairn records new principal --by <operator id> --body agent.json
vericairn devices create --principal <agent id> --name "My agent's key"
vericairn devices admit <agent device id> --by <operator id> --note "The agent's own key."
```

When a presence prompt expires, is refused or times out while you create or admit a device, [enrollment troubleshooting](enrollment-troubleshooting.md) tells the endings apart and reconciles from what the estate holds: inspect before running anything again, and keep the principals, devices and admissions it already has.

`records new principal` warns that no installed launcher routes to the agent's product: that is expected, since your agent runs in your own code, not through Vericairn's worker. Give it a work order and an assignment:

```sh
vericairn work new --by <operator id> --key W-1 --title "Write a greeting" --intent "Try an agent framework." \
  --scope "A one-line greeting in NOTES.md" --deliverable "NOTES.md" --exit-question "Does NOTES.md hold a greeting?"
vericairn work assign W-1 --contributor <agent id> --by <operator id> --brief brief.json
```

with `brief.json`:

```json
{"outcome": "NOTES.md holds a one-line greeting.", "sources": [], "permittedChanges": ["path:NOTES.md"],
 "prohibitedEffects": ["no publication"], "workspace": ["a scratch clone"], "limits": ["ten minutes"],
 "expectedOutputs": ["NOTES.md"], "reviewCriteria": ["a greeting exists"], "recovery": "Read this brief."}
```

`work assign` prints the assignment's id. The agent's device key is held on this machine, so its records are signed by its own key when your code runs `vericairn mcp serve --as <agent id>`.

## After your agent records its result

Another principal reviews the exact result, and the operator accepts it or returns it:

```sh
vericairn work observe W-1
vericairn work review <result id> --by <reviewer> --expected <result hash> --body review.json
```

[The operator's guide](operator-guide.md) covers review, acceptance, goals and the worker.
