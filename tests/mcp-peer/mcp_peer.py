# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr
"""A genuinely different peer: drives the built vericairn MCP server over stdio with JSON-RPC 2.0 and checks the protocol
subset end to end. Read-only: it never calls a write tool. Usage: mcp_peer.py <vericairn binary or vericairn.dll> <estate root> [--as id]
Any estate will do: the brief check reads work order MCP-01 where the estate holds it, as Vericairn's does, and otherwise the
first work order the estate's status lists; in an estate without work orders it is skipped, and the output says which."""
import json, subprocess, sys, time

def main():
    binary, estate = sys.argv[1], sys.argv[2]
    extra = sys.argv[3:]
    command = ['dotnet', binary] if binary.endswith('.dll') else [binary]
    proc = subprocess.Popen([*command, '--estate', estate, 'mcp', 'serve', *extra], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8')
    n = 0
    def call(method, params=None, notify=False):
        nonlocal n
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None: msg["params"] = params
        if not notify:
            n += 1; msg["id"] = n
        proc.stdin.write(json.dumps(msg) + "\n"); proc.stdin.flush()
        if notify: return None
        line = proc.stdout.readline()
        if not line: raise SystemExit("server closed the stream before answering " + method)
        reply = json.loads(line)
        assert reply.get("jsonrpc") == "2.0" and reply.get("id") == n, reply
        return reply
    checks, skipped = [], []
    def check(name, ok, detail=""):
        checks.append((name, ok, detail)); print(("PASS " if ok else "FAIL ") + name + (": " + detail if detail else ""))
    def skip(name, why):
        skipped.append(name); print("SKIP " + name + ": " + why)
    t0 = time.time()
    init = call("initialize", {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "mcp_peer.py", "version": "1"}})
    check("initialize negotiates 2025-06-18", init["result"]["protocolVersion"] == "2025-06-18", init["result"]["serverInfo"]["version"])
    check("instructions are descriptive", "must" not in init["result"]["instructions"].lower())
    call("notifications/initialized", notify=True)
    check("ping answers", call("ping")["result"] == {})
    tools = call("tools/list")["result"]["tools"]
    names = [t["name"] for t in tools]
    check("read tools listed", all(x in names for x in ["estate_status", "work_brief", "record_get", "decisions_list", "releases_list"]), ", ".join(names))
    check("no write tool without --as", ("--as" in extra) or not any(x in names for x in ["work_attempt", "work_result", "work_amend"]))
    check("every tool has an object schema", all(t["inputSchema"]["type"] == "object" for t in tools))
    status = call("tools/call", {"name": "estate_status", "arguments": {}})["result"]
    doc = json.loads(status["content"][0]["text"])
    check("estate_status returns the canonical document", (not status.get("isError")) and "workOrders" in doc and "records" in doc, f"{doc.get('records')} records")
    keys = [w["key"] for w in doc.get("workOrders", []) if isinstance(w, dict) and isinstance(w.get("key"), str)]
    if not keys:
        skip("work_brief returns markdown", "the estate has no work order, so there is no brief to read")
    else:
        key = "MCP-01" if "MCP-01" in keys else keys[0]
        brief = call("tools/call", {"name": "work_brief", "arguments": {"workOrder": key}})["result"]
        check(f"work_brief {key} returns markdown", (not brief.get("isError")) and key in brief["content"][0]["text"], "" if key == "MCP-01" else "the estate has no work order MCP-01, so the first one its status lists stands in")
    missing = call("tools/call", {"name": "work_brief", "arguments": {"workOrder": "NO-SUCH"}})["result"]
    check("unknown work order is an error result, not a crash", missing.get("isError") is True)
    unknown = call("tools/call", {"name": "work_attempt", "arguments": {}})
    check("hidden tool is unknown (invalid params)", "error" in unknown and unknown["error"]["code"] == -32602)
    nf = call("no/such")
    check("unknown method is -32601", nf.get("error", {}).get("code") == -32601)
    rel = call("tools/call", {"name": "releases_list", "arguments": {}})["result"]
    check("releases_list returns rows", (not rel.get("isError")) and isinstance(json.loads(rel["content"][0]["text"]), list))
    proc.stdin.close(); proc.wait(timeout=30)
    check("server exits when the input ends", proc.returncode == 0, f"exit {proc.returncode}")
    err = proc.stderr.read()
    check("diagnostics go to stderr only", "vericairn mcp: serving" in err)
    failed = [c for c in checks if not c[1]]
    print(f"{len(checks) - len(failed)} of {len(checks)} checks passed" + (f", {len(skipped)} skipped" if skipped else "") + f" in {time.time() - t0:.1f}s")
    sys.exit(1 if failed else 0)

main()
