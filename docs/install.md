# Installing Vericairn

This installation draft describes 0.7.2 after published 0.7.1, with FIX-13, FIX-14, FIX-15, FIX-16 and
FIX-18. Earlier sets stay frozen, including the withdrawn 0.7.0 candidate carried in 0.7.1 with FIX-12.
NuGet distribution becomes available after the operator publishes a release and its publication workflow succeeds.
The `0.0.1-reserved` packages reserve names; they are not the product. For the currently published build, use its frozen guide in the [release index](releases/README.md).
The NuGet workflow verifies the exact package assets the operator publishes against that release's own
`SHA256SUMS`; it does not compare them with the estate's accepted candidate. Publication of the accepted
draft is the operator's responsibility. No package is rebuilt during upload, and duplicate versions are skipped.

## Command line from NuGet

Install the .NET 10 SDK on Linux, macOS or Windows, then:

```sh
dotnet tool install --global dotnet-vc
dotnet vc version
dotnet vc doctor
```

Put the global tool directory on PATH if the installer asks. `dotnet-vc` is the tool's package and command
name; `dotnet vc` is its canonical invocation. It uses the .NET and ASP.NET Core shared runtimes included
with the SDK. `doctor` reports the runtime, git, SDK and available device-key custody. Git is needed for
repository operations.

To pin the tool in a repository instead, run these commands at its root (create the manifest only once):

```sh
dotnet new tool-manifest
dotnet tool install dotnet-vc --version <published-version>
dotnet vc version
dotnet vc doctor
```

Commit the generated tool manifest. A new checkout runs `dotnet tool restore`, then uses the same
`dotnet vc ...` commands from that repository. Use `--version <version>` when installing a particular
published release, and `dotnet tool update --global dotnet-vc` to update a global installation.

For example, inspect the record types or read an existing estate:

```sh
dotnet vc records describe
dotnet vc --estate <estate root> status
```

Installation grants no estate authority; writes still require the acting principal's admitted device.
The worker's agent-side MCP launcher currently resolves the archive command `vericairn`; keep that
executable on PATH for worker runs. A NuGet-only worker run is not covered by the package installation checks.

## SDK

In a .NET 10 console or library project:

```sh
dotnet add package Vericairn
```

This dependency-only front door brings `Vericairn.Sdk` and its `Vericairn.Protocol` and `Vericairn.Records`
dependencies. The existing `Vericairn.Sdk` namespace and assembly stay the SDK's identity. It does not bring
the embedded runtime or the daemon; hosts can add `Vericairn.Runtime` or `Vericairn.Daemon` separately, and
MCP adapters can use `Vericairn.Mcp`. All are Community packages under Apache-2.0. See the
[API and SDK guide](api-v1.md#the-sdk) for connecting to an estate.

## Self-contained archives and Workbench

The release archives still contain `bin/vericairn` (`bin/vericairn.exe` on Windows), self-contained for each
platform. Follow the published release's installation guide to verify the archive against the estate's
recorded hashes before running it. Archive commands written as `vericairn ...` use the same arguments as
`dotnet vc ...`.

The macOS archives include `vericairn-enclave` beside the executable. The portable NuGet tool does not
include that native helper; use the macOS archive when you need the bundled helper for Secure Enclave custody.
Workbench continues to use its own archive, with the `vericairn` executable beside it; the tool package installs the command
line only. See the [Workbench draft](workbench.md).

On POSIX, the bounded Git and gh runner requires the runtime to run under an init process, not as PID 1,
without ignored `SIGCHLD`, automatic child reaping or an independent child reaper. Unsupported hosts refuse
before launching a command. Each Git command has 30 seconds, each gh command two minutes, and each output
stream has a 4,194,304-character limit; [software.md](software.md#release-providers-and-the-products-release-profile)
describes containment and uncertain dispatch.

Workbench's first screen reuses status and progress observations and avoids repeated full-history scans.
Unchanged bytes reuse parsing and hashes; opening still validates every record once. Large estates can
still take time, and headless checks establish no fixed native startup-time bound.

## Worker prerequisites and holding limits

The native inventory baselines are codex-cli `0.159.0-alpha.12.1` and Claude Code `2.1.296`.
An authorized human may record a qualification for another exact version using `worker qualify`; all local
probes and the admitted inventory must still match. Generic worker dispatch is refused.
Qualification checks the launcher's protocol and isolation boundaries, not live inference or sign-in.
Qualification is machine-local: its API route refuses paired local-network devices before probes or writes;
local socket and signed loopback requests still require human qualification authority.

Before a native run, set model and effort defaults on the worker's machine. Replace the model and effort
values with your chosen model selector and an effort supported by that launcher:

```sh
dotnet vc worker configure --launcher codex --model <model> --effort <effort>
dotnet vc worker configure --launcher codex
```

Use `--launcher claude-code` for Claude Code. Both choices are required when setting; omitting them shows
the configuration (`--json` is also available). Repeat `--allow-model` or `--allow-effort` to narrow allowed
choices, including each default. With no model allowlist only the default model is allowed; with no effort
allowlist the launcher's supported efforts are allowed. Setting replaces that launcher's defaults and
allowlists. It never widens the qualified inventory or adds tools, servers, variables or routes.

Codex accepts `none`, `minimal`, `low`, `medium`, `high`, `xhigh` and `max` in 0.7.2. For example, `dotnet vc worker configure --launcher codex --model gpt-6-astra --effort max` stores
that selection. The inventory probes do not validate a model's effort support; the harness validates it at launch.
An observed nonzero exit, including an effort refusal, is recorded as `known-failure`. See the
[execution guide](software.md#explicit-execution-and-environment-admission-run-01-070) for the admission and usage limits.

Configuration is per machine and is never recorded in the estate. The owner-only files live at
`worker/codex.json` and `worker/claude-code.json` under the user's Vericairn home. Reads and writes refuse
when that home is inside a Git repository or an estate. If your home directory is a repository, set
`VERICAIRN_HOME` to a machine-local directory outside every repository and estate for the CLI and daemon
before starting them. The CLI and daemon reread configuration for each run and agent goal step; changing
its choices needs no daemon restart. Missing or invalid configuration refuses before intent and never
falls back to ambient harness choices. See the
[software guide](software.md#explicit-execution-and-environment-admission-run-01-070) for precedence and admission.

A home holding `.claude/settings.json` or `.codex/config.toml` is an ancestor of the default workspace,
so the native configuration scan refuses runs there. Choose an absolute directory outside that home:

```sh
dotnet vc worker configure --workspaces <absolute-directory>
dotnet vc worker configure
```

Omit `--launcher` for this setting. It applies to every launcher on the machine and is stored in the
owner-only `worker/workspaces.json` under the Vericairn home. The root must be outside Git repositories
(including bare repositories) and estates, and cannot itself be a symbolic link or reparse point. Linked
ancestors are resolved and the canonical root is stored. The canonical root and its ancestors cannot hold
`.claude`, `.codex`, `.agents` or `.mcp.json`; refusal names what it found.
A new root is created owner-only. Unknown fields and duplicate keys refuse; an invalid file never falls
back to the default. The CLI and daemon reread it for each run, without a restart, and recheck before dispatch.

New workspaces go under `<root>/<estate>/<branch-slug>`; slashes in the branch become hyphens.
With no root configured, the location remains `<VericairnHome>/estates/<estate>/workspaces/<branch-slug>`.
An existing worktree for the branch is reused anywhere under the selected root. A worktree elsewhere is
refused with its location: preserve its work, then use `git worktree remove` with that path from the
registered clone, or select a root containing it. Changing the root does not move a worktree.
Workspace paths stay local and never enter records. Moving workspaces changes their ancestors; separately
discovered user skills and managed configuration still face the same native checks.
In particular, a Codex run is refused while the user's own `~/.agents` contains skills or other customization,
even with a workspace outside that home and a fresh `CODEX_HOME`. Use a worker account without this discovery
root. The native scan allows an empty ordinary directory or one containing only recognized instruction files;
it refuses other entries, child directories and uninspected links. Workspace-root validation itself refuses any `.agents`
marker in the root or its ancestors, including an empty one.

FIX-12 checks the current holder of the work order for every run, before preparing its workspace and again
before recording intent. A run on an assignment displaced by hand-over is refused, whether the assignment
was named explicitly or routed, without an intent or charge. Existing runs still need observed containment.

The Codex launcher enables the ordinary code-mode tool host and pre-approves only the estate MCP server's
tools; the server still enforces the acting principal's authority. Global approval policy is unset.
Five live runs exercised the fixes; the fifth recorded cap, max execution, usage, signed attempt/result
and exactly the permitted file change. They do not qualify other accounts, versions, platforms or billing.
A worker-launched Codex run cannot yet commit: its git worktree metadata lies outside the sandbox.
Changes stay uncommitted in the worktree for review; FIX-19 makes the workspace a repository of its own.
The brief must contain at least one interpretable permitted path pattern; prose alone refuses before intent.

The efficiency Adapter is absent until the planned 0.8.0 work; enabling it is refused. Remote is not built.
Context payloads are measured in bytes; tokens and model fit remain unknown without a qualified local tokenizer.
